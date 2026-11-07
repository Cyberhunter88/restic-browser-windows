//! Baut die Argumentlisten für Restic. Alle Repository-Zugriffe laufen mit `--no-lock`,
//! damit der normale Workflow strikt lesend bleibt (auch auf FUSE- und SMB-Einhängungen).

use crate::models::{CheckMode, OverwritePolicy, RestoreRequest};

pub fn with_repository(repository: &str, command: &[&str]) -> Vec<String> {
    let mut args = vec!["--repo".to_string(), repository.to_string()];
    args.extend(command.iter().map(std::string::ToString::to_string));
    args.push("--no-lock".to_string());
    args
}

pub fn restore(repository: &str, request: &RestoreRequest) -> Vec<String> {
    let mut args = with_repository(
        repository,
        &[
            "restore",
            "--json",
            &request.snapshot_id,
            "--target",
            &request.target,
            "--overwrite",
            overwrite_value(request.overwrite),
        ],
    );
    for include in &request.includes {
        args.push("--include".to_string());
        args.push(escape_include_pattern(&normalize_snapshot_path(include)));
    }
    args
}

/// Maskiert Glob-Zeichen, damit ausgewählte Pfade wörtlich verstanden werden.
fn escape_include_pattern(path: &str) -> String {
    path.replace('[', "[[]").replace('*', "[*]").replace('?', "[?]")
}

pub fn preview_restore(repository: &str, request: &RestoreRequest) -> Vec<String> {
    let mut args = restore(repository, request);
    args.splice(4..4, ["--dry-run".to_string(), "--verbose=2".to_string()]);
    args
}

pub fn check(repository: &str, mode: CheckMode) -> Vec<String> {
    match mode {
        CheckMode::Full => with_repository(repository, &["check", "--json", "--read-data"]),
        CheckMode::Quick => with_repository(repository, &["check", "--json"]),
    }
}

pub fn normalize_snapshot_path(path: &str) -> String {
    let normalized = path.replace('\\', "/");
    if normalized.starts_with('/') {
        normalized
    } else {
        format!("/{normalized}")
    }
}

pub fn parent_path(path: &str) -> String {
    let normalized = normalize_snapshot_path(path);
    let trimmed = normalized.trim_end_matches('/');
    match trimmed.rfind('/') {
        Some(index) if index > 0 => trimmed[..index].to_string(),
        _ => "/".to_string(),
    }
}

pub fn stats(repository: &str) -> Vec<String> {
    with_repository(repository, &["stats", "--json"])
}

pub fn dump(repository: &str, snapshot_id: &str, path: &str) -> Vec<String> {
    with_repository(repository, &["dump", snapshot_id, &normalize_snapshot_path(path)])
}

pub fn ls_json(repository: &str, snapshot_id: &str) -> Vec<String> {
    with_repository(repository, &["ls", "--json", snapshot_id])
}

pub fn overwrite_value(policy: OverwritePolicy) -> &'static str {
    match policy {
        OverwritePolicy::Never => "never",
        OverwritePolicy::IfNewer => "if-newer",
        OverwritePolicy::IfChanged => "if-changed",
        OverwritePolicy::Always => "always",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn request(includes: &[&str]) -> RestoreRequest {
        RestoreRequest {
            snapshot_id: "abc".into(),
            target: r"C:\Ziel mit Leerzeichen".into(),
            includes: includes.iter().map(std::string::ToString::to_string).collect(),
            overwrite: OverwritePolicy::Never,
        }
    }

    fn position(args: &[String], value: &str) -> usize {
        args.iter()
            .position(|a| a == value)
            .unwrap_or_else(|| panic!("{value} fehlt in {args:?}"))
    }

    #[test]
    fn restore_arguments_are_separate_and_complete() {
        let args = restore(
            "rest:https://server/repo",
            &request(&["/Dokumente/a.txt", r"Bilder\b.jpg"]),
        );
        assert!(args.contains(&r"C:\Ziel mit Leerzeichen".to_string()));
        assert_eq!(args.iter().filter(|a| *a == "--include").count(), 2);
        assert!(args.contains(&"/Bilder/b.jpg".to_string()));
        assert_eq!(args[position(&args, "--overwrite") + 1], "never");
    }

    #[test]
    fn glob_characters_in_includes_are_escaped() {
        let args = restore("repo", &request(&["/a*[?].txt"]));
        assert!(args.contains(&"/a[*][[][?]].txt".to_string()));
    }

    #[test]
    fn preview_adds_dry_run_before_snapshot_id() {
        let mut req = request(&["/Datei mit Leerzeichen.txt"]);
        req.snapshot_id = "snapshot id".into();
        let args = preview_restore("repo", &req);
        assert!(args.contains(&"--dry-run".to_string()));
        assert_eq!(args[position(&args, "--verbose=2") + 1], "snapshot id");
        assert!(!args.contains(&"--delete".to_string()));
    }

    #[test]
    fn every_repository_command_is_read_only_with_one_no_lock() {
        let repo = "/run/user/1000/kio-fuse-test/smb/server/Backup mit Leerzeichen";
        let req = request(&["/file"]);
        let commands = [
            with_repository(repo, &["snapshots", "--json"]),
            with_repository(repo, &["find", "--json", "file"]),
            ls_json(repo, "snapshot"),
            dump(repo, "snapshot", "/file"),
            stats(repo),
            check(repo, CheckMode::Quick),
            check(repo, CheckMode::Full),
            restore(repo, &req),
            preview_restore(repo, &req),
        ];
        for args in commands {
            assert_eq!(args[position(&args, "--repo") + 1], repo);
            assert_eq!(args.iter().filter(|a| *a == "--no-lock").count(), 1);
            for forbidden in ["init", "forget", "prune", "rebuild-index", "repair"] {
                assert!(!args.contains(&forbidden.to_string()), "{forbidden} ist verboten");
            }
        }
    }

    #[test]
    fn paths_are_normalized() {
        assert_eq!(normalize_snapshot_path(r"C:\Users\Test"), "/C:/Users/Test");
        assert_eq!(parent_path("/C:/Users/Test"), "/C:/Users");
        assert_eq!(parent_path("/home"), "/");
        assert_eq!(parent_path("/"), "/");
    }

    #[test]
    fn overwrite_modes_map_to_restic_values() {
        assert_eq!(overwrite_value(OverwritePolicy::Never), "never");
        assert_eq!(overwrite_value(OverwritePolicy::IfNewer), "if-newer");
        assert_eq!(overwrite_value(OverwritePolicy::IfChanged), "if-changed");
        assert_eq!(overwrite_value(OverwritePolicy::Always), "always");
    }

    #[test]
    fn dump_stats_check_and_ls_are_correct() {
        assert!(stats("repo").contains(&"stats".to_string()));
        let dump_args = dump("myrepo", "snap1", r"folder\test.txt");
        assert!(dump_args.contains(&"/folder/test.txt".to_string()));

        assert!(!check("r", CheckMode::Quick).contains(&"--read-data".to_string()));
        assert!(check("r", CheckMode::Full).contains(&"--read-data".to_string()));
        assert!(ls_json("r", "snap1").contains(&"snap1".to_string()));
    }
}
