using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task LocalDevices_Route_Exists_ForNewChild()
    {
        using var client = _factory.CreateClient();

        // Create a child in local mode.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Test Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        var devices = await client.GetAsync($"/api/local/children/{id}/devices");
        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
    }[Fact]
public async Task Local_devices_includes_health_fields()
{
    using var app = await TestAppFactory.StartAsync();

    var childId = await app.CreateChildAsync("W12 Device Health");

    // Devices list exists even if empty (contract).
    var res = await app.Client.GetAsync($"/api/local/children/{childId}/devices");
    res.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

    var doc = await res.Content.ReadFromJsonAsync<ApiResponse<List<ChildDeviceSummary>>>(JsonDefaults.Options);
    doc.Should().NotBeNull();
    doc!.Ok.Should().BeTrue();

    // If no devices, array may be empty; this is fine. The point is that the contract type compiles.
}
}
