# Security Policy

## Supported versions

Security fixes are provided for the latest published release. Before reporting
an issue, reproduce it on that version when doing so is safe.

## Report a vulnerability privately

Do not open a public issue for a suspected vulnerability, leaked credential, or
report containing sensitive logs. Use GitHub's **Report a vulnerability** flow
under the repository Security tab. Include the affected version, impact,
reproduction steps, and the smallest sanitized proof of concept that explains
the problem.

Please do not include model API keys, Manager control tokens, private model
paths, signing material, or unredacted personal data. The Manager control token
is process-local discovery material and should never be copied from the
workspace or database.

You can expect acknowledgement within seven days. Public disclosure should wait
until a fix or mitigation is available and coordinated with the maintainer.

## Security boundaries

- The Manager control API is authenticated and bound to loopback.
- Model inference requires the separate model API key. Network exposure is an
  explicit setting.
- Managed downloads validate available size and SHA-256 information before
  installation. Managed runtime installations also record a local file-hash
  baseline; later verification detects changed, missing, and unexpected files.
  This is local change detection, not publisher authentication.
- Manually registered runtimes have no Manager-recorded baseline and are
  displayed as unverified custom runtimes.
- Published binaries are unsigned unless a release explicitly states otherwise.
  Checksums verify integrity, not publisher identity.

See [Architecture](docs/ARCHITECTURE.md) and [Release readiness](docs/RELEASE_READINESS.md)
for the complete trust model and validation process.
