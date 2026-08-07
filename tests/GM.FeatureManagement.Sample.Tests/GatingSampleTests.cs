using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GM.FeatureManagement.Sample.Tests;

// Verifies the ASP.NET gating end to end: both the minimal-API .RequireFeature filter and the MVC
// [FeatureGate] filter return 404 when NewPayments is gated off (Production) and 200 when on (Dev).
public class GatingSampleTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient ClientFor(string environment) =>
        factory.WithWebHostBuilder(b => b.UseEnvironment(environment)).CreateClient();

    [Theory]
    [InlineData("/payments/checkout")] // minimal API .RequireFeature("NewPayments")
    [InlineData("/api/payments")]      // MVC [FeatureGate("NewPayments")]
    public async Task GatedEndpoint_Returns404_InProduction(string path)
    {
        var response = await ClientFor("Production").GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/payments/checkout")]
    [InlineData("/api/payments")]
    public async Task GatedEndpoint_Returns200_InDevelopment(string path)
    {
        var response = await ClientFor("Development").GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
