# SignPath Foundation application information

## Project name

Restic Browser Windows

## Repository

https://github.com/Cyberhunter88/restic-browser-windows

## Maintainer

Cyberhunter88

## License

MIT License

## Project description

Restic Browser Windows is an open-source graphical Windows application for browsing and restoring files from Restic backup repositories.

The complete application source code and all relevant build scripts are maintained in the public GitHub repository.

Windows release binaries are built automatically using GitHub Actions on GitHub-hosted runners.

The application does not collect or transmit telemetry. Network connections are made only when required to access Restic repositories or storage locations selected or configured by the user.

## Download page

https://github.com/Cyberhunter88/restic-browser-windows/releases

## Build system

GitHub Actions using GitHub-hosted Windows runners and the .NET 10 SDK.

## Files to be signed

- `ResticBrowser.exe` — first-party, self-contained Windows x64 application

No third-party executable or DLL, including `restic.exe`, is submitted for signing.

## Code signing policy

https://github.com/Cyberhunter88/restic-browser-windows/blob/main/CODE_SIGNING_POLICY.md

## Privacy information

https://github.com/Cyberhunter88/restic-browser-windows#privacy

## Release process

A `vMAJOR.MINOR.PATCH` tag triggers the GitHub Actions workflow. It validates the tag against the project version, restores dependencies, runs the tests, publishes a self-contained Windows x64 executable and packages it with the license and README. The unsigned ZIP artifact is submitted directly to SignPath. After manual production approval, the workflow extracts and verifies the returned Authenticode-signed executable, then publishes the unchanged SignPath-returned ZIP and its signed executable through GitHub Releases.

## Security

[Confirm before submitting:] Two-factor authentication is enabled for accounts with repository and SignPath access.

Each production signing request requires manual approval.
