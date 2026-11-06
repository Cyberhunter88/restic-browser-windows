//! Abstraktion des Repository-Zugriffs für die Sitzung (Gegenstück zu `IResticRepositoryService`).
//!
//! Treffer werden als Batches über einen Kanal mit Kapazität 1 geliefert. Der Kanal ersetzt die
//! `async`-Rückrufe der C#-Linie: solange die Sitzung einen Batch verarbeitet, wartet der Erzeuger
//! (Gegendruck), und wird die Sitzung verworfen oder die Operation ersetzt, endet der Erzeuger mit
//! [`ResticError::Cancelled`].

use std::future::Future;

use restic_core::models::{
    BackupNode, FilePreviewData, FileVersion, LatestFileMatch, RepositoryStats, ResticVersion,
    RestorePreviewResult, RestoreProgress, RestoreRequest, RestoreResult, SnapshotInfo,
};
use restic_core::{RepositoryProfile, ResticError, ResticRepositoryService, SessionCredentials};
use tokio::sync::mpsc;
use tokio_util::sync::CancellationToken;

/// Sendeseite eines Batch-Kanals.
pub struct BatchSink<T> {
    sender: mpsc::Sender<Vec<T>>,
}

impl<T> Clone for BatchSink<T> {
    fn clone(&self) -> Self {
        Self {
            sender: self.sender.clone(),
        }
    }
}

impl<T> BatchSink<T> {
    /// Liefert einen Batch. Gibt `Cancelled` zurück, wenn die Gegenseite nichts mehr annimmt.
    pub async fn send(&self, batch: Vec<T>) -> Result<(), ResticError> {
        self.sender.send(batch).await.map_err(|_| ResticError::Cancelled)
    }
}

/// Erzeugt einen Batch-Kanal mit Kapazität 1 (Gegendruck).
pub fn batch_channel<T>() -> (BatchSink<T>, mpsc::Receiver<Vec<T>>) {
    let (sender, receiver) = mpsc::channel(1);
    (BatchSink { sender }, receiver)
}

/// Lesender Zugriff auf ein Repository. Implementiert von [`ResticRepositoryService`]; Tests
/// verwenden gesteuerte Fakes.
pub trait RepositoryBackend: Send + Sync + 'static {
    fn validate(
        &self,
        profile: &RepositoryProfile,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<ResticVersion, ResticError>> + Send;

    fn get_snapshots_batched(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        sink: BatchSink<SnapshotInfo>,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<(), ResticError>> + Send;

    /// Liefert Batches über `sink` und am Ende die sortierte Gesamtliste.
    fn get_directory_batched(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        path: &str,
        sink: BatchSink<BackupNode>,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<Vec<BackupNode>, ResticError>> + Send;

    /// Liefert Treffer über `sink`; das Ergebnis ist `true`, wenn die Trefferliste gekürzt wurde.
    fn find_batched(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        pattern: &str,
        sink: BatchSink<BackupNode>,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<bool, ResticError>> + Send;

    fn find_newest(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        pattern: &str,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<Option<LatestFileMatch>, ResticError>> + Send;

    fn get_file_versions(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        exact_path: &str,
        hostname: Option<&str>,
        known_snapshots: Option<&[SnapshotInfo]>,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<Vec<FileVersion>, ResticError>> + Send;

    fn get_stats(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<RepositoryStats, ResticError>> + Send;

    fn get_file_preview(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        node: &BackupNode,
        snapshot_id: &str,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<FilePreviewData, ResticError>> + Send;

    fn restore(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        request: &RestoreRequest,
        progress: &(dyn Fn(RestoreProgress) + Send + Sync),
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<RestoreResult, ResticError>> + Send;

    fn preview_restore(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        request: &RestoreRequest,
        stop: &CancellationToken,
    ) -> impl Future<Output = Result<RestorePreviewResult, ResticError>> + Send;
}

impl RepositoryBackend for ResticRepositoryService {
    async fn validate(
        &self,
        profile: &RepositoryProfile,
        stop: &CancellationToken,
    ) -> Result<ResticVersion, ResticError> {
        ResticRepositoryService::validate(self, profile, stop).await
    }

    async fn get_snapshots_batched(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        sink: BatchSink<SnapshotInfo>,
        stop: &CancellationToken,
    ) -> Result<(), ResticError> {
        ResticRepositoryService::get_snapshots_batched(
            self,
            profile,
            credentials,
            |batch| sink.send(batch),
            stop,
        )
        .await
    }

    async fn get_directory_batched(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        path: &str,
        sink: BatchSink<BackupNode>,
        stop: &CancellationToken,
    ) -> Result<Vec<BackupNode>, ResticError> {
        ResticRepositoryService::get_directory_batched(
            self,
            profile,
            credentials,
            snapshot_id,
            path,
            |batch| sink.send(batch),
            stop,
        )
        .await
    }

    async fn find_batched(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        snapshot_id: &str,
        pattern: &str,
        sink: BatchSink<BackupNode>,
        stop: &CancellationToken,
    ) -> Result<bool, ResticError> {
        ResticRepositoryService::find_batched(
            self,
            profile,
            credentials,
            snapshot_id,
            pattern,
            |batch| sink.send(batch),
            stop,
        )
        .await
    }

    async fn find_newest(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        pattern: &str,
        stop: &CancellationToken,
    ) -> Result<Option<LatestFileMatch>, ResticError> {
        ResticRepositoryService::find_newest(self, profile, credentials, pattern, stop).await
    }

    async fn get_file_versions(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        exact_path: &str,
        hostname: Option<&str>,
        known_snapshots: Option<&[SnapshotInfo]>,
        stop: &CancellationToken,
    ) -> Result<Vec<FileVersion>, ResticError> {
        ResticRepositoryService::get_file_versions(
            self,
            profile,
            credentials,
            exact_path,
            hostname,
            known_snapshots,
            stop,
        )
        .await
    }

    async fn get_stats(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        stop: &CancellationToken,
    ) -> Result<RepositoryStats, ResticError> {
        ResticRepositoryService::get_stats(self, profile, credentials, stop).await
    }

    async fn get_file_preview(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        node: &BackupNode,
        snapshot_id: &str,
        stop: &CancellationToken,
    ) -> Result<FilePreviewData, ResticError> {
        ResticRepositoryService::get_file_preview(self, profile, credentials, node, snapshot_id, stop).await
    }

    async fn restore(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        request: &RestoreRequest,
        progress: &(dyn Fn(RestoreProgress) + Send + Sync),
        stop: &CancellationToken,
    ) -> Result<RestoreResult, ResticError> {
        ResticRepositoryService::restore(self, profile, credentials, request, progress, stop).await
    }

    async fn preview_restore(
        &self,
        profile: &RepositoryProfile,
        credentials: &SessionCredentials,
        request: &RestoreRequest,
        stop: &CancellationToken,
    ) -> Result<RestorePreviewResult, ResticError> {
        ResticRepositoryService::preview_restore(self, profile, credentials, request, stop).await
    }
}
