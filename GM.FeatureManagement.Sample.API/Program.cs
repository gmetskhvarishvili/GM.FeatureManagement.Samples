using GM.Caching;
using GM.Caching.Redis;
using GM.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "GM.FeatureManagement sample",
    environment = app.Environment.EnvironmentName,
    try_it = new[]
    {
        "GET /kyc/route?userId=alice   → which KYC vendor this user is routed to (sticky per user)",
        "GET /features/new-payments?userId=alice → whether the in-progress NewPayments feature is on",
        "GET /features               → all defined flags (admin/diagnostic listing)",
    },
}));

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

app.Run();

// Exposed so the test project can spin the app up with WebApplicationFactory.
public partial class Program;
