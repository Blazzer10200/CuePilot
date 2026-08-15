# Contributing

Thanks for helping improve CuePilot.

1. Open an issue describing the behavior or focused change.
2. Create a branch from `main`.
3. Keep changes scoped and add self-test coverage for detector or automation behavior.
4. Follow the [development guide](docs/development.md) and run `pwsh -NoProfile -File .\scripts\verify.ps1 -All`; build the installer only when the release gate applies.
5. Open a pull request with the .NET, Svelte, and Rust test output plus before/after UI images when visual behavior changes.

Do not include credentials, private paths, local diagnostics, or unrelated generated output in a pull request. New game-frame fixtures must be narrowly scoped regression evidence and reviewed for private or identifying content.
