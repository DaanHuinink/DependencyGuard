# Plugin / provider pattern

One contract, several interchangeable implementations, each of which can change or disappear without affecting the others.

## The pattern

`Notifications` defines the contract: `INotificationSender` and `NotificationRequest`. The `Email` and `Sms` providers each implement it. Providers depend on the contract and never on each other, which keeps every provider replaceable on its own.

```text
     ┌──────×──────┐
     │             ▼
┌────┴────┐   ┌─────────┐
│  Email  │   │   Sms   │
└────┬────┘   └────┬────┘
     │             │
     ▼             ▼
┌───────────────────────┐
│     Notifications     │
└───────────────────────┘
```

Arrows show the dependencies that are allowed. The path marked `×` is the dependency the pattern rules out.

## What breaks it

A provider that reaches into a sibling. In `Demo.cs`, `ProviderDispatcher` in `Email` falls back to `SmsSender`, so the email provider can no longer exist without the SMS one.

## Enforcement

[`dependency-guard.yaml`](dependency-guard.yaml) allows each provider to use the contract namespace and `System.*`, and nothing else. No rule connects `Email` and `Sms`, so that dependency falls under the default deny and is reported as DG0001.
