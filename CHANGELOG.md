# Changelog

## 0.2.0-beta.2 (2026-09-29)

The fixes are described, with examples, in [Docs/Bugs-0.1.md](Docs/Bugs-0.1.md).

### Changed (check your rules)

- Every name is checked, however it came into scope: implicit and global usings, aliases, static usings, extension
  methods and the enclosing namespace no longer bypass the rules. `ImplicitUsings` now needs a rule for what you use,
  usually `from: .*` `to: System.*`.
- Razor components are checked (what is written in `.razor` files).
- Code without a namespace (top-level statements) belongs to the project's `RootNamespace`, not to `''`.
- A mistake in a rule file (an unknown key, a missing `from` or `to`, a pattern like `MyApp.*.Api`) is an error
  (DG0004) at its line, and nothing is checked until it is fixed.
- CLI: `--config` adds to the project's own `dependency-guard.yaml` instead of replacing it, and may be repeated.
- CLI: `generate --output` writes one file with the rules of every project.

### Added

- The `DependencyGuard.Core` package (the rules engine), to test what a rule file allows.

### Removed

- `exposedTo`. Write a `denied` rule for everyone and an `allowed` rule for the consumers instead.

### Fixed

- False DG0001s for explicitly typed `foreach` and `catch` variables.
- Nested namespace blocks were reported by their last part.
- A using above several namespaces was checked for the first one only.
- DG0003 pointed at the line below the rule.
- A broken rule file also gave DG0000, and other rule files were used on their own.
- Crossed rules with `.*` were not reported as a conflict.
- CLI: `--help`, `--version`; unknown options are usage errors.
- The tests and the demo no longer leave packages in the machine's NuGet cache.

## 0.1.0

First version.
