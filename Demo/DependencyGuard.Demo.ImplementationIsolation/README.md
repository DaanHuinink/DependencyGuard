# Implementation isolation

Implementations hidden behind contracts, even when everything lives in one assembly.

## The pattern

Consumers reach implementations only through contracts. `External` holds the contracts, `Internal.Email` and `Internal.Sms` implement them, and `Dispatching` uses them. All types are `internal` to a single assembly, so C# access modifiers can't keep the implementations apart. Namespace rules can.

```text
                           ┌─────────×──────────┐
                           │                    ▼
┌─────────────┐   ┌────────┴───────┐   ┌────────────────┐
│ Dispatching │   │ Internal.Email │   │  Internal.Sms  │
└──────┬──────┘   └────────┬───────┘   └────────┬───────┘
       │                   │                    │
       ▼                   ▼                    ▼
┌───────────────────────────────────────────────────────┐
│                 External (contracts)                  │
└───────────────────────────────────────────────────────┘
```

Arrows show the dependencies that are allowed. The path marked `×` is the dependency the pattern rules out.

## What breaks it

One implementation depending on another. `EmailFallback` in `Internal.Email` creates an `SmsSender` directly, coupling the email implementation to the SMS one. The compiler accepts this, because both types are internal to the same project.

## Enforcement

[`dependency-guard.yaml`](dependency-guard.yaml) allows `Dispatching` and `Internal.*` to use `External`. Nothing allows `Internal.Email` to use `Internal.Sms`, so that `using` is reported as DG0001.
