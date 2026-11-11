//! Prozess-Tests für den Runner (Phase 1). Gegenstücke zu `ReliabilityTests`, `StreamingTests`
//! und den Runner-Tests der C#-Linie.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use restic_core::{Flow, ResticCommand, ResticError, ResticProcessRunner};
use serde::Deserialize;
use tokio_util::sync::CancellationToken;

static COUNTER: AtomicUsize = AtomicUsize::new(0);

struct Fixture {
    command: ResticCommand,
    pid_file: PathBuf,
}

fn fixture(mode: &str) -> Fixture {
    let id = COUNTER.fetch_add(1, Ordering::Relaxed);
    let pid_file = std::env::temp_dir().join(format!("rb-runner-{}-{id}.pid", std::process::id()));
    let _ = std::fs::remove_file(&pid_file);
    let command = ResticCommand::new(
        env!("CARGO_BIN_EXE_runner-fixture"),
        vec![mode.to_string(), pid_file.to_string_lossy().into_owned()],
    );
    Fixture { command, pid_file }
}

impl Fixture {
    fn heartbeat_file(&self) -> PathBuf {
        PathBuf::from(format!("{}.alive", self.pid_file.display()))
    }

    fn heartbeat(&self) -> Option<u64> {
        std::fs::read_to_string(self.heartbeat_file())
            .ok()?
            .trim()
            .parse()
            .ok()
    }

    /// Der Prozess gilt als beendet, wenn sein Heartbeat über 400 ms nicht mehr weiterläuft.
    async fn assert_process_gone(&self) {
        for _ in 0..50 {
            let before = self.heartbeat();
            tokio::time::sleep(Duration::from_millis(400)).await;
            let after = self.heartbeat();
            if before == after {
                let _ = std::fs::remove_file(&self.pid_file);
                let _ = std::fs::remove_file(self.heartbeat_file());
                return;
            }
        }
        panic!(
            "Prozess läuft noch (Heartbeat in {} schreitet fort)",
            self.heartbeat_file().display()
        );
    }
}

#[derive(Debug, Deserialize)]
struct Item {
    value: i32,
}

fn runner() -> ResticProcessRunner {
    ResticProcessRunner::new()
}

fn stop() -> CancellationToken {
    CancellationToken::new()
}

#[tokio::test]
async fn regular_failure_reports_exit_code_and_stderr() {
    let f = fixture("failed");
    let result = runner().run(&f.command, &stop()).await.unwrap();
    assert_eq!(result.exit_code, 42);
    assert!(result.standard_error.contains("regulären Fehler"));
    f.assert_process_gone().await;
}

#[tokio::test]
async fn lines_are_delivered_without_line_endings() {
    let f = fixture("lines");
    let mut lines = Vec::new();
    let result = runner()
        .run_lines(
            &f.command,
            |line| {
                lines.push(line);
                std::future::ready(Ok(()))
            },
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!(result.exit_code, 0);
    assert_eq!(lines, ["erste Zeile", "zweite Zeile", "Übung mit Umlauten"]);
}

#[tokio::test]
async fn run_collects_standard_output() {
    let f = fixture("lines");
    let result = runner().run(&f.command, &stop()).await.unwrap();
    assert_eq!(
        result.standard_output,
        "erste Zeile\nzweite Zeile\nÜbung mit Umlauten\n"
    );
}

#[tokio::test]
async fn environment_is_passed_to_the_process_only() {
    let f = fixture("env");
    let command = f.command.clone().with_environment(HashMap::from([(
        "RB_TEST_VALUE".to_string(),
        "geheim-123".to_string(),
    )]));
    let result = runner().run(&command, &stop()).await.unwrap();
    assert_eq!(result.standard_output.trim(), "geheim-123");
    assert!(
        std::env::var("RB_TEST_VALUE").is_err(),
        "Umgebung des Aufrufers bleibt unverändert"
    );
}

#[tokio::test]
async fn missing_executable_gives_german_error() {
    let command = ResticCommand::new("dieses-programm-gibt-es-nicht-xyz", vec![]);
    let error = runner().run(&command, &stop()).await.unwrap_err();
    assert!(
        error
            .to_string()
            .starts_with("Restic konnte nicht gestartet werden"),
        "{error}"
    );
    assert!(!error.is_cancelled());
}

#[tokio::test]
async fn already_cancelled_token_does_not_start_a_process() {
    let f = fixture("lines");
    let cancelled = stop();
    cancelled.cancel();
    let error = runner().run(&f.command, &cancelled).await.unwrap_err();
    assert!(error.is_cancelled());
    assert!(
        !Path::new(&f.pid_file).exists(),
        "es darf kein Prozess gestartet worden sein"
    );
}

#[tokio::test]
async fn user_cancel_kills_the_process_and_reports_cancelled() {
    let f = fixture("hang");
    let cancel = stop();
    let canceller = cancel.clone();
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(500)).await;
        canceller.cancel();
    });
    let error = runner().run(&f.command, &cancel).await.unwrap_err();
    assert!(
        error.is_cancelled(),
        "Abbruch bleibt Abbruch, nicht Fehler: {error}"
    );
    f.assert_process_gone().await;
}

#[tokio::test]
async fn json_array_is_streamed_completely() {
    let f = fixture("array-complete");
    let mut values = Vec::new();
    let result = runner()
        .run_json_array(
            &f.command,
            |item: Item| {
                values.push(item.value);
                std::future::ready(Ok(()))
            },
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!(values, [1, 2, 3]);
    assert_eq!(result.exit_code, 0);
    assert!(!result.stopped_early);
}

#[tokio::test]
async fn json_array_stops_restic_after_early_end() {
    let f = fixture("array");
    let mut seen = 0;
    let result = tokio::time::timeout(
        Duration::from_secs(20),
        runner().run_json_array_until(
            &f.command,
            |_: Item| {
                seen += 1;
                std::future::ready(Ok(Flow::Stop))
            },
            &stop(),
        ),
    )
    .await
    .expect("kein Hänger")
    .unwrap();
    assert_eq!(seen, 1);
    assert!(result.stopped_early, "absichtliches Ende ist kein Fehler");
    f.assert_process_gone().await;
}

#[tokio::test]
async fn find_matches_arrive_before_the_group_ends_and_stop_early() {
    let f = fixture("find");
    let mut names = Vec::new();
    let result = tokio::time::timeout(
        Duration::from_secs(20),
        runner().run_find_matches_until(
            &f.command,
            |node| {
                names.push(node.name);
                std::future::ready(Ok(Flow::Stop))
            },
            &stop(),
        ),
    )
    .await
    .expect("kein Hänger")
    .unwrap();
    assert_eq!(names, ["first.txt"]);
    assert!(result.stopped_early);
    f.assert_process_gone().await;
}

#[tokio::test]
async fn find_matches_of_all_groups_are_read_in_order() {
    let f = fixture("find-complete");
    let mut names = Vec::new();
    let result = runner()
        .run_find_matches_until(
            &f.command,
            |node| {
                names.push(node.name);
                std::future::ready(Ok(Flow::Continue))
            },
            &stop(),
        )
        .await
        .unwrap();
    assert_eq!(names, ["a.txt", "b.txt", "ä.txt"]);
    assert!(!result.stopped_early);
}

#[tokio::test]
async fn invalid_output_kills_the_process_and_reports_german_error() {
    let f = fixture("invalid");
    let error = tokio::time::timeout(
        Duration::from_secs(20),
        runner().run_json_array(&f.command, |_: Item| std::future::ready(Ok(())), &stop()),
    )
    .await
    .expect("kein Hänger")
    .unwrap_err();
    assert!(error.to_string().contains("ungültig"), "{error}");
    f.assert_process_gone().await;
}

#[tokio::test]
async fn callback_error_kills_the_process_and_is_propagated() {
    let f = fixture("array");
    let error = tokio::time::timeout(
        Duration::from_secs(20),
        runner().run_json_array(
            &f.command,
            |_: Item| std::future::ready(Err(ResticError::new("Callback gescheitert"))),
            &stop(),
        ),
    )
    .await
    .expect("kein Hänger")
    .unwrap_err();
    assert_eq!(error.to_string(), "Callback gescheitert");
    f.assert_process_gone().await;
}

#[tokio::test]
async fn eof_before_complete_array_waits_for_exit_code() {
    // Exitcode ≠ 0: wird gemeldet, kein Parse-Fehler.
    let failing = fixture("partial-exit-error");
    let result = runner()
        .run_json_array(&failing.command, |_: Item| std::future::ready(Ok(())), &stop())
        .await
        .unwrap();
    assert_eq!(result.exit_code, 7);
    assert!(result.standard_error.contains("abgebrochen"));

    // Exitcode 0: unvollständige Ausgabe ist ein Fehler.
    let ok = fixture("partial-exit-ok");
    let error = runner()
        .run_json_array(&ok.command, |_: Item| std::future::ready(Ok(())), &stop())
        .await
        .unwrap_err();
    assert!(error.to_string().contains("unvollständig"), "{error}");

    // Leere Ausgabe mit Exitcode 0 ebenfalls.
    let empty = fixture("empty");
    assert!(
        runner()
            .run_json_array(&empty.command, |_: Item| std::future::ready(Ok(())), &stop())
            .await
            .is_err()
    );
}

#[tokio::test]
async fn run_json_returns_value_or_none_on_failure() {
    #[derive(Debug, Deserialize)]
    struct List(#[allow(dead_code)] Vec<Item>);
    let f = fixture("array-complete");
    let result = runner().run_json::<List>(&f.command, &stop()).await.unwrap();
    assert_eq!(result.exit_code, 0);
    assert!(result.value.is_some());

    let failing = fixture("partial-exit-error");
    let result = runner()
        .run_json::<List>(&failing.command, &stop())
        .await
        .unwrap();
    assert_eq!(result.exit_code, 7);
    assert!(result.value.is_none());
}

#[tokio::test]
async fn binary_output_keeps_original_bytes_and_is_limited() {
    let f = fixture("binary");
    let result = runner().run_binary(&f.command, 2000, &stop()).await.unwrap();
    assert_eq!(result.standard_output.len(), 1000);
    assert_eq!(result.standard_output[255], 255);
    assert_eq!(result.standard_output[256], 0);

    let limited = fixture("binary");
    let error = runner()
        .run_binary(&limited.command, 500, &stop())
        .await
        .unwrap_err();
    assert!(error.to_string().contains("erlaubte Größe"), "{error}");
    limited.assert_process_gone().await;
}

#[tokio::test]
async fn standard_error_is_bounded_but_fully_drained() {
    let f = fixture("stderr-flood-exit");
    let result = tokio::time::timeout(Duration::from_secs(30), runner().run(&f.command, &stop()))
        .await
        .expect("stderr wird vollständig geleert, der Prozess hängt nicht")
        .unwrap();
    assert_eq!(result.exit_code, 3);
    assert!(result.standard_error.len() <= 64 * 1024);
    assert!(result.standard_error.len() > 60 * 1024);
}
