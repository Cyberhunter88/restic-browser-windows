//! Täuscht `restic version --json` vor. Das Verhalten steht in der Datei `<eigener Pfad>.mode`
//! (Standard: `ok`), damit Tests dieselbe Binärdatei mehrfach unter verschiedenen Pfaden und
//! Modi ablegen können (Gegenstück zu den Fake-Dateien der C#-Tests).
//!
//! Jeder Start wird in `<eigener Pfad>.log` vermerkt. Im Modus `hang` schreibt der Prozess
//! zusätzlich einen Heartbeat nach `<eigener Pfad>.alive`.

use std::io::Write;
use std::time::Duration;
use std::{env, fs, process, thread};

fn main() {
    let exe = env::current_exe().expect("eigener Pfad");
    let side = |extension: &str| format!("{}.{extension}", exe.display());
    let mode = fs::read_to_string(side("mode"))
        .map(|m| m.trim().to_string())
        .unwrap_or_else(|_| "ok".into());
    if let Ok(mut log) = fs::OpenOptions::new().create(true).append(true).open(side("log")) {
        let _ = writeln!(log, "start");
    }

    let args: Vec<String> = env::args().skip(1).collect();
    if args != ["version", "--json"] {
        let _ = writeln!(std::io::stderr(), "Unerwartete Argumente: {args:?}");
        process::exit(2);
    }
    let output = match mode.as_str() {
        "ok" => r#"{"version":"0.19.1","unknown":true}"#,
        "old" => r#"{"version":"0.16.0"}"#,
        "badjson" => "not json",
        "null" => "null",
        "empty-object" => "{}",
        "brace" => "{",
        "null-version" => r#"{"version":null}"#,
        "fail" => {
            let _ = writeln!(std::io::stderr(), "private raw output");
            process::exit(1);
        }
        "hang" => {
            let alive = side("alive");
            let mut tick = 0u64;
            loop {
                tick += 1;
                let _ = fs::write(&alive, tick.to_string());
                thread::sleep(Duration::from_millis(40));
            }
        }
        other => {
            let _ = writeln!(std::io::stderr(), "Unbekannter Modus {other}");
            process::exit(3);
        }
    };
    println!("{output}");
}
