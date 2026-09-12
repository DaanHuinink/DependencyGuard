# DependencyGuard demos

Each project in this solution shows one architectural pattern and how DependencyGuard enforces it. Every project deliberately contains at least one violation, which shows up as a DG0001 warning when you build.

| Demo | Pattern |
|------|---------|
| [Notifications](DependencyGuard.Demo.Notifications/README.md) | Plugin / provider: `Email` and `Sms` use the `Notifications` contract, but not each other |
| [ImplementationIsolation](DependencyGuard.Demo.ImplementationIsolation/README.md) | Implementation isolation: `internal` implementations are reached only through contracts, which access modifiers alone can't enforce |
| [DependencyInversion](DependencyGuard.Demo.DependencyInversion/README.md) | Dependency inversion: `Domain` owns the repository interface and depends on nothing, while `Application.Persistence` implements it |
| [ThirdPartyIsolation](DependencyGuard.Demo.ThirdPartyIsolation/README.md) | Third-party isolation: only `FileOperations` may use `System.IO`, while the rest of the code goes through `IFileService` |
| [DenyList](DependencyGuard.Demo.DenyList/README.md) | Deny list: every dependency is allowed, except on `System.Reflection` |
| [SharedConfig](SharedConfig/README.md) | One `dependency-guard.yaml` shared by the `Orders` and `Customers` projects through `DependencyGuardConfigPath` |

## Running the demos

The demos use the analyzer as a NuGet package from the `LocalPackages` folder at the root of the repository. To build them against the latest source, run this from the repository root:

```bash
dotnet run Scripts/UpdateDemoToLatestAnalyzer.cs
```

In Rider, you can pick **Update demo to latest analyzer** from the run configurations instead. The script packs the analyzer with a unique prerelease version, so NuGet never serves a stale cached copy.

Then open `DependencyGuard.Demo.slnx` and build it. The violations appear as DG0001 warnings in the build output and in the editor.

## Layout

All namespaces of a project live in a single `Demo.cs`, declared with block-scoped `namespace { }` syntax. [`Directory.Build.props`](Directory.Build.props) holds the settings every project shares, including the reference to the analyzer package.
