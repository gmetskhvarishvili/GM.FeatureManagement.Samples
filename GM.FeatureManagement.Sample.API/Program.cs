using GM.Caching;
using GM.Caching.Redis;
using GM.FeatureManagement;
using GM.FeatureManagement.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// Cache backing for flag definitions. In-memory is fine for a single instance; point Redis at a
// shared instance so every replica evaluates flags identically (sticky rollouts across the fleet):
//   Redis__ConnectionString=localhost:6379
var redis = builder.Configuration.GetSection("Redis")["ConnectionString"];
if (!string.IsNullOrWhiteSpace(redis))
    builder.Services.AddGMRedisCaching(o => { o.ConnectionString = redis; o.KeyPrefix = "sample:"; });
else
    builder.Services.AddGMCaching();

// Flags bound from appsettings, then a code-defined default so the sample runs with no config.
var environment = builder.Environment.EnvironmentName; // wire the host env into feature evaluation
builder.Services.AddGMFeatureManagement(builder.Configuration.GetSection("FeatureManagement"), o =>
{
    o.CacheTtl = TimeSpan.FromSeconds(15);
    o.EnvironmentName = environment; // so EnabledEnvironments gates on the actual ASP.NET environment

    // Use case 1 — gradually shift KYC traffic from Didit to Identomat (25% to the new vendor).
    o.Features.TryAdd("KycVendorSwitch", new FeatureDefinition
    {
        Enabled = true,
        Variants =
        [
            new FeatureVariantDefinition { Name = "Didit", Weight = 75 },
            new FeatureVariantDefinition
            {
                Name = "Identomat",
                Weight = 25,
                Configuration = { ["endpoint"] = "https://api.identomat.example" },
            },
        ],
    });

    // Use case 2 — environment-gate an in-progress feature until it's stable.
    o.Features.TryAdd("NewPayments", new FeatureDefinition
    {
        Enabled = true,
        EnabledEnvironments = ["Development", "Staging"],
    });
});

// ASP.NET Core gating: [FeatureGate] on controllers + .RequireFeature(...) on minimal-API endpoints,
// with the request FeatureContext built from the caller's claims. A closed gate returns 404 (default).
builder.Services.AddGMFeatureManagementAspNetCore();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

// Liveness must not depend on downstream dependencies, so it runs no checks; readiness runs
// every registered health check (none here yet). See engineering baseline §11.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

string[] tryIt =
[
    "GET /kyc/route?userId=alice   → which KYC vendor this user is routed to (sticky per user)",
    "GET /features/new-payments?userId=alice → whether the in-progress NewPayments feature is on",
    "GET /features               → all defined flags (admin/diagnostic listing)",
    "GET /payments/checkout      → minimal-API endpoint gated by .RequireFeature(NewPayments) (404 in prod)",
    "GET /api/payments           → MVC controller action gated by [FeatureGate(NewPayments)] (404 in prod)",
];

app.MapGet("/", () => Results.Ok(new
{
    message = "GM.FeatureManagement sample",
    environment = app.Environment.EnvironmentName,
    try_it = tryIt,
}));

// ---- ASP.NET gating: a minimal-API endpoint behind the NewPayments flag ------------------------
// 404 when the gate is closed (prod), 200 when open (dev/staging) — the feature simply "isn't there".
app.MapGet("/payments/checkout", () => Results.Ok(new { status = "checkout ready (NewPayments is on)" }))
   .RequireFeature("NewPayments");

// ---- Use case 1: sticky per-user KYC vendor routing via a weighted variant --------------------
app.MapGet("/kyc/route", async (string userId, IFeatureManager features, CancellationToken ct) =>
{
    var variant = await features.GetVariantAsync("KycVendorSwitch", FeatureContext.ForUser(userId), ct);
    return Results.Ok(new
    {
        userId,
        vendor = variant.Name,               // "Didit" or "Identomat" — same every call for this user
        endpoint = variant["endpoint"],       // per-variant configuration, when present
    });
});

// ---- Use case 2: environment-gated feature ----------------------------------------------------
app.MapGet("/features/new-payments", async (string userId, IFeatureManager features, CancellationToken ct) =>
{
    var enabled = await features.IsEnabledAsync("NewPayments", FeatureContext.ForUser(userId), ct);
    return Results.Ok(new { userId, feature = "NewPayments", enabled, environment = app.Environment.EnvironmentName });
});

// ---- Admin/diagnostic: list all defined flags -------------------------------------------------
app.MapGet("/features", async (IFeatureDefinitionProvider provider, CancellationToken ct) =>
{
    var all = new List<object>();
    await foreach (var def in provider.GetAllFeatureDefinitionsAsync(ct))
        all.Add(new
        {
            def.Key,
            def.Enabled,
            environments = def.EnabledEnvironments,
            rolloutPercentage = def.RolloutPercentage,
            variants = def.Variants.Select(v => new { v.Name, v.Weight }),
        });
    return Results.Ok(all);
});

await app.RunAsync();

// Exposed so the test project can spin the app up with WebApplicationFactory.
public partial class Program
{
    protected Program() { }
}
