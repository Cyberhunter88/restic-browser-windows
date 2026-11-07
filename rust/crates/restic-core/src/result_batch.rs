//! Bündelt Treffer zu Batches: der erste Treffer sofort, danach höchstens 256 Einträge je Batch
//! oder spätestens alle 100 ms. Die Zustellung ist `async`; solange sie läuft, wird nicht weiter aus
//! stdout gelesen, wodurch Restic natürlich gebremst wird (Backpressure).

use std::future::Future;
use std::time::{Duration, Instant};

use tokio_util::sync::CancellationToken;

use crate::error::ResticError;

pub const MAXIMUM_BATCH_SIZE: usize = 256;
const MAXIMUM_BATCH_AGE: Duration = Duration::from_millis(100);

pub struct ResultBatch<'a, T, F> {
    items: Vec<T>,
    deliver: F,
    stop: &'a CancellationToken,
    last_delivery: Instant,
    first: bool,
}

impl<'a, T, F, Fut> ResultBatch<'a, T, F>
where
    F: FnMut(Vec<T>) -> Fut,
    Fut: Future<Output = Result<(), ResticError>>,
{
    pub fn new(deliver: F, stop: &'a CancellationToken) -> Self {
        Self {
            items: Vec::with_capacity(MAXIMUM_BATCH_SIZE),
            deliver,
            stop,
            last_delivery: Instant::now(),
            first: true,
        }
    }

    pub async fn add(&mut self, item: T) -> Result<(), ResticError> {
        self.check_cancelled()?;
        self.items.push(item);
        if self.first
            || self.items.len() >= MAXIMUM_BATCH_SIZE
            || self.last_delivery.elapsed() >= MAXIMUM_BATCH_AGE
        {
            self.flush().await?;
        }
        Ok(())
    }

    pub async fn flush(&mut self) -> Result<(), ResticError> {
        self.check_cancelled()?;
        if self.items.is_empty() {
            return Ok(());
        }
        let batch = std::mem::replace(&mut self.items, Vec::with_capacity(MAXIMUM_BATCH_SIZE));
        (self.deliver)(batch).await?;
        self.first = false;
        self.last_delivery = Instant::now();
        Ok(())
    }

    fn check_cancelled(&self) -> Result<(), ResticError> {
        if self.stop.is_cancelled() {
            Err(ResticError::Cancelled)
        } else {
            Ok(())
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn first_item_is_delivered_immediately_then_batches_are_bounded() {
        let stop = CancellationToken::new();
        let mut sizes = Vec::new();
        {
            let mut batch = ResultBatch::new(
                |items: Vec<u32>| {
                    sizes.push(items.len());
                    std::future::ready(Ok(()))
                },
                &stop,
            );
            for value in 0..1000u32 {
                batch.add(value).await.unwrap();
            }
            batch.flush().await.unwrap();
        }
        assert_eq!(sizes[0], 1);
        assert!(sizes.iter().all(|&size| size <= MAXIMUM_BATCH_SIZE));
        assert_eq!(sizes.iter().sum::<usize>(), 1000);
        assert!(sizes.len() >= 5);
    }

    #[tokio::test]
    async fn cancelled_token_stops_adding_and_flushing() {
        let stop = CancellationToken::new();
        let mut batch = ResultBatch::new(|_: Vec<u32>| std::future::ready(Ok(())), &stop);
        batch.add(1).await.unwrap();
        stop.cancel();
        assert!(matches!(batch.add(2).await, Err(ResticError::Cancelled)));
        assert!(matches!(batch.flush().await, Err(ResticError::Cancelled)));
    }

    #[tokio::test]
    async fn delivery_errors_are_propagated() {
        let stop = CancellationToken::new();
        let mut batch = ResultBatch::new(
            |_: Vec<u32>| std::future::ready(Err(ResticError::new("Zustellung gescheitert"))),
            &stop,
        );
        assert_eq!(
            batch.add(1).await.unwrap_err().to_string(),
            "Zustellung gescheitert"
        );
    }
}
