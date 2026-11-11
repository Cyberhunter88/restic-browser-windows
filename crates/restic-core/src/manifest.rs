//! Das Build-Manifest (`build/restic-manifest.json`) ist die einzige Quelle für die gebündelte
//! Restic-Version und die erwarteten SHA-256-Prüfsummen. Es wird zur Compile-Zeit eingebettet.

use serde::Deserialize;

const MANIFEST_JSON: &str = include_str!("../../../build/restic-manifest.json");

#[derive(Debug, Clone, Deserialize)]
pub struct PlatformEntry {
    #[serde(rename = "binarySha256")]
    pub binary_sha256: String,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ResticManifest {
    pub version: String,
    pub windows: PlatformEntry,
    pub linux: PlatformEntry,
}

impl ResticManifest {
    /// Das eingebettete Manifest. Ein beschädigtes Manifest ist ein Build-Fehler und wird
    /// durch den Test `embedded_manifest_is_valid` entdeckt.
    pub fn embedded() -> Self {
        serde_json::from_str(MANIFEST_JSON).expect("eingebettetes Restic-Manifest ist ungültig")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn embedded_manifest_is_valid() {
        let manifest = ResticManifest::embedded();
        assert_eq!(manifest.version, "0.19.1");
        for hash in [&manifest.windows.binary_sha256, &manifest.linux.binary_sha256] {
            assert_eq!(hash.len(), 64);
            assert!(hash.chars().all(|c| c.is_ascii_hexdigit()));
        }
    }
}
