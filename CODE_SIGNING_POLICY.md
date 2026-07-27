# Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Project

Restic Browser Windows is developed and maintained in the following public source-code repository:

https://github.com/Cyberhunter88/restic-browser-windows

Only binaries built from the source code and build scripts contained in this repository are submitted for signing.

## Team roles

### Committer, reviewer and approver

- [Cyberhunter88](https://github.com/Cyberhunter88)

The maintainer is responsible for developing and reviewing changes and manually approving release-signing requests.

Contributions from other users must be submitted through pull requests and reviewed before they are merged.

## Build and release process

Release binaries are built automatically using GitHub Actions on GitHub-hosted runners.

The unsigned release artifact is uploaded directly from the GitHub Actions workflow to SignPath. Signed artifacts are downloaded from SignPath and published through GitHub Releases.

Release artifacts are not built or modified manually after signing.

## Privacy policy

Restic Browser Windows does not collect or transmit telemetry unless this is explicitly documented in the application.

The application may access local or remote Restic repositories and storage locations explicitly selected or configured by the user.

## Security

Two-factor authentication must be enabled for accounts with access to the source-code repository and SignPath.

Each production signing request requires manual approval.

## Reporting security issues

Security issues should be reported privately through the GitHub repository's security reporting function.
