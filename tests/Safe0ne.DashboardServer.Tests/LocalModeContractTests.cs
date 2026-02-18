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
    }

    [Fact]
    public async Task LocalDeviceRevoke_InvalidatesToken_ButKeepsDeviceRecord()
    {
        using var client = _factory.CreateClient();

        // Create a child in local mode.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Revoke Test Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        // Start pairing (v1).
        var start = await client.PostAsync($"/api/v1/children/{childId}/pair/start", content: null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var pairingCode = startDoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(pairingCode));

        // Complete pairing (v1) to obtain device token and id.
        var complete = await client.PostAsJsonAsync($"/api/v1/children/{childId}/pair/complete", new
        {
            pairingCode,
            deviceName = "TestDevice",
            agentVersion = "test"
        });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using var completeDoc = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
        var deviceId = completeDoc.RootElement.GetProperty("data").GetProperty("deviceId").GetString();
        var token = completeDoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(deviceId));
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Heartbeat should succeed with token when devices exist.
        using (var hbReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat"))
        {
            hbReq.Headers.Add("X-Safe0ne-Device-Token", token);
            hbReq.Content = JsonContent.Create(new
            {
                deviceName = "TestDevice",
                agentVersion = "test",
                sentAtUtc = DateTimeOffset.UtcNow
            });

            var hbRes = await client.SendAsync(hbReq);
            Assert.Equal(HttpStatusCode.OK, hbRes.StatusCode);
        }

        // Revoke token in local mode.
        var revoke = await client.PostAsJsonAsync($"/api/local/devices/{deviceId}/revoke", new { revokedBy = "test", reason = "unit" });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        // Device record should still exist.
        var devices = await client.GetAsync($"/api/local/children/{childId}/devices");
        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);

        var devicesJson = await devices.Content.ReadAsStringAsync();
        Assert.Contains(deviceId!, devicesJson);

        // Heartbeat must now fail with the old token.
        using (var hbReq2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat"))
        {
            hbReq2.Headers.Add("X-Safe0ne-Device-Token", token);
            hbReq2.Content = JsonContent.Create(new
            {
                deviceName = "TestDevice",
                agentVersion = "test",
                sentAtUtc = DateTimeOffset.UtcNow
            });

            var hbRes2 = await client.SendAsync(hbReq2);
            Assert.Equal(HttpStatusCode.Unauthorized, hbRes2.StatusCode);
        }
    }

    [Fact]
    public async Task LocalReports_ScheduleAndRunNow_AreAvailable_AndEmitDigestActivity()
    {
        using var client = _factory.CreateClient();

        // Create a child in local mode.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Reports Test Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        // GET schedule envelope exists.
        var get0 = await client.GetAsync($"/api/local/children/{childId}/reports/schedule");
        Assert.Equal(HttpStatusCode.OK, get0.StatusCode);

        // PUT schedule should succeed.
        var put = await client.PutAsJsonAsync($"/api/local/children/{childId}/reports/schedule", new
        {
            enabled = true,
            digest = new { frequency = "daily", timeLocal = "18:00", weekday = "sun" }
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Run now should emit a digest activity event.
        var run = await client.PostAsJsonAsync($"/api/local/children/{childId}/reports/run-now", new { });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);

        // Query recent activity and ensure report_digest appears.
        var from = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
        var act = await client.GetAsync($"/api/local/children/{childId}/activity?from={Uri.EscapeDataString(from)}&take=100");
        Assert.Equal(HttpStatusCode.OK, act.StatusCode);

        var actJson = await act.Content.ReadAsStringAsync();
        Assert.Contains("report_digest", actJson);
    }


    [Fact]
    public async Task LocalActivity_ExportEndpoint_ReturnsEnvelopeWithEvents()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Activity Export Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        // Append one activity event.
        var occurredAt = DateTimeOffset.UtcNow.ToString("O");
        var post = await client.PostAsJsonAsync($"/api/local/children/{childId}/activity", new[]
        {
            new { kind = "unit_test", occurredAtUtc = occurredAt, message = "hello" }
        });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        // Export should return a stable envelope with events array containing the item.
        var exp = await client.GetAsync($"/api/local/children/{childId}/activity/export");
        Assert.Equal(HttpStatusCode.OK, exp.StatusCode);

        var expJson = await exp.Content.ReadAsStringAsync();
        Assert.Contains(""events"", expJson);
        Assert.Contains("unit_test", expJson);
    }
}
