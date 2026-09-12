# Dependency inversion

High-level policy owns the abstractions, and low-level details implement them.

## The pattern

`Domain` defines `ICustomerRepository`. `Application.Persistence` implements it and `Application` uses it. Every dependency points towards `Domain`, so the domain never depends on how data is stored.

```text
┌─────────────┐   ┌─────────────────────────┐
│ Application │   │ Application.Persistence │◄──┐
└──────┬──────┘   └────────────┬────────────┘   │
       │                       │                ×
       ▼                       ▼                │
┌───────────────────────────────────────────┐   │
│                  Domain                   ├───┘
└───────────────────────────────────────────┘
```

Arrows show the dependencies that are allowed. The path marked `×` is the dependency the pattern rules out.

## What breaks it

The domain depending on an implementation. `CustomerService` in `Domain` creates a `CustomerRepository` from `Application.Persistence`, which turns the dependency around.

## Enforcement

The key rule in [`dependency-guard.yaml`](dependency-guard.yaml) allows `Application` and its sub-namespaces to use `Domain`. DependencyGuard denies everything that isn't allowed, so `Domain` using `Application.Persistence` is reported as DG0001 without an explicit deny rule.
