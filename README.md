# DependencyGuard

Namespace-level architecture rules for C#.

## Features

- Dependency rules between namespaces, in a simple YAML file
- **Nothing is allowed by default:** every dependency needs a rule
- Violations show up as warnings in the IDE and the build, at the offending line
- `allowed`, `denied` and `exposedTo` rules with wildcards
- A config per project, one for the whole solution, or both
- CLI tool for CI, with `generate` to create a starting config from existing code
- No attributes, no base classes, no runtime cost

## Example

```yaml
# dependency-guard.yaml
allowed:
  - from: MyApp.Application.*
    to: MyApp.Domain.*
```

```csharp
using MyApp.Application; // warning DG0001: No rule allows 'MyApp.Domain' to depend on 'MyApp.Application'.

namespace MyApp.Domain;
```

## Requirements

- **Analyzer:** .NET SDK 9.0.200 or later.
- **CLI tool:** .NET 8 runtime
- **Building from source:** .NET 10 SDK

## Roslyn analyzer

1. Add the package:

   ```xml
   <PackageReference Include="DependencyGuard.Analyzer" Version="0.1.0" PrivateAssets="all" />
   ```

2. Add a `dependency-guard.yaml` to the project directory. It is picked up automatically.
3. Build. Every violation is a warning.

## Rules

- **Nothing is allowed by default.**
- `from`: the namespace that has the dependency. `to`: the namespace it depends on.
- Checked in this order:
  1. `allowed` / `denied` rules. The most specific `to` wins, then the most specific `from`.
  2. `exposedTo`
  3. Nothing matched: denied.

### Patterns

- `MyApp.Api`: exactly that namespace
- `MyApp.Api.*`: that namespace and everything below it
- `.*`: every namespace

### allowed / denied

```yaml
allowed:
  - from: MyApp.Application
    to: System.*       # all of System...

denied:
  - from: MyApp.Application
    to: System.IO.*    # ...except System.IO
```

- The more specific rule wins.
- An equally specific `allowed` and `denied` rule is a conflict that's reported as an error.

### Related namespaces need rules too

- Parents, children and siblings all need a rule.
- The same namespace is never checked.
- To let a module use its own sub-namespaces:

  ```yaml
  allowed:
    - from: MyApp.Orders.*
      to: MyApp.Orders.*
  ```

### exposedTo

```yaml
exposedTo:
  - namespace: MyApp.Infrastructure.*
    consumers:
      - MyApp.Application.*
      - MyApp.Worker.*
```

- Limits who may use a namespace. Everyone else is denied.
- Listed consumers need no separate `allowed` rule.
- An `allowed` rule wins over `exposedTo`.
- One entry per namespace. Duplicates are a DG0003 conflict.

### What is checked

- `using` directives
- Fully-qualified type references
- Inferred types of `var`
- **Not** checked: global, static and alias usings. So `ImplicitUsings` needs no rules.

### Multiple config files

- Add a solution-wide file to every project from a `Directory.Build.props`:

  ```xml
  <ItemGroup>
    <DependencyGuardConfig Include="$(MSBuildThisFileDirectory)dependency-guard.yaml" />
  </ItemGroup>
  ```

- All files are merged into one rule set. Conflicts between files are DG0003.
- Every file must be named `dependency-guard.yaml`.
- To share one file instead of a file per project, set `DependencyGuardConfigPath` ([example](Demo/SharedConfig/README.md)).
- The CLI ignores these MSBuild settings.

## Architectural patterns

The examples leave out `System.*` and the rules a module needs for its own sub-namespaces.

### 1. Layered architecture

```yaml
allowed:
  - from: MyApp.Application.*
    to: MyApp.Domain.*
  - from: MyApp.Infrastructure.*
    to: MyApp.Domain.*
```

- Application and Infrastructure use Domain. Domain uses nothing.

### 2. Module isolation

```yaml
allowed:
  - from: MyApp.Domain.Orders.*
    to: MyApp.Domain.Orders.*
  - from: MyApp.Domain.Customers.*
    to: MyApp.Domain.Customers.*
```

- Each module uses only itself. Orders can't use Customers.

### 3. Plugin / provider

```yaml
allowed:
  - from: MyApp.Notifications.Email
    to: MyApp.Notifications
  - from: MyApp.Notifications.Sms
    to: MyApp.Notifications
```

- Providers use the contract, never each other.

### 4. Internal helpers

```yaml
allowed:
  - from: MyApp.Payments.Stripe
    to: MyApp.Payments
  - from: MyApp.Payments.Stripe.Http
    to: MyApp.Payments.Stripe.*
```

- Stripe implements the contract. Its HTTP helper uses only Stripe internals.

### 5. Deny list

```yaml
allowed:
  - from: .*
    to: .*

denied:
  - from: .*
    to: System.Reflection.*
```

- Everything is allowed, except `System.Reflection`.

## CLI tool

```
dotnet tool install --global DependencyGuard.Cli

dependency-guard generate MySolution.slnx   # write a starting config per project
dependency-guard check MySolution.slnx      # report violations
```

- Checks projects without building them. Handy in CI.
- Syntax only: it skips `var` inference and reads `Outer.Inner` as namespace `Outer`.

### generate

```
dependency-guard generate [--output <path>] [--force] [<target>]
```

- Writes a `dependency-guard.yaml` per project that allows every dependency it has today.
- Then remove the rules for the dependencies you don't want.
- `--output`, `-o`: write to this path (single project)
- `--force`: overwrite an existing file
- Exit codes: `0` written, `2` failed or usage error

### check

```
dependency-guard [check] [--config <path>] [<target>]
```

- `check` is the default, so `dependency-guard <target>` works too.
- `<target>`: a `.csproj`, `.sln` or `.slnx` file, or a directory. Default: the current directory.
- `--config`, `-c`: use this config for every project.
- Projects without a config are skipped.
- Exit codes: `0` no violations, `1` violations or an invalid config, `2` usage error

```
MyApp.Application (14 files)
  src/Services/OrderService.cs(12,5): warning DG0001: No rule allows 'MyApp.Application' to depend on 'MyApp.Infrastructure'.

Found 1 violation(s).
```

## Demo

See [Demo/README.md](Demo/README.md) for working projects of these patterns.

## Building from source

```
dotnet run Scripts/BuildTestPackRunDemo.cs
```

- Builds, runs all tests, packs, and checks that every demo still reports its violations.
- In Rider: **Build, test, pack and run demo**.

## Why DependencyGuard?

Managing dependencies in software is hard. As a codebase grows, it is easy to lose track of which parts of the code depend on which. The documented design (the one an architect drew up at the start of the project) slowly drifts away from the implemented design (the one that actually lives in the code). Manual review rarely catches this: a new dependency is often a single `using` line, and it is easy to miss in a large pull request.

This is the same argument Gerard Holzmann makes for static analysis in NASA/JPL's [The Power of 10: Rules for Developing Safety-Critical Code](https://spinroot.com/gerard/pdf/P10.pdf):

> Tool-based checks are important, since it is often infeasible to manually review the hundreds of thousands of lines of code that are written for larger applications.

A common way to let tooling enforce boundaries is at the project or package level: split the code into separate projects, and let project and package references decide what may use what. That works, but it is coarse. A project is a large unit, so every boundary you want to enforce means another project. Even a relatively small codebase quickly grows to dozens of projects, each adding build time and maintenance, just to express rules that the namespaces already describe.

DependencyGuard checks dependencies at the namespace level instead. Boundaries can be as fine-grained as your namespaces, the project structure can stay the way you want it, and every build checks the implemented design against the documented one.
