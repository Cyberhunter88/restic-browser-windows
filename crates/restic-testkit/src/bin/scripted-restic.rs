//! Skriptgesteuertes Restic für Service-Tests. Aufruf wie Restic: `--repo <pfad> <befehl> …`.
//!
//! Das Verhalten steht in `<eigener Pfad>.script` (JSON): ein Eintrag je Befehl, `*` als Standard.
//! Felder: `stdout`, `stdout_hex`, `stderr`, `exit`, `gen` (`name:anzahl`), `hang_after`.
//! Protokolle: `<pfad>.log` (je Start eine Zeile mit dem Befehl), `<pfad>.args.<befehl>` (Argumente
//! als JSON), `<pfad>.password` (Wert von `RESTIC_PASSWORD`), `<pfad>.alive` (Heartbeat bei `hang_after`).

use std::io::{self, BufWriter, Write};
use std::time::Duration;
use std::{env, fs, process, thread};

use serde_json::Value;

fn write_all(out: &mut impl Write, text: &str) {
    if out.write_all(text.as_bytes()).is_err() {
        // Leser hat den Stream beendet (frühes Ende): Prozess ohne Fehlermeldung verlassen.
        process::exit(0);
    }
}

fn generate(kind: &str, count: usize, out: &mut impl Write) {
    match kind {
        "ls" => {
            for i in 0..count {
                write_all(
                    out,
                    &format!(
                        "{{\"message_type\":\"node\",\"name\":\"{i:04}.txt\",\"path\":\"/{i:04}.txt\",\"type\":\"file\",\"size\":{i}}}\n"
                    ),
                );
            }
        }
        "ls-sized" => {
            for i in 0..count {
                write_all(
                    out,
                    &format!(
                        "{{\"message_type\":\"node\",\"name\":\"{i}.txt\",\"path\":\"/ordner/{i}.txt\",\"type\":\"file\",\"size\":10}}\n"
                    ),
                );
            }
        }
        "ls-folders" => {
            for i in 0..count {
                write_all(
                    out,
                    &format!(
                        "{{\"message_type\":\"node\",\"name\":\"{i}.txt\",\"path\":\"/folder-{i}/file.txt\",\"type\":\"file\",\"size\":1}}\n"
                    ),
                );
            }
        }
        "ls-deep" => {
            for i in 0..count {
                write_all(
                    out,
                    &format!(
                        "{{\"message_type\":\"node\",\"name\":\"{i}.bin\",\"path\":\"/daten/{}/gruppe/{i}.bin\",\"type\":\"file\",\"size\":1024}}\n",
                        i % 100
                    ),
                );
            }
        }
        "find-matches" => {
            write_all(out, "[{\"snapshot\":\"snapshot\",\"matches\":[");
            for i in 0..count {
                let comma = if i == 0 { "" } else { "," };
                write_all(
                    out,
                    &format!("{comma}{{\"name\":\"{i}.txt\",\"path\":\"/{i}.txt\",\"type\":\"file\"}}"),
                );
            }
            write_all(out, "]}]\n");
        }
        "find-groups" => {
            write_all(out, "[");
            for i in 0..count {
                let comma = if i == 0 { "" } else { "," };
                write_all(
                    out,
                    &format!(
                        "{comma}{{\"snapshot\":\"snapshot-{i:05}\",\"matches\":[{{\"name\":\"file.txt\",\"path\":\"/file.txt\",\"type\":\"file\"}}]}}"
                    ),
                );
            }
            write_all(out, "]\n");
        }
        "find-groups-search" => {
            write_all(out, "[");
            for i in 0..count {
                let comma = if i == 0 { "" } else { "," };
                write_all(
                    out,
                    &format!(
                        "{comma}{{\"snapshot\":\"snapshot\",\"matches\":[{{\"name\":\"{i}.txt\",\"path\":\"/{i}.txt\",\"type\":\"file\"}}]}}"
                    ),
                );
            }
            write_all(out, "]\n");
        }
        "check-big" => {
            write_all(
                out,
                "Keine JSON-Meldung\n[]\n{\"message_type\":\"future\",\"num_errors\":\"unknown\"}\n",
            );
            let line = format!("{}😀\n", "ä".repeat(200));
            for _ in 0..count {
                write_all(out, &line);
            }
            write_all(
                out,
                "{\"num_errors\":3,\"suggest_repair_index\":true,\"suggest_prune\":true,\"extra\":1}\n",
            );
        }
        "restore-errors" => {
            for i in 0..count {
                write_all(
                    out,
                    &format!(
                        "{{\"message_type\":\"error\",\"error\":{{\"message\":\"unbekannter Fehler {i}\"}},\"during\":\"restore\"}}\n"
                    ),
                );
            }
        }
        other => {
            let _ = writeln!(io::stderr(), "Unbekannter Generator {other}");
            process::exit(3);
        }
    }
}

fn main() {
    let exe = env::current_exe().expect("eigener Pfad");
    let side = |extension: &str| format!("{}.{extension}", exe.display());
    let args: Vec<String> = env::args().skip(1).collect();
    let command = args.get(2).cloned().unwrap_or_default();

    if let Ok(mut log) = fs::OpenOptions::new().create(true).append(true).open(side("log")) {
        let _ = writeln!(log, "{command}");
    }
    let _ = fs::write(
        side(&format!("args.{command}")),
        serde_json::to_string(&args).unwrap_or_default(),
    );
    let _ = fs::write(side("password"), env::var("RESTIC_PASSWORD").unwrap_or_default());

    let script: Value = fs::read_to_string(side("script"))
        .ok()
        .and_then(|text| serde_json::from_str(&text).ok())
        .unwrap_or(Value::Null);
    let spec = script
        .get(&command)
        .or_else(|| script.get("*"))
        .cloned()
        .unwrap_or(Value::Null);

    let stdout = io::stdout();
    let mut out = BufWriter::new(stdout.lock());
    if let Some(text) = spec.get("stdout").and_then(Value::as_str) {
        write_all(&mut out, text);
    }
    if let Some(hex) = spec.get("stdout_hex").and_then(Value::as_str) {
        let bytes: Vec<u8> = (0..hex.len() / 2)
            .filter_map(|i| u8::from_str_radix(&hex[i * 2..i * 2 + 2], 16).ok())
            .collect();
        if out.write_all(&bytes).is_err() {
            process::exit(0);
        }
    }
    if let Some(generator) = spec.get("gen").and_then(Value::as_str) {
        let (kind, count) = generator.split_once(':').unwrap_or((generator, "0"));
        generate(kind, count.parse().unwrap_or(0), &mut out);
    }
    if out.flush().is_err() {
        process::exit(0);
    }
    if let Some(text) = spec.get("stderr").and_then(Value::as_str) {
        let _ = write!(io::stderr(), "{text}");
    }
    if spec.get("hang_after") == Some(&Value::Bool(true)) {
        let alive = side("alive");
        let mut tick = 0u64;
        loop {
            tick += 1;
            let _ = fs::write(&alive, tick.to_string());
            thread::sleep(Duration::from_millis(40));
        }
    }
    process::exit(spec.get("exit").and_then(Value::as_i64).unwrap_or(0) as i32);
}
