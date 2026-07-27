# SignPath setup for maintainers

## Requirements

- Public GitHub repository
- OSI-approved MIT license
- GitHub two-factor authentication
- Accepted SignPath Foundation application
- SignPath organization and project
- GitHub Actions enabled

## GitHub secret

Create the repository secret:

- `SIGNPATH_API_TOKEN`

Path:

`Repository Settings -> Secrets and variables -> Actions -> Secrets`

Never store the token in the repository.

## GitHub variables

Create the repository variables:

- `SIGNPATH_ORGANIZATION_ID`
- `SIGNPATH_PROJECT_SLUG`
- `SIGNPATH_SIGNING_POLICY_SLUG`
- `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`

Path:

`Repository Settings -> Secrets and variables -> Actions -> Variables`

## SignPath artifact configuration

Create or import the final artifact configuration in the SignPath web portal and use its slug for `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`.

The repository file [`.signpath/artifact-configuration.xml`](../.signpath/artifact-configuration.xml) is a reviewable template for the portal configuration. It signs only the first-party `ResticBrowser.exe` at the root of the submitted ZIP archive. `LICENSE` and `README.md` remain unsigned. Do not add or sign third-party executables or DLL files, especially a separately installed `restic.exe`, with this project's certificate.

## Release process

1. Update `Version`, `AssemblyVersion`, `FileVersion` and `InformationalVersion` consistently in `src/ResticBrowser/ResticBrowser.csproj`.
2. Commit and push all changes to a clean, tested `main`.
3. Create a Git tag matching that version, such as `v0.1.4` after updating the project to version `0.1.4`.
4. Push the tag.
5. GitHub Actions builds the unsigned `ResticBrowserWindows-<VERSION>-win-x64.zip` artifact.
6. The artifact is submitted to SignPath.
7. Approve the production signing request in SignPath.
8. GitHub Actions verifies the returned `ResticBrowser.exe` signature.
9. GitHub Actions records SHA-256 hashes and publishes `ResticBrowser.exe` plus the unchanged, versioned ZIP returned by SignPath as a GitHub Release.
10. The workflow downloads both published assets again and compares their SHA-256 hashes with the pre-upload values.

Manual workflow runs build and submit an artifact for signing but do not publish a GitHub Release.

## Verification

Run on Windows:

`Get-AuthenticodeSignature ".\ResticBrowser.exe" | Format-List`

Expected status:

`Valid`

The signer will normally be shown as SignPath Foundation.

## GitHub settings checklist

### GitHub account

- [ ] Enable two-factor authentication for all accounts with repository and SignPath access.

### Repository visibility

- [ ] Make the repository public. SignPath Foundation requires a public open-source project.

### Branch protection

Configure a ruleset or branch protection for `main`:

- [ ] Require a pull request before merging.
- [ ] Require status checks to pass.
- [ ] Block force pushes.
- [ ] Do not allow branch deletion.

A one-person project may need review rules that permit the maintainer to merge after automated checks without an impossible second-person approval.

### Security

- [ ] Enable Dependabot alerts.
- [ ] Enable private vulnerability reporting.
- [ ] Enable secret scanning if available.
- [ ] Keep GitHub Actions permissions limited to those declared in the workflow.

## SmartScreen

A valid Authenticode signature improves trust and traceability, but it does not guarantee that Microsoft Defender SmartScreen immediately stops warning about a new or rarely downloaded application. Reputation can build through consistent signatures, unchanged releases and legitimate downloads. With SignPath Foundation, the publisher is normally displayed as `SignPath Foundation`.

Never ask users to disable SmartScreen globally or permanently bypass Windows security features.

## Before the first release

- [ ] Submit and obtain acceptance of the SignPath Foundation application.
- [ ] Create the SignPath organization, project, artifact configuration and production signing policy.
- [ ] Ensure every production signing request requires manual approval.
- [ ] Add the GitHub secret and variables listed above.
- [ ] Confirm that the artifact configuration matches the ZIP contents exactly.
- [ ] Run the workflow manually once and approve the signing request.
- [ ] Verify the downloaded signed artifact before pushing the first release tag.
