# Security policy

## Reporting

Please report security-sensitive issues privately through GitHub Security Advisories instead of a public issue. Include the affected version, reproduction steps, and impact.

## Supported release

Security fixes are made against the latest published CuePilot release. Include the exact version shown in the title bar and whether the copy was installed by Velopack when reporting a problem.

## Data boundary

CuePilot does not collect telemetry or upload game captures. It stores local settings, numeric diagnostics, and detector evidence for local Detection Review; it does not record keystrokes. The installed application makes outbound HTTPS requests only to CuePilot's public GitHub releases for user-facing update checks and package downloads. Automation, capture, input control, and diagnostics continue to work without that connection.

Do not include passwords, recovery codes, payment details, or other secrets in issue reports or diagnostic files.

## Update trust boundary

- Release packaging runs in `.github/workflows/release.yml` with the repository-scoped GitHub Actions token.
- Velopack 1.2.0 generates `releases.win.json` plus SHA-256-addressed packages. The runtime and packaging CLI are pinned to the same version.
- Installed clients read only stable, public releases from `Blazzer10200/CuePilot`; pre-releases are excluded.
- The release job verifies that the installer, full package, feed, checksum, and release manifest are public and that the feed advertises the tagged version.
- Packages are not Authenticode-signed today. HTTPS, GitHub repository security, workflow permissions, and package hashes are the current trust root; SmartScreen warnings are expected until signing is added.

Compromise of the repository, Actions token, or release workflow could therefore compromise the update channel. Keep release permissions narrow, require reviewed changes before tagging, and treat code signing as the remaining defense-in-depth project.
