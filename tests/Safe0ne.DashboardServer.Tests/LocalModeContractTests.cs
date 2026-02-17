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
    public async Task PolicyVersion_Bumps_And_Heartbeat_Ack_Persists()
    {
        using var client = _factory.CreateClient();

        // Create a child in local mode.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Policy Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        // Read current policy envelope.
        var get1 = await client.GetAsync($"/api/local/children/{id}/policy");
        Assert.Equal(HttpStatusCode.OK, get1.StatusCode);
        using var doc1 = JsonDocument.Parse(await get1.Content.ReadAsStringAsync());
        var v1 = doc1.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt32();

        // Apply a policy change (mode) and ensure policyVersion increments.
        var put = await client.PutAsJsonAsync($"/api/local/children/{id}/policy", new { policy = new { mode = "Lockdown" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var putDoc = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        var v2 = putDoc.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt32();
        Assert.True(v2 > v1);

        // Send a heartbeat that acks the applied version.
        var hb = new
        {
            deviceName = "TestDevice",
            agentVersion = "0.0.0-test",
            sentAtUtc = DateTimeOffset.UtcNow,
            // extras are additive; server must persist into SSOT status
            lastAppliedPolicyVersion = (long)v2,
            lastAppliedPolicyEffectiveAtUtc = DateTimeOffset.UtcNow
        };
        var hbRes = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
        Assert.Equal(HttpStatusCode.OK, hbRes.StatusCode);

        // Verify status surfaces the applied version.
        var st = await client.GetAsync($"/api/v1/children/{id}/status");
        Assert.Equal(HttpStatusCode.OK, st.StatusCode);
        using var stDoc = JsonDocument.Parse(await st.Content.ReadAsStringAsync());
        var lastApplied = stDoc.RootElement.GetProperty("data").GetProperty("lastAppliedPolicyVersion").GetInt64();
        Assert.Equal(v2, lastApplied);
    }

    [Fact]
    public async Task DeviceToken_Revoke_MakesHeartbeatUnauthorized()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Token Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        // Start pairing
        var start = await client.PostAsync($"/api/local/children/{id}/devices/pair", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var code = startDoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        // Enroll device
        var enroll = await client.PostAsJsonAsync("/api/local/devices/enroll", new { pairingCode = code, deviceName = "TDev", agentVersion = "0.0.0-test" });
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        using var enrollDoc = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
        var token = enrollDoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
        var deviceId = enrollDoc.RootElement.GetProperty("data").GetProperty("deviceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.False(string.IsNullOrWhiteSpace(deviceId));

        // Authenticated heartbeat should be OK
        client.DefaultRequestHeaders.Remove(AgentAuth.DeviceTokenHeaderName);
        client.DefaultRequestHeaders.Add(AgentAuth.DeviceTokenHeaderName, token);
        var hb = new { deviceName = "TDev", agentVersion = "0.0.0-test", sentAtUtc = DateTimeOffset.UtcNow };
        var hbOk = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
        Assert.Equal(HttpStatusCode.OK, hbOk.StatusCode);

        // Revoke token
        var revoke = await client.PostAsJsonAsync($"/api/local/devices/{deviceId}/revoke", new { revokedBy = "test" });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        // Same token must now be rejected
        var hbFail = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
        Assert.Equal(HttpStatusCode.Unauthorized, hbFail.StatusCode);
    }

    [Fact]
    public async Task PolicyWatchdog_MarksOverdue_WhenAppliedLagsTooLong()
    {
        Environment.SetEnvironmentVariable("SAFE0NE_POLICY_WATCHDOG_SECONDS", "1");
        try
        {
            using var localFactory = new WebApplicationFactory<Program>();
            using var client = localFactory.CreateClient();

            var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Watchdog Child" });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));

            // Get current policy version (v1)
            var get1 = await client.GetAsync($"/api/local/children/{id}/policy");
            Assert.Equal(HttpStatusCode.OK, get1.StatusCode);
            using var doc1 = JsonDocument.Parse(await get1.Content.ReadAsStringAsync());
            var v1 = doc1.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt32();

            // Bump to v2
            var put = await client.PutAsJsonAsync($"/api/local/children/{id}/policy", new { policy = new { mode = "Lockdown" } });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            using var putDoc = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
            var v2 = putDoc.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt32();
            Assert.True(v2 > v1);

            // Heartbeat claims it only applied v1 => pending
            var hb = new { deviceName = "WDev", agentVersion = "0.0.0-test", sentAtUtc = DateTimeOffset.UtcNow, lastAppliedPolicyVersion = (long)v1 };
            var hb1 = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
            Assert.Equal(HttpStatusCode.OK, hb1.StatusCode);

            var st1 = await client.GetAsync($"/api/v1/children/{id}/status");
            Assert.Equal(HttpStatusCode.OK, st1.StatusCode);
            using var st1Doc = JsonDocument.Parse(await st1.Content.ReadAsStringAsync());
            var state1 = st1Doc.RootElement.GetProperty("data").GetProperty("policyApplyState").GetString();
            Assert.Equal("Pending", state1);

            await Task.Delay(1200);

            // Second heartbeat should preserve pendingSince and flip to overdue
            var hb2 = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
            Assert.Equal(HttpStatusCode.OK, hb2.StatusCode);

            var st2 = await client.GetAsync($"/api/v1/children/{id}/status");
            Assert.Equal(HttpStatusCode.OK, st2.StatusCode);
            using var st2Doc = JsonDocument.Parse(await st2.Content.ReadAsStringAsync());
            var overdue = st2Doc.RootElement.GetProperty("data").GetProperty("policyApplyOverdue").GetBoolean();
            var state2 = st2Doc.RootElement.GetProperty("data").GetProperty("policyApplyState").GetString();
            Assert.True(overdue);
            Assert.Equal("Overdue", state2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SAFE0NE_POLICY_WATCHDOG_SECONDS", null);
        }
    }


    [Fact]
    public async Task DeviceToken_Expires_WhenTtlIsShort()
    {
        // Deterministic TTL for this test.
        Environment.SetEnvironmentVariable("SAFE0NE_DEVICE_TOKEN_TTL_SECONDS", "1");
        try
        {
            using var localFactory = new WebApplicationFactory<Program>();
            using var client = localFactory.CreateClient();

            var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Expiry Child" });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));

            var start = await client.PostAsync($"/api/local/children/{id}/devices/pair", null);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
            var code = startDoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
            Assert.False(string.IsNullOrWhiteSpace(code));

            var enroll = await client.PostAsJsonAsync("/api/local/devices/enroll", new { pairingCode = code, deviceName = "EDev", agentVersion = "0.0.0-test" });
            Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
            using var enrollDoc = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
            var token = enrollDoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));

            client.DefaultRequestHeaders.Remove(AgentAuth.DeviceTokenHeaderName);
            client.DefaultRequestHeaders.Add(AgentAuth.DeviceTokenHeaderName, token);

            var hb = new { deviceName = "EDev", agentVersion = "0.0.0-test", sentAtUtc = DateTimeOffset.UtcNow };
            var hbOk = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
            Assert.Equal(HttpStatusCode.OK, hbOk.StatusCode);

            await Task.Delay(1500);

            var hbExpired = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hb);
            Assert.Equal(HttpStatusCode.Unauthorized, hbExpired.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SAFE0NE_DEVICE_TOKEN_TTL_SECONDS", null);
        }
    }

}

    [Fact]
    public async Task PolicyRollback_IsRecommended_OnFailure_And_RollbackEndpoint_RevertsSnapshot()
    {
        using var client = _factory.CreateClient();

        // Create a child in local mode.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Rollback Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        // v2: set Lockdown
        var put2 = await client.PutAsJsonAsync($"/api/v1/children/{id}/policy", new { mode = "Lockdown", updatedBy = "test" });
        Assert.Equal(HttpStatusCode.OK, put2.StatusCode);
        using var put2Doc = JsonDocument.Parse(await put2.Content.ReadAsStringAsync());
        var v2 = put2Doc.RootElement.GetProperty("data").GetProperty("version").GetProperty("value").GetInt64();
        Assert.True(v2 >= 2);

        // Heartbeat acks v2 successfully -> sets LKG.
        var hbOk = new
        {
            deviceName = "RDev",
            agentVersion = "0.0.0-test",
            sentAtUtc = DateTimeOffset.UtcNow,
            lastAppliedPolicyVersion = v2,
            lastAppliedPolicyEffectiveAtUtc = DateTimeOffset.UtcNow
        };
        var hbOkRes = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hbOk);
        Assert.Equal(HttpStatusCode.OK, hbOkRes.StatusCode);

        // v3: switch back to Open (creates a different snapshot)
        var put3 = await client.PutAsJsonAsync($"/api/v1/children/{id}/policy", new { mode = "Open", updatedBy = "test" });
        Assert.Equal(HttpStatusCode.OK, put3.StatusCode);
        using var put3Doc = JsonDocument.Parse(await put3.Content.ReadAsStringAsync());
        var v3 = put3Doc.RootElement.GetProperty("data").GetProperty("version").GetProperty("value").GetInt64();
        Assert.True(v3 > v2);

        // Heartbeat reports failure while still applied at v2.
        var hbFail = new
        {
            deviceName = "RDev",
            agentVersion = "0.0.0-test",
            sentAtUtc = DateTimeOffset.UtcNow,
            lastAppliedPolicyVersion = v2,
            lastPolicyApplyFailedAtUtc = DateTimeOffset.UtcNow,
            lastPolicyApplyError = "unit_test_failure"
        };
        var hbFailRes = await client.PostAsJsonAsync($"/api/v1/children/{id}/heartbeat", hbFail);
        Assert.Equal(HttpStatusCode.OK, hbFailRes.StatusCode);

        var st = await client.GetAsync($"/api/v1/children/{id}/status");
        Assert.Equal(HttpStatusCode.OK, st.StatusCode);
        using var stDoc = JsonDocument.Parse(await st.Content.ReadAsStringAsync());
        var rec = stDoc.RootElement.GetProperty("data").GetProperty("recommendedRollbackPolicyVersion");
        Assert.Equal(v2, rec.GetInt64());

        // Trigger rollback.
        var rb = await client.PostAsJsonAsync($"/api/v1/children/{id}/policy/rollback-last-known-good", new { updatedBy = "test" });
        Assert.Equal(HttpStatusCode.OK, rb.StatusCode);
        using var rbDoc = JsonDocument.Parse(await rb.Content.ReadAsStringAsync());
        var rbMode = rbDoc.RootElement.GetProperty("data").GetProperty("mode").GetString();
        var rbVer = rbDoc.RootElement.GetProperty("data").GetProperty("version").GetProperty("value").GetInt64();

        // The snapshot was Lockdown (v2) and rollback bumps the version beyond v3.
        Assert.Equal("Lockdown", rbMode);
        Assert.True(rbVer > v3);
    }
