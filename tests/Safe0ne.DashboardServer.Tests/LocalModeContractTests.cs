using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Safe0ne.DashboardServer.Tests;

public sealed class LocalModeContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalModeContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LocalHealth_IsAvailable()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/local/_health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task LocalChildren_CollectionRoute_Exists()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/local/children");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }


    [Fact]
    public async Task LocalAudit_Endpoint_Exists()
    {
        using var client = _factory.CreateClient();
        // Seed childId (deterministic)
        var childId = "11111111-1111-1111-1111-111111111111";
        var res = await client.GetAsync($"/api/local/children/{childId}/audit");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

}
