# DependencyGuard

Namespace-level architecture rules for C#.

## Features

- Dependency rules between namespaces, in a simple YAML file
- **Nothing is allowed by default:** every dependency needs a rule
- Violations show up as warnings in the IDE and the build, at the offending line, in `.razor` files too
- Every name is checked, however it came into scope: a using, an implicit or global using, an alias, the enclosing
  namespace
- `allowed` and `denied` rules with wildcards
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
   <PackageReference Include="DependencyGuard.Analyzer" Version="0.2.0-beta.2" PrivateAssets="all" />
   ```

2. Add a `dependency-guard.yaml` to the project directory. It is picked up automatically.
3. Build. Every violation is a warning.

## Rules

- **Nothing is allowed by default.**
- `from`: the namespace that has the dependency. `to`: the namespace it depends on.
- An `allowed` or `denied` rule decides. The most specific `to` wins, then the most specific `from`.
- Nothing matched: denied.
- A mistake in the file is an error (DG0004) at its line: an unknown key (`allow:`, `form:`), a missing or empty
  `from` or `to`, or a pattern that is not one of the three below (`MyApp.*.Api`, `MyApp*`). Nothing is checked
  until the file is right: a misspelled `denied` rule would otherwise allow what it was meant to deny.

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

### What is checked

- **Every name that refers to a type in another namespace**, however the name came into scope: a `using`, a global
  or implicit using (`ImplicitUsings`, `<Using>` items), an alias, a static using, an extension method, a fully
  qualified name, or the enclosing namespace (a child namespace sees its parent's types without a using).
- `var`: the type the compiler infers, and the types in it (`List<Order>`: `System.Collections.Generic` and
  `Order`'s namespace).
- `using Namespace;` directives, where they are written. A using above all namespaces counts for every namespace in
  the file.
- **Razor components**: what is written in `.razor` files (the C# that Razor generates is checked where a `#line`
  directive maps it back to the `.razor` file). A using in `_Imports.razor` is reported once, not once per component.
- **Code without a namespace** (top-level statements, a class in the global namespace) belongs to the project's
  `RootNamespace`, so a rule can say `from: MyCompany.MyApp`.
- **Not** checked: keywords (`string`, `int`), types that are never named (lambda parameters, a target-typed
  `new()`), and generated code that no `#line` maps to a file.
- `ImplicitUsings` and `<Using>` items bring in namespaces without a using line, so what you use from them needs
  rules too, usually `from: .*` `to: System.*`.

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
- The CLI does not read these MSBuild settings: give it the solution-wide file with `--config`; it is added to each
  project's own file, as in the build.

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
- Syntax only: it checks `using` directives and fully qualified names in the `.cs` files of a project's folder. It
  does not see what the analyzer sees through the compiler: names from global, implicit or static usings, `var`,
  `.razor` files and files linked from elsewhere. It reads `Outer.Inner` as namespace `Outer`.
- `--help`, `--version`.

### generate

```
dependency-guard generate [--output <path>] [--force] [<target>]
```

- Writes a `dependency-guard.yaml` per project that allows every dependency it has today.
- Then remove the rules for the dependencies you don't want.
- `--output`, `-o`: write one file with the rules of every project, e.g. for the whole solution
- `--force`: overwrite an existing file
- Exit codes: `0` written, `2` failed or usage error

### check

```
dependency-guard [check] [--config <path>]... [<target>]
```

- `check` is the default, so `dependency-guard <target>` works too.
- `<target>`: a `.csproj`, `.sln` or `.slnx` file, or a directory. Default: the current directory.
- `--config`, `-c`: a config for every project, merged with the project's own `dependency-guard.yaml`. May be
  repeated.
- Projects without any config are skipped.
- An unknown option is a usage error.
- Exit codes: `0` no violations, `1` violations or an invalid config, `2` usage error

```
MyApp.Application (14 files)
  src/Services/OrderService.cs(12,5): warning DG0001: No rule allows 'MyApp.Application' to depend on 'MyApp.Infrastructure'.

Found 1 violation(s).
```

## Testing a rule file

The `DependencyGuard.Core` package holds the rules engine. A test can use it to check that the rule file still says
what the architecture says, so a violation is not "fixed" by loosening a rule:

```csharp
DependencyRuleSet rules = DependencyGuardFactory.ParseFromYaml(File.ReadAllText(path), path);
DependencyGuardFactory.TryCreateAnalyzer([rules], out IDependencyAnalyzer? analyzer);
Assert.That(analyzer!.AnalyzeDependency(new("MyApp.Domain", "MyApp.Infrastructure")).IsAllowed, Is.False);
```

## Diagnostics

| ID | Severity | Meaning |
|----|----------|---------|
| DG0000 | warning | The project has no `dependency-guard.yaml`. |
| DG0001 | warning | A dependency no rule allows, or one a `denied` rule denies. |
| DG0003 | error | Conflicting rules: the same pair allowed and denied, or rules whose order of specificity is ambiguous. |
| DG0004 | error | A mistake in a rule file, at its line. |
| DG9999 | error | A bug in DependencyGuard. |

To fail the build on a violation, add `<WarningsAsErrors>$(WarningsAsErrors);DG0001</WarningsAsErrors>` to the
project (or a `Directory.Build.props`). A severity in `.editorconfig` also works for `.cs` files, but it does not
reach the C# that Razor generates, so a violation in a `.razor` file would stay a warning.

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
