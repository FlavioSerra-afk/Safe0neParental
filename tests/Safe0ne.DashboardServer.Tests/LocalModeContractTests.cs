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
    public async Task ReportsSchedule_Routes_Exist_And_RunNow_Works()
    {
        using var client = _factory.CreateClient();

        // Create a child
        var create = await client.PostAsync("/api/local/children", new StringContent("{\"displayName\":\"Test Kid\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var createdJson = await create.Content.ReadAsStringAsync();
        // Quick parse: look for "id":"..."
        var idStart = createdJson.IndexOf("\"id\":\"", StringComparison.Ordinal);
        Assert.True(idStart >= 0, "Expected created child id in response");
        var id = createdJson.Substring(idStart + 6, 36);

        // Put a schedule
        var put = await client.PutAsync($"/api/local/children/{id}/reports/schedule",
            new StringContent("{\"enabled\":true,\"digest\":{\"frequency\":\"daily\",\"timeLocal\":\"18:00\",\"weekday\":\"sun\"}}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Get it back
        var get = await client.GetAsync($"/api/local/children/{id}/reports/schedule");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        // Run now
        var run = await client.PostAsync($"/api/local/children/{id}/reports/run-now", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
    }

}
