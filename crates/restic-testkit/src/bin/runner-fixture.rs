//! Simuliert Restic-Prozesse für Runner-Tests (Gegenstück zu `--runner-fixture` in C#).
//!
//! Aufruf: `runner-fixture <modus> <pidfile>`. Der Prozess schreibt seine PID in die Datei,
//! damit Tests prüfen können, ob er nach Abbruch oder frühem Ende wirklich beendet wurde.

use std::io::{self, Write};
use std::time::Duration;
use std::{env, fs, process, thread};

fn flood_stderr() {
    thread::spawn(|| {
        let line = "e".repeat(4096);
        loop {
            if writeln!(io::stderr(), "{line}").is_err() {
                break;
            }
        }
    });
}

fn print(text: &str) {
    let mut out = io::stdout();
    let _ = out.write_all(text.as_bytes());
    let _ = out.flush();
}

fn sleep_forever() -> ! {
    loop {
        thread::sleep(Duration::from_secs(3600));
    }
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let mode = args.get(1).map(String::as_str).unwrap_or("hang");
    if let Some(pid_path) = args.get(2) {
        let _ = fs::write(pid_path, process::id().to_string());
        start_heartbeat(format!("{pid_path}.alive"));
    }
    match mode {
        "failed" => {
            let _ = writeln!(io::stderr(), "Testprozess meldet einen regulären Fehler.");
            process::exit(42);
        }
        // Vollständige, normal endende Ausgaben.
        "lines" => {
            print("erste Zeile\nzweite Zeile\r\nÜbung mit Umlauten\n");
        }
        "array-complete" => {
            print("[{\"value\":1},{\"value\":2},{\"value\":3}]\n");
        }
        "find-complete" => {
            print(
                "[{\"matches\":[{\"name\":\"a.txt\"},{\"name\":\"b.txt\"}],\"snapshot\":\"s1\"},{\"matches\":[{\"name\":\"ä.txt\"}],\"snapshot\":\"s2\"}]\n",
            );
        }
        "empty" => {}
        "binary" => {
            let bytes: Vec<u8> = (0..1000u32).map(|i| (i % 256) as u8).collect();
            let mut out = io::stdout();
            let _ = out.write_all(&bytes);
            let _ = out.flush();
        }
        "stderr-flood-exit" => {
            let chunk = "f".repeat(1024);
            for _ in 0..1024 {
                let _ = writeln!(io::stderr(), "{chunk}");
            }
            process::exit(3);
        }
        // Stdout endet mit dem Prozess, bevor das Array vollständig ist.
        "partial-exit-error" => {
            print("[{\"value\":1},");
            thread::sleep(Duration::from_millis(150));
            let _ = writeln!(io::stderr(), "abgebrochen");
            process::exit(7);
        }
        "partial-exit-ok" => {
            print("[{\"value\":1},");
            thread::sleep(Duration::from_millis(150));
            process::exit(0);
        }
        // Laufende Prozesse, die nicht von selbst enden und stderr fluten.
        "array" => {
            print("[{\"value\":1},");
            flood_stderr();
            sleep_forever();
        }
        "find" => {
            print("[{\"matches\":[{\"name\":\"first.txt\"},");
            flood_stderr();
            sleep_forever();
        }
        "invalid" => {
            print("!\n");
            flood_stderr();
            let line = "x".repeat(4096);
            loop {
                if writeln!(io::stdout(), "{line}").is_err() {
                    break;
                }
            }
            sleep_forever();
        }
        "env" => {
            print(&format!("{}\n", env::var("RB_TEST_VALUE").unwrap_or_default()));
        }
        _ => {
            print("Ausgabe eines laufenden Prozesses\n");
            flood_stderr();
            sleep_forever();
        }
    }
}

/// Schreibt regelmäßig einen Zähler in eine Datei. Tests erkennen am ausbleibenden Takt,
/// dass der Prozess beendet wurde (schneller und portabler als eine Prozessabfrage).
fn start_heartbeat(path: String) {
    thread::spawn(move || {
        let mut tick: u64 = 0;
        loop {
            tick += 1;
            let _ = fs::write(&path, tick.to_string());
            thread::sleep(Duration::from_millis(40));
        }
    });
}
