# Security policy

## Reporting

Please report security-sensitive issues privately through GitHub Security Advisories instead of a public issue. Include the affected version, reproduction steps, and impact.

## Data boundary

Workflow Looper does not require a network connection and does not collect telemetry. Recorded patterns may contain keystrokes, so never record passwords, recovery codes, payment details, or other secrets. Pattern files remain under `%LOCALAPPDATA%\WorkflowLooper\Patterns` unless the user exports them elsewhere.
