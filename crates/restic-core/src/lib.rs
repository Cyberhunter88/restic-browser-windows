//! Kern von Restic Browser: Prozess-Runner, Befehlsbau und JSON-Modelle.
//!
//! Restic bleibt die einzige Schnittstelle zum Repository. Alle Prozesse werden ohne Shell
//! gestartet; Passwörter und Backend-Geheimnisse leben nur in der Prozessumgebung.

pub mod bounded_details;
pub mod command;
pub mod credentials;
pub mod env_validator;
pub mod error;
pub mod guidance;
pub mod json_stream;
pub mod locator;
pub mod manifest;
pub mod models;
pub mod paths;
pub mod profile;
pub mod provisioning;
pub mod repository;
pub mod result_batch;
pub mod runner;
pub mod storage;
pub mod version_probe;

pub use credentials::SessionCredentials;
pub use error::ResticError;
pub use profile::{RepositoryProfile, RepositoryType};
pub use provisioning::{EmbeddedRestic, ResticExecutableInfo, ResticResolver};
pub use repository::ResticRepositoryService;
pub use runner::{BinaryResult, Flow, JsonResult, ProcessResult, ResticCommand, ResticProcessRunner};
pub use version_probe::{ProcessVersionProbe, VersionProbe};
