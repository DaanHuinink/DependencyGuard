# Third-party isolation

An external API wrapped in one place, so the rest of the code depends only on your own abstraction.

## The pattern

Only `FileOperations` talks to `System.IO`. It implements `IFileService` from `Abstractions`, and the rest of the code, such as `Services`, only knows that interface. File access stays in one place that is easy to test, audit and replace.

```text
     ┌──────────────×──────────────┐
     │                             │
┌────┴─────┐   ┌────────────────┐  │
│ Services │   │ FileOperations │  │
└────┬─────┘   └───┬────────┬───┘  │
     │             │        │      │
     ▼             ▼        ▼      ▼
┌─────────────────────┐  ┌───────────┐
│    Abstractions     │  │ System.IO │
└─────────────────────┘  └───────────┘
```

Arrows show the dependencies that are allowed. The path marked `×` is the dependency the pattern rules out.

## What breaks it

Code outside the wrapper calling the API directly. `ReportService` in `Services` writes its report with `System.IO.File` instead of going through `IFileService`.

## Enforcement

[`dependency-guard.yaml`](dependency-guard.yaml) denies `System.IO.*` to every namespace, then allows it again for `FileOperations`, because the more specific rule wins. Every namespace may use `Abstractions`. The `using System.IO;` in `ReportService` is reported as DG0001.
