//! Suche nach Restic-Dateien in der festen Reihenfolge:
//! neben der Anwendung, `tools`, `PATH`, Systempfade (Linux) bzw. `WinGet` (nur Windows).

use std::fs;
use std::path::{Path, PathBuf};

fn executable_name(is_windows: bool) -> &'static str {
    if is_windows { "restic.exe" } else { "restic" }
}

fn separator(is_windows: bool) -> char {
    if is_windows { '\\' } else { '/' }
}

/// Fügt Pfadteile mit dem Trennzeichen der Zielplattform zusammen (unabhängig vom Host).
fn join(base: &str, part: &str, is_windows: bool) -> String {
    let separator = separator(is_windows);
    let is_separator = |c: char| c == separator || (is_windows && c == '/');
    if base.is_empty() {
        part.to_string()
    } else if base.ends_with(is_separator) {
        format!("{base}{part}")
    } else {
        format!("{base}{separator}{part}")
    }
}

fn same(a: &str, b: &str, is_windows: bool) -> bool {
    if is_windows {
        a.eq_ignore_ascii_case(b)
    } else {
        a == b
    }
}

fn push_unique(paths: &mut Vec<String>, candidate: String, is_windows: bool) {
    if !paths
        .iter()
        .any(|existing| same(existing, &candidate, is_windows))
    {
        paths.push(candidate);
    }
}

/// Alle Kandidaten in fester Reihenfolge, ohne Duplikate (unter Windows ohne Beachtung der
/// Groß-/Kleinschreibung). Der Zugriff auf das Dateisystem beschränkt sich auf die WinGet-Pakete.
pub fn candidates(
    is_windows: bool,
    base_directory: &str,
    search_path: &str,
    program_files: &str,
) -> Vec<String> {
    candidates_with(is_windows, base_directory, search_path, program_files, true)
}

/// Wie [`candidates`]; mit `include_system_locations = false` entfallen Linux-Systempfade und `WinGet`
/// (für abgeschlossene Tests und portable Installationen ohne Systemzugriff).
pub fn candidates_with(
    is_windows: bool,
    base_directory: &str,
    search_path: &str,
    program_files: &str,
    include_system_locations: bool,
) -> Vec<String> {
    let executable = executable_name(is_windows);
    let mut paths = Vec::new();
    push_unique(
        &mut paths,
        join(base_directory, executable, is_windows),
        is_windows,
    );
    push_unique(
        &mut paths,
        join(&join(base_directory, "tools", is_windows), executable, is_windows),
        is_windows,
    );
    let list_separator = if is_windows { ';' } else { ':' };
    for directory in search_path.split(list_separator).filter(|d| !d.is_empty()) {
        push_unique(&mut paths, join(directory, executable, is_windows), is_windows);
    }
    if !include_system_locations {
        return paths;
    }
    if is_windows {
        let winget = join(program_files, "WinGet", true);
        push_unique(
            &mut paths,
            join(&join(&winget, "Links", true), executable, true),
            true,
        );
        for path in winget_packages(&join(&winget, "Packages", true)) {
            push_unique(&mut paths, path, true);
        }
    } else {
        push_unique(&mut paths, "/usr/local/bin/restic".to_string(), false);
        push_unique(&mut paths, "/usr/bin/restic".to_string(), false);
    }
    paths
}

/// Findet `restic_*_windows_amd64.exe` rekursiv; Zugriffsfehler werden ignoriert.
fn winget_packages(directory: &str) -> Vec<String> {
    fn walk(directory: &Path, found: &mut Vec<String>) {
        let Ok(entries) = fs::read_dir(directory) else {
            return;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            let Ok(file_type) = entry.file_type() else {
                continue;
            };
            if file_type.is_dir() {
                walk(&path, found);
            } else if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                let lower = name.to_ascii_lowercase();
                if lower.starts_with("restic_") && lower.ends_with("_windows_amd64.exe") {
                    found.push(path.to_string_lossy().into_owned());
                }
            }
        }
    }
    let mut found = Vec::new();
    walk(Path::new(directory), &mut found);
    found.sort();
    found
}

/// Dateiidentität nach Auflösung von Verknüpfungen, um dieselbe Datei nicht doppelt zu prüfen.
pub fn identity(path: &Path) -> PathBuf {
    fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf())
}

/// Prüft, ob die Datei grundsätzlich verwendbar ist. `None` bedeutet: kein Fehler.
pub fn file_error(path: &Path) -> Option<String> {
    let Ok(metadata) = fs::metadata(path) else {
        return Some("Die Restic-Datei fehlt oder ist nicht zugänglich.".to_string());
    };
    if !metadata.is_file() {
        return Some("Die Restic-Datei fehlt oder ist nicht zugänglich.".to_string());
    }
    if metadata.len() == 0 {
        return Some("Die Restic-Datei ist leer.".to_string());
    }
    if fs::File::open(path).is_err() {
        return Some(
            "Die Restic-Datei kann nicht gelesen werden. Prüfe Pfad und Zugriffsrechte.".to_string(),
        );
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if metadata.permissions().mode() & 0o111 == 0 {
            return Some("Die Restic-Datei ist nicht ausführbar. Prüfe die Ausführungsrechte.".to_string());
        }
    }
    None
}

/// Kurzbeschreibung der Herkunft für die Anzeige.
pub fn describe(candidate: &str, base_directory: &str, is_windows: bool) -> &'static str {
    let executable = executable_name(is_windows);
    let tools = join(&join(base_directory, "tools", is_windows), executable, is_windows);
    let beside = join(base_directory, executable, is_windows);
    if same(candidate, &tools, is_windows) {
        "Mit der Anwendung ausgeliefert"
    } else if same(candidate, &beside, is_windows) {
        "Neben der Anwendung"
    } else if is_windows && candidate.to_ascii_lowercase().contains("winget") {
        "WinGet-Installation"
    } else {
        "PATH"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn windows_candidates_are_unique_ignoring_case() {
        let list = candidates(true, r"C:\App", r"C:\first;C:\FIRST;C:\first", r"C:\Programme");
        assert_eq!(list[0], r"C:\App\restic.exe");
        assert_eq!(list[1], r"C:\App\tools\restic.exe");
        assert_eq!(
            list.iter()
                .filter(|p| p.eq_ignore_ascii_case(r"C:\first\restic.exe"))
                .count(),
            1
        );
        assert!(list.iter().any(|p| p.ends_with(r"WinGet\Links\restic.exe")));
    }

    #[test]
    fn linux_candidates_keep_order_and_case_sensitivity() {
        let list = candidates(false, "/portable", "/first:/FIRST:/first:/usr/bin", "unused");
        assert_eq!(list[0], "/portable/restic");
        assert_eq!(list[1], "/portable/tools/restic");
        let first = list.iter().position(|p| p == "/first/restic").unwrap();
        let usr_bin = list.iter().position(|p| p == "/usr/bin/restic").unwrap();
        assert!(first < usr_bin);
        assert_eq!(list.iter().filter(|p| *p == "/first/restic").count(), 1);
        assert!(list.contains(&"/FIRST/restic".to_string()));
        assert!(list.contains(&"/usr/local/bin/restic".to_string()));
        assert!(candidates(false, "/portable", "", "unused").contains(&"/usr/bin/restic".to_string()));
    }

    #[test]
    fn winget_paths_are_only_used_on_windows() {
        let linux = candidates(false, "/app", "", r"C:\Programme");
        assert!(!linux.iter().any(|p| p.contains("WinGet")));
    }

    #[test]
    fn describes_the_origin() {
        assert_eq!(
            describe("/portable/tools/restic", "/portable", false),
            "Mit der Anwendung ausgeliefert"
        );
        assert_eq!(
            describe("/portable/restic", "/portable", false),
            "Neben der Anwendung"
        );
        assert_eq!(describe("/portable-other/restic", "/portable", false), "PATH");
        assert_eq!(describe("/PORTABLE/restic", "/portable", false), "PATH");
        assert_eq!(
            describe(r"c:\APP\restic.exe", r"C:\App", true),
            "Neben der Anwendung"
        );
        assert_eq!(
            describe(r"C:\Programme\WinGet\Links\restic.exe", r"C:\App", true),
            "WinGet-Installation"
        );
    }

    #[test]
    fn file_errors_are_reported_in_german() {
        let dir = std::env::temp_dir().join(format!("rb-locator-{}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let missing = dir.join("fehlt");
        assert!(file_error(&missing).unwrap().contains("fehlt"));
        let empty = dir.join("leer");
        fs::write(&empty, b"").unwrap();
        assert!(file_error(&empty).unwrap().contains("leer"));
        assert!(
            file_error(&dir).unwrap().contains("fehlt"),
            "Ordner sind keine Restic-Datei"
        );
        let _ = fs::remove_dir_all(&dir);
    }
}
