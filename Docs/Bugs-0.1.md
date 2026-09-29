# Bugs in 0.1, and what 0.2.0-beta.4 does about them

Found on 2026-09-28 and 29 while adding DependencyGuard to Quiz Night (a .NET 10 solution with Blazor components,
`ImplicitUsings`, top-level statements and about 8,000 cross-namespace references), and while fixing what was found.
Each bug has a small example, the cause, the fix and the tests that keep it fixed.

| # | Bug | Effect | Status |
|---|-----|--------|--------|
| 1 | [Dependencies without a `using` line of their own are not checked](#1-dependencies-without-a-using-line-of-their-own-are-not-checked) | Rules silently not enforced | Fixed |
| 2 | [Razor components are never checked](#2-razor-components-are-never-checked) | Rules silently not enforced | Fixed |
| 3 | [Explicitly typed `foreach` and `catch` variables are checked as `var`](#3-explicitly-typed-foreach-and-catch-variables-are-checked-as-var) | False DG0001s | Fixed |
| 4 | [Code without a namespace can't be named in a rule](#4-code-without-a-namespace-cant-be-named-in-a-rule) | False DG0001s, or no check | Fixed |
| 5 | [Nested namespace blocks are reported by their last part](#5-nested-namespace-blocks-are-reported-by-their-last-part) | Wrong rules apply | Fixed |
| 6 | [A using above several namespaces counts for the first one only](#6-a-using-above-several-namespaces-counts-for-the-first-one-only) | Wrong rules apply | Fixed |
| 7 | [Mistakes in a rule file are accepted silently](#7-mistakes-in-a-rule-file-are-accepted-silently) | A `denied` rule with a typo allows | Fixed |
| 8 | [Rule locations are one line and one column off](#8-rule-locations-are-one-line-and-one-column-off) | DG0003 points at the wrong line | Fixed |
| 9 | [A broken rule file gives DG0004 without a location, plus DG0000](#9-a-broken-rule-file-gives-dg0004-without-a-location-plus-dg0000) | Confusing errors | Fixed |
| 10 | [The conflict check never sees `.*` as a parent](#10-the-conflict-check-never-sees--as-a-parent) | Ambiguous rules not reported | Fixed |
| 11 | [`exposedTo` can't limit what a broader `allowed` rule allows](#11-exposedto-cant-limit-what-a-broader-allowed-rule-allows) | Rule silently not enforced | `exposedTo` removed |
| 12 | [CLI: `--help` is taken as the target, and so is any unknown option](#12-cli---help-is-taken-as-the-target-and-so-is-any-unknown-option) | Confusing errors | Fixed |
| 13 | [CLI: `generate --output` fails for more than one project](#13-cli-generate---output-fails-for-more-than-one-project) | No solution-wide file | Fixed |
| 14 | [CLI: `--config` replaces the project's own file](#14-cli---config-replaces-the-projects-own-file) | CLI and build disagree | Fixed |
| 15 | [CLI and analyzer see different code](#15-cli-and-analyzer-see-different-code) | CLI and build disagree | Fixed |
| 16 | [Every test run leaves a package in the NuGet cache](#16-every-test-run-leaves-a-package-in-the-nuget-cache) | 58 stale versions on one machine | Fixed |
| 17 | [Exact rules that can never meet are reported as a conflict](#17-exact-rules-that-can-never-meet-are-reported-as-a-conflict) | False DG0003 | Fixed |

## 1. Dependencies without a `using` line of their own are not checked

**What happens.** Only `using` lines, fully qualified names and `var` were checked. A dependency that needs no `using`
line of its own went unseen:

```csharp
// ImplicitUsings (on in every new SDK project) makes System.IO a global using: no rule is needed for this.
namespace MyApp.Domain;

public class Report
{
    public void Save(string path) { File.WriteAllText(path, "..."); }   // not reported
}
```

The same goes for a `global using` or `<Using>` item, an alias (`using Store = MyApp.Infrastructure.Repository;`), a
`using static`, an extension method, and a type from an enclosing namespace (code in `MyApp.Orders.Api` sees
`MyApp.Orders` without a using). The ThirdPartyIsolation demo shows it: delete its `using System.IO;` (the IDE even
suggests it, as the line is redundant) and it builds without a warning, while `ReportService` still writes files.

In Quiz Night this hid the web library's use of dependency injection, configuration and hosting (all `<Using>` items)
and the four places the pages use the media URL helper.

**Cause.** `Analyzer.AnalyzeTypeReference` only looked at names with a namespace on the left (`IsNamespaceQualified`),
and `AnalyzeUsingDirective` skipped global, static and alias usings on purpose.

**Fix.** Every name is resolved (`SemanticModel.GetSymbolInfo`) and checked against the namespace of the type it refers
to: a type name, an attribute (its constructor), a member of a static class used without the class name (a static
using or an extension method), or a static member of another class used without its name (a static using). A name
after a type (`Outer.Inner`, `Type.Member`) is left to the name on the left, so a dependency is reported once. The
checks live in `AnalyzerCompilation`.

**Tests.** `TestsAnalyzer`: `Analyzer_ShouldReportDG0001AtTheUse_WhenTypeComesFromGlobalUsing`, `..._FromAlias`,
`..._WhenMemberComesFromStaticUsing`, `Analyzer_ShouldReportDG0001AtTheCall_WhenExtensionMethodComesFromAnotherNamespace`,
`Analyzer_ShouldReportDG0001_WhenTypeComesFromEnclosingNamespace`, `Analyzer_ShouldReportOnce_WhenNestedTypeOrStaticMemberIsUsedThroughItsType`,
`Analyzer_ShouldReportTheTypesInAnInferredType_WhenVarIsUsed`. `IntegrationTestsAnalyzer.Build_ShouldReportDG0001_WhenTypeComesFromImplicitUsings`.

## 2. Razor components are never checked

**What happens.** Nothing in a `.razor` file was checked. Quiz Night's web library has 47 components. Its
`_Imports.razor` gives every one of them `@using QuizNight.Web.Internal.Hosting`, which the architecture forbids,
without a warning.

**Cause.** `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`: Razor compiles a component into
generated C#, and generated code was skipped entirely.

**Fix.** Generated code is analyzed, but a diagnostic is only reported where a `#line` directive maps the code back to a
file someone wrote (`Location.GetMappedLineSpan().HasMappedPath`, on a visible line). That is exactly what Razor does for
the code in a `.razor` file. The scaffolding it adds, and whatever other generators write, stays unchecked. The warning
shows up in the `.razor` file, at the right line. Razor copies the usings of `_Imports.razor` into every component: those
lines are reported once per namespace, not once per component (47 identical warnings before).

**Tests.** `TestsAnalyzer`: `Analyzer_ShouldReportWhatARazorFileUses_WhenGeneratedCodeIsMappedToIt`,
`Analyzer_ShouldReportAnImportsUsingOnce_WhenSeveralComponentsShareIt`, `Analyzer_ShouldSkipGeneratedCode_WhenNothingMapsItToAFile`.
`IntegrationTestsAnalyzer.Build_ShouldReportDG0001InTheRazorFile_WhenComponentUsesDisallowedNamespace` builds a real
Razor class library.

## 3. Explicitly typed `foreach` and `catch` variables are checked as `var`

**What happens.** A false DG0001 for a loop or catch variable that names its type:

```csharp
foreach (string item in items) { }        // warning DG0001: No rule allows 'MyApp.Domain' to depend on 'System'.
try { } catch (Exception exception) { }   // the same, when Exception comes from an implicit using
```

In Quiz Night 65 of the first 107 warnings were these.

**Cause.** `AnalyzeVariableDeclarator` decided whether a declaration was inferred with
`declarator.Syntax switch { VariableDeclaratorSyntax ... => IsVar, ForEachStatementSyntax f => f.Type.IsVar, _ => true }`.
Roslyn's declarator syntax for a foreach variable is its *type* (`string`), not the statement, and for a catch variable a
`CatchDeclarationSyntax`. Both fell through to `_ => true`.

**Fix.** The declarator check is gone. `var` is now checked where it is written: the `var` keyword resolves to the
inferred type, which is checked with the types in it (`List<Order>`: `System.Collections.Generic` and `Order`). That also
covers `out var` and `foreach (var ...)`.

**Tests.** `TestsAnalyzer.Analyzer_ShouldReportNoDiagnostics_WhenForeachAndCatchVariablesNameTheirType`,
`Analyzer_ShouldReportTheTypesInAnInferredType_WhenVarIsUsed`.

## 4. Code without a namespace can't be named in a rule

**What happens.** Top-level statements (every `Program.cs` of a modern app) and classes in the global namespace: their
`using` lines were checked with the source namespace `''`, which no rule can name (`from: ''` happened to work, but was
undocumented), while their fully qualified names were not checked at all. The CLI skipped these files.

**Fix.** Code without a namespace belongs to the project's `RootNamespace` (the package makes it visible to the analyzer
with `CompilerVisibleProperty`), or else to the assembly name. The CLI reads `<RootNamespace>` from the project file, or
uses the project file's name. A type in the global namespace is attributed the same way when it is the target. A rule
can now say `from: MyCompany.MyApp`.

**Tests.** `TestsAnalyzer`: `Analyzer_ShouldUseTheRootNamespace_WhenCodeHasNoNamespace`, `Analyzer_ShouldUseTheAssemblyName_WhenThereIsNoRootNamespace`,
`Analyzer_ShouldReportTheProjectAsTarget_WhenTypeHasNoNamespace`. `IntegrationTestsAnalyzer.Build_ShouldUseTheRootNamespace_WhenCodeHasNoNamespace`,
`IntegrationTestsTool.Tool_ShouldUseTheProjectName_WhenFileHasNoNamespace`.

## 5. Nested namespace blocks are reported by their last part

**What happens.**

```csharp
namespace MyApp
{
    namespace Application
    {
        using MyApp.Infrastructure;   // checked as 'Application', not 'MyApp.Application'
    }
}
```

**Cause.** The source namespace was the syntax of the nearest namespace declaration (`ns.Name.ToString()`).

**Fix.** The analyzer takes the namespace from the semantic model (`GetDeclaredSymbol`, `ContainingSymbol`). The CLI
joins the names of all enclosing declarations.

**Tests.** `TestsAnalyzer.Analyzer_ShouldReportTheFullNamespace_WhenNamespaceBlocksAreNested`,
`IntegrationTestsInspector.Generate_ShouldWriteTheFullNamespace_WhenNamespaceBlocksAreNested`.

## 6. A using above several namespaces counts for the first one only

**What happens.** A using at the top of a file applies to the whole file, but was checked for the first namespace
declared in it. In a file with top-level code and a namespace, or with two namespace blocks, the others were not
checked.

**Fix.** Such a using is checked for every namespace in the file, and for the root namespace when the file has code
outside a namespace.

**Tests.** `TestsAnalyzer.Analyzer_ShouldCheckAFileUsingForEveryNamespaceInTheFile_WhenUsingIsAboveThem`.

## 7. Mistakes in a rule file are accepted silently

**What happens.**

```yaml
allow:              # not 'allowed': the section is ignored
  - from: MyApp.Api
    to: MyApp.Domain
denied:
  - form: .*        # not 'from': the rule gets the source '' and never matches...
    to: System.IO.*  # ...so System.IO stays allowed
  - from: MyApp.*.Api   # a wildcard in the middle: taken literally, never matches
    to: System.Net.*
```

A mistake in an `allowed` rule shows up as DG0001s that are hard to explain. In a `denied` rule it silently allows what
the rule was meant to deny.

**Fix.** The parser reports every mistake with its location (`RuleSetException`, DG0004 in the build): an unknown key
(`allow`, `form`), a section without rules, a missing or empty `from` or `to`, a rule that is not a mapping, and a pattern that is not `.*`, a
namespace, or a namespace followed by `.*`.

**Tests.** `TestsDependencyRuleSetParserYaml`: `Parse_ShouldReportError_WhenSectionNameIsMisspelled`,
`Parse_ShouldReportError_WhenRuleKeyIsMisspelled`, `Parse_ShouldReportError_WhenRuleIsIncomplete`,
`Parse_ShouldReportError_WhenPatternIsNotANamespacePattern`, `Parse_ShouldReportEveryError_WhenFileHasSeveralMistakes`,
and more. `TestsAnalyzer.Analyzer_ShouldReportDG0004AtTheMistakeAndNothingElse_WhenRuleFileIsInvalid`,
`IntegrationTestsAnalyzer.Build_ShouldFailWithDG0004AtTheLine_WhenRuleFileHasATypo`, `IntegrationTestsTool.Tool_ShouldReportTheLine_WhenRuleFileHasATypo`.

## 8. Rule locations are one line and one column off

**What happens.** A DG0003 (conflicting rules) pointed at the line below the rule.

**Cause.** YamlDotNet counts lines and columns from 1. `SourceLocation` is documented, and used, as 0-based.

**Fix.** The parser converts.

**Tests.** `TestsDependencyRuleSetParserYaml.Parse_ShouldGiveZeroBasedLocations_WhenPathIsGiven`,
`TestsAnalyzer.Analyzer_ShouldPointDG0003AtTheRules_WhenRulesConflict`.

## 9. A broken rule file gives DG0004 without a location, plus DG0000

**What happens.** A YAML syntax error gave a DG0004 whose message was the exception's `ToString()`, without a location.
When it was the only rule file there was also a DG0000 ("configuration not found"). When there were more, the others
were used on their own, which gave DG0001s that made no sense.

**Fix.** One DG0004 per mistake, at its line. No DG0000. Nothing is checked until the files are right.

**Tests.** `TestsAnalyzer.Analyzer_ShouldReportDG0004AtTheMistakeAndNothingElse_WhenRuleFileIsInvalid`,
`TestsDependencyRuleSetParserYaml.Parse_ShouldReportErrorWithLocation_WhenYamlIsMalformed`.

## 10. The conflict check never sees `.*` as a parent

**What happens.** No DG0003 for crossed rules when one of them starts from `.*`:

```yaml
allowed:
  - from: .*
    to: System.IO.*
denied:
  - from: MyApp.*
    to: System.*
```

For `MyApp` → `System.IO` the allow wins on its more specific target, although the deny names `MyApp`. The same rules
with `from: Root.*` instead of `.*` were reported.

**Cause.** `RuleSetValidator.PatternBaseIsStrictPrefix` tested `descendant.StartsWith(ancestor + ".")`. The base of `.*`
is empty, and no namespace starts with `.`.

**Fix.** An empty base is the parent of every other pattern. The usual carve-out (`allowed: .* → System.*`,
`denied: MyApp.Core.* → System.IO.*`) is still fine.

**Tests.** `TestsConflictDetection`: `TryCreateAnalyzer_ShouldReportConflict_WhenCrossingAllowComesFromEveryNamespace`,
`..._WhenCrossingDenyComesFromEveryNamespace`, `TryCreateAnalyzer_ShouldReturnNoConflicts_WhenDenyCarvesOutOfAnAllowForEveryNamespace`.

## 11. `exposedTo` can't limit what a broader `allowed` rule allows

**What happens.** `exposedTo` was documented as "Limits who may use a namespace. Everyone else is denied.", but it was
only consulted when no `allowed` or `denied` rule matched. Next to the common `allowed: .* → System.*`, an
`exposedTo: System.IO.Compression → [MyApp.Storage]` limited nothing. Since nothing is allowed by default, it could only
ever allow.

**Status.** Not fixed: `exposedTo` was removed on the owner's request (first commit of this branch). A limit is written as
a `denied` rule with the same `from` as the broader `allowed` rule, plus `allowed` rules for the consumers. For the
example, that is `denied: .* → System.IO.Compression.*` and `allowed: MyApp.Storage.* → System.IO.Compression.*`. A
`denied` rule with a broader `from` than the `allowed` rule it limits crosses it: next to `allowed: MyApp.* → MyApp.*`, a
`denied: .* → MyApp.Internal.*` is a DG0003. The deny needs `from: MyApp.*`.

## 12. CLI: `--help` is taken as the target, and so is any unknown option

**What happens.** `dependency-guard --help` printed `File not found: ...\--help`. A typo such as `--confg` became the
target as well.

**Fix.** `--help`, `-h` and `--version`. An unknown option or a second target is a usage error (exit code 2).

**Tests.** `IntegrationTestsTool`: `Tool_ShouldShowUsageAndExitWithCode0_WhenHelpIsAsked`, `Tool_ShouldShowTheVersion_WhenVersionIsAsked`,
`Tool_ShouldExitWithCode2_WhenOptionIsUnknown`.

## 13. CLI: `generate --output` fails for more than one project

**What happens.** `generate --output rules.yaml MySolution.slnx` wrote the first project's rules and failed on the
second ("already exists"). With `--force` each project overwrote the last. There was no way to generate the
solution-wide file the README recommends.

**Fix.** With `--output`, the rules of all projects go into that one file.

**Tests.** `IntegrationTestsInspector.Generate_ShouldWriteOneFileForAllProjects_WhenOutputIsGivenForASolution`.

## 14. CLI: `--config` replaces the project's own file

**What happens.** In the build every `dependency-guard.yaml` is merged (a solution file from `Directory.Build.props` plus
the project's own). The CLI used either the `--config` file or the project's file, so it could not check such a setup,
and could disagree with the build.

**Fix.** `--config` files (the option may be repeated) are merged with the project's own file.

**Tests.** `IntegrationTestsTool.Tool_ShouldMergeConfigFromFlagWithProjectConfig_WhenProjectHasItsOwnConfig`,
`Tool_ShouldMergeEveryConfigFromFlags_WhenFlagIsRepeated`.

## 15. CLI and analyzer see different code

**What happens.** On Quiz Night, `generate` and the analyzer disagreed on 107 references. The CLI reads the `.cs` files in
a project's folder and only their syntax: it misses linked files (`<Compile Include="..\shared\*.cs" />`), `.razor`
files, fully qualified names in expressions (`System.Text.Encoding.UTF8`), and everything bug 1 is about. It includes
files the project excludes.

**Fix.** The CLI no longer reads syntax itself. It restores the projects, loads them with MSBuild through Roslyn's
workspace (5.3, the compiler of the .NET 10 SDK: the same C# the build compiles), source generators included, and runs
the analyzer on that compilation, with the rule files the build gives the project (or the `dependency-guard.yaml` in
its folder, for a project without the package) and the `--config` files. `generate` runs the analyzer with a rule
file that allows nothing and collects the namespaces every DG0001 carries as properties. The CLI now needs the .NET 10
SDK, and a whole solution takes seconds (Quiz Night, 16 projects: about 15 s).

**Tests.** `IntegrationTestsTool`: `Tool_ShouldReportTheUseInTheRazorFile_WhenComponentUsesDisallowedNamespace`,
`Tool_ShouldReportDG0001_WhenTypeComesFromImplicitUsings`, `Tool_ShouldCheckALinkedFile_WhenProjectCompilesASourceFromElsewhere`,
`Tool_ShouldUseTheRuleFilesOfTheBuild_WhenMSBuildGivesThemToTheProject`.
`IntegrationTestsInspector.Generate_ShouldWriteWhatTheCompilerSees_WhenTypeComesFromImplicitUsings`.

## 16. Every test run leaves a package in the NuGet cache

**What happens.** The integration tests pack the analyzer as `0.1.0-int<guid>` and build projects that restore it into
the machine's global package folder. Every run added a version (58 on the author's machine). The demo does the same
with `0.1.0-dev.<time>`.

**Fix.** The test projects and the demo solution restore into a folder of their own (`globalPackagesFolder` in their
`NuGet.Config`): the tests' temporary folder, and `LocalPackages/Cache` for the demo.

## 17. Exact rules that can never meet are reported as a conflict

**What happens.** A false DG0003 for rules that no dependency matches both:

```yaml
allowed:
  - from: Services
    to: System            # exactly System, not System.IO
denied:
  - from: .*
    to: System.IO.*
```

`generate` writes exact rules like the first one (for what the implicit usings bring in), so a generated file next to a
hand-written deny failed with DG0003.

**Cause.** `RuleSetValidator` checked whether one pattern's base starts with the other's, wildcard or not. That dates from
when a bare pattern still matched its children (a comment in the analyzer tests says `to: System` "now" matches only
`System`). Two conflict tests still asserted it for exact rules.

**Fix.** Only a wildcard covers other namespaces: `MyApp.*` is the parent of `MyApp.Api`, `.*` of everything, an exact
`MyApp` of nothing. The two tests now use rules that really cross.

**Tests.** `TestsConflictDetection.TryCreateAnalyzer_ShouldReturnNoConflicts_WhenExactPatternsCannotMeet`, and the
crossing tests in `TestsConflictDetection` and `TestsAnalyzer`.
