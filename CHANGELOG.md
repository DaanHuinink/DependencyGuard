# Changelog

## 0.2.0-beta.3 (2026-09-29)

The fixes are described, with examples, in [Docs/Bugs-0.1.md](Docs/Bugs-0.1.md).

### Changed (check your rules)

- Every name is checked, however it came into scope: implicit and global usings, aliases, static usings, extension
  methods and the enclosing namespace no longer bypass the rules. `ImplicitUsings` now needs a rule for what you use,
  usually `from: .*` `to: System.*`.
- Razor components are checked (what is written in `.razor` files).
- Code without a namespace (top-level statements) belongs to the project's `RootNamespace`, not to `''`.
- A mistake in a rule file (an unknown key, a missing `from` or `to`, a pattern like `MyApp.*.Api`) is an error
  (DG0004) at its line, and nothing is checked until it is fixed.
- The CLI loads projects with MSBuild and runs the analyzer, so it reports what the build reports (Razor files,
  implicit usings, linked files, MSBuild settings). It restores first (`--no-restore` skips that) and needs the
  .NET 10 SDK.
- CLI: `--config` adds to the project's own `dependency-guard.yaml` instead of replacing it, and may be repeated.
- CLI: `generate --output` writes one file with the rules of every project.

### Added

- Every DG0001 carries its namespaces as properties (`SourceNamespace`, `TargetNamespace`).
- The `DependencyGuard.Core` package (the rules engine), to test what a rule file allows.

### Removed

- `exposedTo`. Nothing is allowed by default, so `allowed` rules for the consumers are usually enough. To limit what a
  broader `allowed` rule lets in, add a `denied` rule with the same `from` as that rule, plus `allowed` rules for the
  consumers. A `denied` rule with a broader `from` would cross the `allowed` rule (DG0003).

### Fixed

- False DG0001s for explicitly typed `foreach` and `catch` variables.
- Nested namespace blocks were reported by their last part.
- A using above several namespaces was checked for the first one only.
- DG0003 pointed at the line below the rule.
- A broken rule file also gave DG0000, and other rule files were used on their own.
- Crossed rules with `.*` were not reported as a conflict, and exact rules that can never meet were.
- CLI: `--help`, `--version`; unknown options are usage errors.
- The tests and the demo no longer leave packages in the machine's NuGet cache.

## 0.1.0

First version.
