# GM.FeatureManagement.Samples

Runnable usage for [GM.FeatureManagement](https://github.com/gmetskhvarishvili/GM.FeatureManagement) —
feature flags for the `GM.*` ecosystem — built around its two primary use cases.

## Run it

```bash
dotnet run --project GM.FeatureManagement.Sample.API
```

**Use case 1 — gradual KYC vendor switch (sticky, weighted variants).** 25% of users are routed to
Identomat, the rest stay on Didit; a given user always routes the same way:

```bash
curl "http://localhost:5xxx/kyc/route?userId=alice"
# { "userId": "alice", "vendor": "Didit",     "endpoint": null }
curl "http://localhost:5xxx/kyc/route?userId=bob"
# { "userId": "bob",   "vendor": "Identomat", "endpoint": "https://api.identomat.example" }
```

**Use case 2 — environment-gated in-progress feature.** `NewPayments` is enabled only in
Development/Staging, so it's off in prod until it's stable:

```bash
curl "http://localhost:5xxx/features/new-payments?userId=alice"
# development → { ..., "enabled": true }   |   production → { ..., "enabled": false }
```

`GET /features` lists all defined flags (an admin/diagnostic view over `IFeatureDefinitionProvider`).

## What it shows

- `AddGMFeatureManagement(configuration, postConfigure)` — flags from **appsettings** plus a
  code-defined default, and wiring the ASP.NET host environment into evaluation
  (`o.EnvironmentName = builder.Environment.EnvironmentName`).
- **Sticky, weighted variant** routing for a real rollout (`GetVariantAsync`), with per-variant
  configuration (the Identomat endpoint).
- **Environment gating** for shipping in-progress features safely (`IsEnabledAsync`).
- In-memory cache by default; set `Redis:ConnectionString` to share flag state across instances so
  rollouts stay consistent fleet-wide.

## Tests

```bash
dotnet test
```

`WebApplicationFactory` drives the app end to end: it proves per-user vendor routing is **sticky** and
**splits traffic** across both vendors, and that the environment gate flips `NewPayments` between
Production (off) and Development (on).

> References the sibling `GM.FeatureManagement` source repo by project path. Once the package is
> published, swap the `ProjectReference` in the API csproj for a `PackageReference`.
