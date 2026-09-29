# Deny list

Everything is allowed, except the namespaces you explicitly ban.

## The pattern

Not every codebase needs a strict allow list. When you introduce DependencyGuard into an existing project, or only want to keep one API out, start from the other side: allow every dependency and deny just the ones you don't want. Here that is `System.Reflection`, which sidesteps compile-time checks and is easy to misuse.

```text
                    ┌─────────×─────────┐
                    │                   │
┌──────────┐   ┌────┴────┐              │
│  Orders  │   │ Plugins │              │
└────┬─────┘   └────┬────┘              │
     │              │                   │
     ▼              ▼                   ▼
┌─────────────────────────┐   ┌───────────────────┐
│   Any other namespace   │   │ System.Reflection │
└─────────────────────────┘   └───────────────────┘
```

Arrows show the dependencies that are allowed. The path marked `×` is the dependency the pattern rules out.

## What breaks it

Any use of the banned namespace. `PluginLoader` in `Plugins` creates objects through `System.Reflection` instead of constructing them directly.

## Enforcement

[`dependency-guard.yaml`](dependency-guard.yaml) allows every namespace to use every other namespace, then denies `System.Reflection.*` to all of them. The deny rule has the more specific `to`, so it wins. The `using System.Reflection;` in `PluginLoader` is reported as DG0001, while `Orders` can still use `Customers` and `System.Text`.
