using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GM.FeatureManagement.Sample.Tests;

// Spins the real sample app up in-memory and drives the two primary use cases end to end.
public class FeatureSampleTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task KycRoute_IsStickyPerUser_AndSplitsAcrossVendors()
    {
        var client = factory.CreateClient();

        async Task<string> Vendor(string userId)
        {
            var doc = await client.GetFromJsonAsync<JsonElement>($"/kyc/route?userId={userId}");
            return doc.GetProperty("vendor").GetString()!;
        }

        // Every user routes to a known vendor, and the same user always routes the same way.
        var vendors = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var v = await Vendor($"user-{i}");
            Assert.Contains(v, new[] { "Didit", "Identomat" });
            Assert.Equal(v, await Vendor($"user-{i}")); // sticky
            vendors.Add(v);
        }

        // Across 200 users the rollout actually splits traffic (both arms are hit).
        Assert.Contains("Didit", vendors);
        Assert.Contains("Identomat", vendors);
    }

    [Fact]
    public async Task NewPayments_IsOff_InProduction()
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        var doc = await client.GetFromJsonAsync<JsonElement>("/features/new-payments?userId=alice");
        Assert.False(doc.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task NewPayments_IsOn_InDevelopment()
    {
        var client = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var doc = await client.GetFromJsonAsync<JsonElement>("/features/new-payments?userId=alice");
        Assert.True(doc.GetProperty("enabled").GetBoolean());
    }
}
