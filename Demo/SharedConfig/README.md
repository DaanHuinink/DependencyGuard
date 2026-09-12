# One rule set for several services

Services with the same architecture share a single configuration instead of copies that drift apart.

## The pattern

The `Orders` and `Customers` services follow the same layered architecture: `Application` and `Infrastructure` depend on `Domain`, and `Application` never uses `Infrastructure` directly. Since the rules are identical, they live in one `dependency-guard.yaml` in the folder that contains both projects.

```text
       ┌─────────×─────────┐
       │                   ▼
┌──────┴──────┐   ┌────────────────┐
│ Application │   │ Infrastructure │
└──────┬──────┘   └────────┬───────┘
       │                   │
       ▼                   ▼
┌──────────────────────────────────┐
│              Domain              │
└──────────────────────────────────┘
```

Arrows show the dependencies that are allowed in both services. The path marked `×` is the dependency the pattern rules out.

## What breaks it

The application layer bypassing the domain: in both services, the application service depends on the repository class from `Infrastructure` instead of the domain interface.

## Enforcement

`Directory.Build.props` in this folder points `DependencyGuardConfigPath` at the shared [`dependency-guard.yaml`](dependency-guard.yaml), so both projects load the same rules. Both services use the same namespace names, so two rules cover them: `Application` and `Infrastructure` may use `Domain`. `Application` using `Infrastructure` is reported as DG0001 in each project.
