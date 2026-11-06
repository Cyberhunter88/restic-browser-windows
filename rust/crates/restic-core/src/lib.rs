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
pub mod models;
pub mod runner;

pub use error::ResticError;
pub use runner::{BinaryResult, Flow, JsonResult, ProcessResult, ResticCommand, ResticProcessRunner};
