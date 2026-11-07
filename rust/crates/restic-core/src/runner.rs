//! Asynchroner Prozess-Runner für Restic.
//!
//! Regeln (identisch zur C#-Linie 3.x):
//! - Start immer ohne Shell über Argumentlisten; Geheimnisse nur in der Prozessumgebung.
//! - Streams werden frühzeitig beendet, sobald das Ergebnis vollständig ist. Der Prozess wird
//!   dann beendet, stdout/stderr werden geleert, und das gilt nicht als Fehler.
//! - Ein Benutzerabbruch (`CancellationToken`) bleibt ein Abbruch ([`ResticError::Cancelled`]).
//! - Fehler in Callbacks oder ungültige Ausgabe beenden den Prozess sofort und räumen auf.
//! - Endet stdout vor einem vollständigen JSON-Array, wird erst der Exitcode abgewartet:
//!   bei Exitcode ≠ 0 wird er gemeldet, bei Exitcode 0 ist die Ausgabe ein Fehler.

use std::collections::HashMap;
use std::future::Future;
use std::path::PathBuf;
use std::process::Stdio;

use serde::de::DeserializeOwned;
use tokio::io::{AsyncBufReadExt, AsyncReadExt, BufReader};
use tokio::process::{Child, ChildStderr, ChildStdout, Command};
use tokio_util::sync::CancellationToken;

use crate::error::ResticError;
use crate::json_stream::{JsonScanner, ScanMode};
use crate::models::BackupNode;

const MAX_STANDARD_ERROR_BYTES: usize = 64 * 1024;
const READ_BUFFER_BYTES: usize = 64 * 1024;

/// Auszuführender Prozess. `environment` wird zusätzlich zur geerbten Umgebung gesetzt.
#[derive(Debug, Clone, Default)]
pub struct ResticCommand {
    pub executable: String,
    pub arguments: Vec<String>,
    pub environment: HashMap<String, String>,
    pub working_directory: Option<PathBuf>,
}

impl ResticCommand {
    pub fn new(executable: impl Into<String>, arguments: Vec<String>) -> Self {
        Self {
            executable: executable.into(),
            arguments,
            ..Self::default()
        }
    }

    pub fn with_environment(mut self, environment: HashMap<String, String>) -> Self {
        self.environment = environment;
        self
    }

    pub fn with_working_directory(mut self, directory: impl Into<PathBuf>) -> Self {
        self.working_directory = Some(directory.into());
        self
    }
}

#[derive(Debug, Clone)]
pub struct ProcessResult {
    pub exit_code: i32,
    pub standard_output: String,
    pub standard_error: String,
    pub stopped_early: bool,
}

#[derive(Debug, Clone)]
pub struct BinaryResult {
    pub exit_code: i32,
    pub standard_output: Vec<u8>,
    pub standard_error: String,
}

#[derive(Debug, Clone)]
pub struct JsonResult<T> {
    pub exit_code: i32,
    /// `None`, wenn Restic mit Fehler endete und keine gültige Ausgabe lieferte.
    pub value: Option<T>,
    pub standard_error: String,
}

/// Rückgabe eines Callbacks: weiterlesen oder den Stream vorzeitig beenden.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Flow {
    Continue,
    Stop,
}

#[derive(Debug, Clone, Copy, Default)]
pub struct ResticProcessRunner;

struct ReadOutput<T> {
    value: T,
    stop_process: bool,
}

struct CoreResult<T> {
    exit_code: i32,
    value: T,
    standard_error: String,
}

enum ScanOutcome {
    Complete,
    Stopped,
    Incomplete,
}

impl ResticProcessRunner {
    pub fn new() -> Self {
        Self
    }

    /// Liest stdout zeilenweise und ruft für jede Zeile den Callback auf.
    pub async fn run_lines<F, Fut>(
        &self,
        command: &ResticCommand,
        mut on_line: F,
        stop: &CancellationToken,
    ) -> Result<ProcessResult, ResticError>
    where
        F: FnMut(String) -> Fut,
        Fut: Future<Output = Result<(), ResticError>>,
    {
        let on_line = &mut on_line;
        let core = run_core(command, stop, move |stdout| async move {
            let mut reader = BufReader::new(stdout);
            let mut raw = Vec::new();
            loop {
                raw.clear();
                let count = reader.read_until(b'\n', &mut raw).await.map_err(read_error)?;
                if count == 0 {
                    break;
                }
                let text = String::from_utf8_lossy(&raw);
                on_line(text.trim_end_matches(['\n', '\r']).to_string()).await?;
            }
            Ok(ReadOutput {
                value: (),
                stop_process: false,
            })
        })
        .await?;
        Ok(ProcessResult {
            exit_code: core.exit_code,
            standard_output: String::new(),
            standard_error: core.standard_error,
            stopped_early: false,
        })
    }

    /// Führt den Prozess aus und sammelt stdout (jede Zeile mit `\n`).
    pub async fn run(
        &self,
        command: &ResticCommand,
        stop: &CancellationToken,
    ) -> Result<ProcessResult, ResticError> {
        let mut collected = String::new();
        let collected_ref = &mut collected;
        let result = self
            .run_lines(
                command,
                |line| {
                    collected_ref.push_str(&line);
                    collected_ref.push('\n');
                    std::future::ready(Ok(()))
                },
                stop,
            )
            .await?;
        Ok(ProcessResult {
            standard_output: collected,
            ..result
        })
    }

    /// Liest stdout als Rohbytes und bricht ab, sobald mehr als `maximum_output_bytes` anfallen.
    pub async fn run_binary(
        &self,
        command: &ResticCommand,
        maximum_output_bytes: usize,
        stop: &CancellationToken,
    ) -> Result<BinaryResult, ResticError> {
        let core = run_core(command, stop, move |mut stdout| async move {
            let mut output = Vec::with_capacity(maximum_output_bytes.min(READ_BUFFER_BYTES));
            let mut buffer = vec![0u8; READ_BUFFER_BYTES];
            loop {
                let count = stdout.read(&mut buffer).await.map_err(read_error)?;
                if count == 0 {
                    break;
                }
                if output.len() + count > maximum_output_bytes {
                    return Err(ResticError::new(format!(
                        "Die Vorschau überschreitet die erlaubte Größe von {} MB.",
                        maximum_output_bytes / (1024 * 1024)
                    )));
                }
                output.extend_from_slice(&buffer[..count]);
            }
            Ok(ReadOutput {
                value: output,
                stop_process: false,
            })
        })
        .await?;
        Ok(BinaryResult {
            exit_code: core.exit_code,
            standard_output: core.value,
            standard_error: core.standard_error,
        })
    }

    /// Liest die gesamte Ausgabe als ein JSON-Dokument.
    pub async fn run_json<T: DeserializeOwned>(
        &self,
        command: &ResticCommand,
        stop: &CancellationToken,
    ) -> Result<JsonResult<T>, ResticError> {
        let core = run_core(command, stop, move |mut stdout| async move {
            let mut bytes = Vec::new();
            stdout.read_to_end(&mut bytes).await.map_err(read_error)?;
            Ok(ReadOutput {
                value: serde_json::from_slice::<T>(&bytes).ok(),
                stop_process: false,
            })
        })
        .await?;
        if core.value.is_none() && core.exit_code == 0 {
            return Err(ResticError::new(
                "Die Ausgabe von Restic ist unvollständig oder ungültig.",
            ));
        }
        Ok(JsonResult {
            exit_code: core.exit_code,
            value: core.value,
            standard_error: core.standard_error,
        })
    }

    /// Verarbeitet ein JSON-Array Element für Element und beendet Restic, sobald der Callback
    /// [`Flow::Stop`] liefert.
    pub async fn run_json_array_until<T, F, Fut>(
        &self,
        command: &ResticCommand,
        on_item: F,
        stop: &CancellationToken,
    ) -> Result<ProcessResult, ResticError>
    where
        T: DeserializeOwned,
        F: FnMut(T) -> Fut,
        Fut: Future<Output = Result<Flow, ResticError>>,
    {
        run_scanned(
            command,
            ScanMode::TopLevelArray,
            "Die Ausgabe von Restic ist unvollständig oder ungültig.",
            on_item,
            stop,
        )
        .await
    }

    /// Wie [`Self::run_json_array_until`], aber ohne vorzeitiges Ende.
    pub async fn run_json_array<T, F, Fut>(
        &self,
        command: &ResticCommand,
        mut on_item: F,
        stop: &CancellationToken,
    ) -> Result<ProcessResult, ResticError>
    where
        T: DeserializeOwned,
        F: FnMut(T) -> Fut,
        Fut: Future<Output = Result<(), ResticError>>,
    {
        self.run_json_array_until(
            command,
            |item: T| {
                let next = on_item(item);
                async move { next.await.map(|()| Flow::Continue) }
            },
            stop,
        )
        .await
    }

    /// Liest einzelne Treffer aus `restic find --json`, auch innerhalb sehr großer Snapshot-Gruppen.
    pub async fn run_find_matches_until<F, Fut>(
        &self,
        command: &ResticCommand,
        on_match: F,
        stop: &CancellationToken,
    ) -> Result<ProcessResult, ResticError>
    where
        F: FnMut(BackupNode) -> Fut,
        Fut: Future<Output = Result<Flow, ResticError>>,
    {
        run_scanned(
            command,
            ScanMode::FindMatches,
            "Die Suchausgabe von Restic ist unvollständig oder ungültig.",
            on_match,
            stop,
        )
        .await
    }
}

async fn run_scanned<T, F, Fut>(
    command: &ResticCommand,
    mode: ScanMode,
    invalid_message: &'static str,
    mut on_item: F,
    stop: &CancellationToken,
) -> Result<ProcessResult, ResticError>
where
    T: DeserializeOwned,
    F: FnMut(T) -> Fut,
    Fut: Future<Output = Result<Flow, ResticError>>,
{
    let on_item = &mut on_item;
    let core = run_core(command, stop, move |mut stdout| async move {
        let mut scanner = JsonScanner::new(mode);
        let mut buffer = vec![0u8; READ_BUFFER_BYTES];
        let mut elements = Vec::new();
        loop {
            let count = stdout.read(&mut buffer).await.map_err(read_error)?;
            if count == 0 {
                break;
            }
            elements.clear();
            scanner
                .push(&buffer[..count], &mut elements)
                .map_err(|_| ResticError::new(invalid_message))?;
            for element in elements.drain(..) {
                let item =
                    serde_json::from_slice::<T>(&element).map_err(|_| ResticError::new(invalid_message))?;
                if on_item(item).await? == Flow::Stop {
                    return Ok(ReadOutput {
                        value: ScanOutcome::Stopped,
                        stop_process: true,
                    });
                }
            }
        }
        let outcome = if scanner.is_complete() {
            ScanOutcome::Complete
        } else {
            ScanOutcome::Incomplete
        };
        Ok(ReadOutput {
            value: outcome,
            stop_process: false,
        })
    })
    .await?;
    if matches!(core.value, ScanOutcome::Incomplete) && core.exit_code == 0 {
        return Err(ResticError::new(invalid_message));
    }
    Ok(ProcessResult {
        exit_code: core.exit_code,
        standard_output: String::new(),
        standard_error: core.standard_error,
        stopped_early: matches!(core.value, ScanOutcome::Stopped),
    })
}

fn read_error(error: std::io::Error) -> ResticError {
    ResticError::new(format!(
        "Die Ausgabe von Restic konnte nicht gelesen werden: {error}"
    ))
}

async fn run_core<T, F, Fut>(
    command: &ResticCommand,
    stop: &CancellationToken,
    read: F,
) -> Result<CoreResult<T>, ResticError>
where
    F: FnOnce(ChildStdout) -> Fut,
    Fut: Future<Output = Result<ReadOutput<T>, ResticError>>,
{
    if stop.is_cancelled() {
        return Err(ResticError::Cancelled);
    }
    let mut child = spawn(command)?;
    let stdout = child.stdout.take().expect("stdout ist als Pipe angefordert");
    let stderr = child.stderr.take().expect("stderr ist als Pipe angefordert");
    // stderr wird immer bis zum Ende geleert, damit der Prozess nie an einer vollen Pipe hängt.
    let stderr_task = tokio::spawn(read_standard_error(stderr));

    let outcome = tokio::select! {
        biased;
        () = stop.cancelled() => Err(ResticError::Cancelled),
        result = read(stdout) => result,
    };

    match outcome {
        Ok(read_output) => {
            if read_output.stop_process {
                stop_process(&mut child).await;
            }
            let status = tokio::select! {
                biased;
                () = stop.cancelled() => None,
                status = child.wait() => Some(status),
            };
            let Some(status) = status else {
                stop_process(&mut child).await;
                let _ = stderr_task.await;
                return Err(ResticError::Cancelled);
            };
            let standard_error = stderr_task.await.unwrap_or_default();
            if stop.is_cancelled() {
                return Err(ResticError::Cancelled);
            }
            let exit_code = status
                .map_err(|e| ResticError::new(format!("Restic wurde unerwartet beendet: {e}")))?
                .code()
                .unwrap_or(-1);
            Ok(CoreResult {
                exit_code,
                value: read_output.value,
                standard_error,
            })
        }
        Err(error) => {
            stop_process(&mut child).await;
            let _ = stderr_task.await;
            if stop.is_cancelled() {
                Err(ResticError::Cancelled)
            } else {
                Err(error)
            }
        }
    }
}

fn spawn(command: &ResticCommand) -> Result<Child, ResticError> {
    let mut process = Command::new(&command.executable);
    process
        .args(&command.arguments)
        .envs(&command.environment)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true);
    if let Some(directory) = command
        .working_directory
        .as_ref()
        .filter(|d| !d.as_os_str().is_empty())
    {
        process.current_dir(directory);
    }
    #[cfg(windows)]
    process.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    // Unter Linux meldet exec kurz „Text file busy“, wenn ein paralleler fork noch den
    // Schreib-Deskriptor einer gerade geschriebenen Datei hält; wenige kurze Wiederholungen genügen.
    let mut attempts = 0;
    loop {
        match process.spawn() {
            Ok(child) => return Ok(child),
            Err(e) if e.kind() == std::io::ErrorKind::ExecutableFileBusy && attempts < 20 => {
                attempts += 1;
                std::thread::sleep(std::time::Duration::from_millis(10));
            }
            Err(e) => {
                return Err(ResticError::new(format!(
                    "Restic konnte nicht gestartet werden: {e}"
                )));
            }
        }
    }
}

/// Beendet den Prozess und wartet darauf. Fehler sind hier bewusst unerheblich.
async fn stop_process(child: &mut Child) {
    let _ = child.start_kill();
    let _ = child.wait().await;
}

async fn read_standard_error(mut stderr: ChildStderr) -> String {
    let mut kept = Vec::new();
    let mut buffer = [0u8; 4096];
    loop {
        match stderr.read(&mut buffer).await {
            Ok(0) | Err(_) => break,
            Ok(count) => {
                let room = MAX_STANDARD_ERROR_BYTES.saturating_sub(kept.len());
                if room > 0 {
                    kept.extend_from_slice(&buffer[..count.min(room)]);
                }
            }
        }
    }
    String::from_utf8_lossy(&kept).into_owned()
}
