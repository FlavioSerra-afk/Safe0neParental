using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Safe0ne.Shared.Contracts;
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

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Test Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        var devices = await client.GetAsync($"/api/local/children/{id}/devices");
        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
    }

    // Policy runtime contract: version bumps on save + heartbeat ack persists to SSOT status.
    [Fact]
    public async Task PolicyVersion_Bumps_And_Heartbeat_Ack_Persists()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Policy Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        var get1 = await client.GetAsync($"/api/local/children/{id}/policy");
        Assert.Equal(HttpStatusCode.OK, get1.StatusCode);

        using var doc1 = JsonDocument.Parse(await get1.Content.ReadAsStringAsync());
        var v1 = doc1.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt32();

        var put = await client.PutAsJsonAsync($"/api/local/children/{id}/policy", new { policy = new { mode = "Lockdown" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var putDoc = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        var v2 = putDoc.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt32();
        Assert.True(v2 > v1);

        // Heartbeat: ack applied policy version and confirm status endpoint returns applied fields.
        var hb = new ChildHeartbeatRequest(
            ChildId: id!,
            DeviceId: "dev-test",
            DeviceName: "Test Device",
            OsVersion: "win",
            AppVersion: "test",
            UtcNow: DateTimeOffset.UtcNow,
            PolicyAppliedVersion: v2,
            PolicyAppliedAtUtc: DateTimeOffset.UtcNow,
            PolicyApplyFailedAtUtc: null,
            PolicyApplyError: null,
            Telemetry: null);

        var hbRes = await client.PostAsJsonAsync($"/api/local/children/{id}/heartbeat", hb);
        Assert.Equal(HttpStatusCode.OK, hbRes.StatusCode);

        var statusRes = await client.GetAsync($"/api/v1/children/{id}/status");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);

        using var statusDoc = JsonDocument.Parse(await statusRes.Content.ReadAsStringAsync());
        var appliedVersion = statusDoc.RootElement.GetProperty("data").GetProperty("lastAppliedPolicyVersion").GetInt32();
        Assert.Equal(v2, appliedVersion);
    }
}
