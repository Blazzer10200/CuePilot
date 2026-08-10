# Contributing

Thanks for helping improve Workflow Looper.

1. Open an issue describing the behavior or focused change.
2. Create a branch from `main`.
3. Keep changes scoped and add self-test coverage for timing or pattern behavior.
4. Run `dotnet build -c Release` and `dotnet run -c Release -- --self-test`.
5. Open a pull request with the test output and before/after UI images when visual behavior changes.

Do not include recorded pattern files, credentials, private paths, or unrelated generated output in a pull request.
