using System.Net;
using System.Net.Http.Json;
using System.Linq;
using System.Collections.Generic;
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
    public async Task LocalDevices_MultiDevice_Pairing_And_Unpair_Workflows_AreStable()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Multi Device Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        async Task<(string deviceId, string token)> PairOne(string name)
        {
            var start = await client.PostAsync($"/api/v1/children/{childId}/pair/start", content: null);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);

            using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
            var pairingCode = startDoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
            Assert.False(string.IsNullOrWhiteSpace(pairingCode));

            var complete = await client.PostAsJsonAsync($"/api/v1/children/{childId}/pair/complete", new { pairingCode, deviceName = name, agentVersion = "test" });
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

            using var completeDoc = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
            var deviceId = completeDoc.RootElement.GetProperty("data").GetProperty("deviceId").GetString();
            var token = completeDoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(deviceId));
            Assert.False(string.IsNullOrWhiteSpace(token));
            return (deviceId!, token!);
        }

        var d1 = await PairOne("Kid-Laptop");
        var d2 = await PairOne("Kid-Desktop");

        var devices0 = await client.GetAsync($"/api/local/children/{childId}/devices");
        Assert.Equal(HttpStatusCode.OK, devices0.StatusCode);

        using (var doc = JsonDocument.Parse(await devices0.Content.ReadAsStringAsync()))
        {
            var arr = doc.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.Array, arr.ValueKind);
            Assert.True(arr.GetArrayLength() >= 2);
        }

        // Unpair one device and ensure the other remains.
        var del = await client.DeleteAsync($"/api/local/devices/{d1.deviceId}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        var devices1 = await client.GetAsync($"/api/local/children/{childId}/devices");
        Assert.Equal(HttpStatusCode.OK, devices1.StatusCode);
        var json = await devices1.Content.ReadAsStringAsync();
        Assert.DoesNotContain(d1.deviceId, json);
        Assert.Contains(d2.deviceId, json);
    }

    [Fact]
    public async Task LocalDevices_LastSeen_IsTracked_PerDevice_WhenMultipleDevicesExist()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "LastSeen Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        async Task<(string deviceId, string token)> PairOne(string name)
        {
            var start = await client.PostAsync($"/api/v1/children/{childId}/pair/start", content: null);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);

            using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
            var pairingCode = startDoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
            Assert.False(string.IsNullOrWhiteSpace(pairingCode));

            var complete = await client.PostAsJsonAsync($"/api/v1/children/{childId}/pair/complete", new { pairingCode, deviceName = name, agentVersion = "test" });
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

            using var completeDoc = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
            var deviceId = completeDoc.RootElement.GetProperty("data").GetProperty("deviceId").GetString();
            var token = completeDoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(deviceId));
            Assert.False(string.IsNullOrWhiteSpace(token));
            return (deviceId!, token!);
        }

        var d1 = await PairOne("Kid-1");
        var d2 = await PairOne("Kid-2");

        // Heartbeat from device 1.
        using (var hbReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat"))
        {
            hbReq.Headers.Add("X-Safe0ne-Device-Token", d1.token);
            hbReq.Content = JsonContent.Create(new { deviceName = "Kid-1", agentVersion = "test", sentAtUtc = DateTimeOffset.UtcNow });
            var hbRes = await client.SendAsync(hbReq);
            Assert.Equal(HttpStatusCode.OK, hbRes.StatusCode);
        }

        var devices = await client.GetAsync($"/api/local/children/{childId}/devices");
        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);

        using (var doc = JsonDocument.Parse(await devices.Content.ReadAsStringAsync()))
        {
            var arr = doc.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.Array, arr.ValueKind);

            string? last1 = null;
            string? last2 = null;
            foreach (var el in arr.EnumerateArray())
            {
                var did = el.GetProperty("deviceId").GetString();
                var last = el.TryGetProperty("lastSeenUtc", out var ls) ? ls.GetString() : null;
                if (string.Equals(did, d1.deviceId, StringComparison.OrdinalIgnoreCase)) last1 = last;
                if (string.Equals(did, d2.deviceId, StringComparison.OrdinalIgnoreCase)) last2 = last;
            }

            Assert.False(string.IsNullOrWhiteSpace(last1));
            Assert.True(string.IsNullOrWhiteSpace(last2));
        }
    }

    [Fact]
    public async Task LocalActivity_Append_Query_Filter_And_Export_AreStable()
    {
        using var client = _factory.CreateClient();

        // Create child.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Activity Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        // Append a small batch.
        var batch = new object[]
        {
            new { eventId = Guid.NewGuid(), occurredAtUtc = t0, kind = "test_a", details = "a" },
            new { eventId = Guid.NewGuid(), occurredAtUtc = t1, kind = "test_b", details = "b" },
            new { eventId = Guid.NewGuid(), occurredAtUtc = t2, kind = "test_c", details = "c" },
        };

        var post = await client.PostAsJsonAsync($"/api/local/children/{childId}/activity", batch);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        // Query newest-first.
        var get = await client.GetAsync($"/api/local/children/{childId}/activity?take=10");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        using (var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.TryGetProperty("ok", out var okEl) ? okEl.GetBoolean() : true);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.Array, data.ValueKind);
            Assert.True(data.GetArrayLength() >= 3);

            // Newest first should be test_c
            Assert.Equal("test_c", data[0].GetProperty("kind").GetString());
        }

        // Filter from t1 (should include b and c)
        var get2 = await client.GetAsync($"/api/local/children/{childId}/activity?fromUtc={Uri.EscapeDataString(t1.ToString("O"))}&take=10");
        Assert.Equal(HttpStatusCode.OK, get2.StatusCode);
        using (var doc = JsonDocument.Parse(await get2.Content.ReadAsStringAsync()))
        {
            var data = doc.RootElement.GetProperty("data");
            var kinds = data.EnumerateArray().Select(x => x.GetProperty("kind").GetString()).ToArray();
            Assert.Contains("test_b", kinds);
            Assert.Contains("test_c", kinds);
            Assert.DoesNotContain("test_a", kinds);
        }

        // Export envelope should include events.
        var export = await client.GetAsync($"/api/local/children/{childId}/activity/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportJson = await export.Content.ReadAsStringAsync();
        Assert.Contains("events", exportJson);
        Assert.Contains("test_a", exportJson);
        Assert.Contains("test_b", exportJson);
        Assert.Contains("test_c", exportJson);
    }

    [Fact]
    public async Task LocalActivity_Retention_Caps_To_2000_Newest()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Retention Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        // Append 2050 events in chunks so request sizes remain reasonable.
        var baseUtc = DateTimeOffset.UtcNow.AddHours(-1);
        const int total = 2050;
        const int chunk = 250;
        for (var i = 0; i < total; i += chunk)
        {
            var list = new List<object>();
            for (var j = 0; j < chunk && (i + j) < total; j++)
            {
                var n = i + j;
                list.Add(new { eventId = Guid.NewGuid(), occurredAtUtc = baseUtc.AddSeconds(n), kind = "retention_test", seq = n });
            }

            var post = await client.PostAsJsonAsync($"/api/local/children/{childId}/activity", list);
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }

        var get = await client.GetAsync($"/api/local/children/{childId}/activity?take=2000");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2000, data.GetArrayLength());

        // Because newest-first, the highest seq should be first.
        var firstSeq = data[0].TryGetProperty("seq", out var seqEl) ? seqEl.GetInt32() : -1;
        Assert.True(firstSeq >= 2049);
    }

    [Fact]
    public async Task LocalAuditLog_ReturnsEntries_AfterLocalPolicyWrite()
    {
        using var client = _factory.CreateClient();

        // Create child.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Audit Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        // Write a local policy to ensure an audit entry is appended.
        var put = await client.PutAsJsonAsync($"/api/local/children/{childId}/policy", new { mode = "allow" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var audit = await client.GetAsync($"/api/local/children/{childId}/audit?take=50");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

        using var auditDoc = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
        var entries = auditDoc.RootElement.GetProperty("data").GetProperty("entries");
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
        Assert.True(entries.GetArrayLength() >= 1);
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
        Assert.Contains("\"events\"", expJson);
        Assert.Contains("unit_test", expJson);
    }


    [Fact]
    public async Task LocalPolicySync_Watchdog_SetsOverdue_WhenMismatchBeyondThreshold()
    {
        // For this test we force the watchdog threshold to 0 seconds so overdue becomes immediate.
        Environment.SetEnvironmentVariable("SAFE0NE_POLICY_WATCHDOG_SECONDS", "0");

        using var client = _factory.CreateClient();

        // Create a child in local mode.
        var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Watchdog Child" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(childId));

        // Fetch current policy envelope to obtain the current configured version and policy payload.
        var polGet = await client.GetAsync($"/api/local/children/{childId}/policy");
        Assert.Equal(HttpStatusCode.OK, polGet.StatusCode);

        using var polDoc = JsonDocument.Parse(await polGet.Content.ReadAsStringAsync());
        var currentVersion = polDoc.RootElement.GetProperty("data").GetProperty("policyVersion").GetInt64();
        var policyJson = polDoc.RootElement.GetProperty("data").GetProperty("policy").GetRawText();

        // Save policy again to bump the configured version.
        var putBody = JsonContent.Create(new { policy = JsonDocument.Parse(policyJson).RootElement });
        var polPut = await client.PutAsync($"/api/local/children/{childId}/policy", putBody);
        Assert.Equal(HttpStatusCode.OK, polPut.StatusCode);

        // Heartbeat reports it only applied the previous version.
        var hb = await client.PostAsJsonAsync($"/api/v1/children/{childId}/heartbeat", new
        {
            deviceName = "TestDevice",
            agentVersion = "test",
            sentAtUtc = DateTimeOffset.UtcNow,
            lastAppliedPolicyVersion = currentVersion
        });
        Assert.Equal(HttpStatusCode.OK, hb.StatusCode);

        using var hbDoc = JsonDocument.Parse(await hb.Content.ReadAsStringAsync());
        var data = hbDoc.RootElement.GetProperty("data");

        Assert.True(data.TryGetProperty("policyApplyOverdue", out var overdueProp));
        Assert.True(overdueProp.GetBoolean());

        Assert.True(data.TryGetProperty("policyApplyState", out var stateProp));
        Assert.Equal("overdue", stateProp.GetString());

        Assert.True(data.TryGetProperty("policyApplyPendingSinceUtc", out var pendingProp));
        Assert.Equal(JsonValueKind.String, pendingProp.ValueKind);

        // Cleanup env var to avoid bleeding into other tests.
        Environment.SetEnvironmentVariable("SAFE0NE_POLICY_WATCHDOG_SECONDS", null);
    }


[Fact]
public async Task LocalPairing_MultiDevice_RevokeIsolation_And_LastSeen_AreStable()
{
    using var client = _factory.CreateClient();

    // Create a child in local mode.
    var create = await client.PostAsJsonAsync("/api/local/children", new { name = "Multi Device Child" });
    Assert.Equal(HttpStatusCode.OK, create.StatusCode);

    using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
    var childId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
    Assert.False(string.IsNullOrWhiteSpace(childId));

    // Pair device A.
    var startA = await client.PostAsync($"/api/v1/children/{childId}/pair/start", content: null);
    Assert.Equal(HttpStatusCode.OK, startA.StatusCode);
    using var startADoc = JsonDocument.Parse(await startA.Content.ReadAsStringAsync());
    var codeA = startADoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
    Assert.False(string.IsNullOrWhiteSpace(codeA));

    var completeA = await client.PostAsJsonAsync($"/api/v1/children/{childId}/pair/complete", new
    {
        pairingCode = codeA,
        deviceName = "DeviceA",
        agentVersion = "test"
    });
    Assert.Equal(HttpStatusCode.OK, completeA.StatusCode);
    using var completeADoc = JsonDocument.Parse(await completeA.Content.ReadAsStringAsync());
    var deviceIdA = completeADoc.RootElement.GetProperty("data").GetProperty("deviceId").GetString();
    var tokenA = completeADoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
    Assert.False(string.IsNullOrWhiteSpace(deviceIdA));
    Assert.False(string.IsNullOrWhiteSpace(tokenA));

    // Pair device B.
    var startB = await client.PostAsync($"/api/v1/children/{childId}/pair/start", content: null);
    Assert.Equal(HttpStatusCode.OK, startB.StatusCode);
    using var startBDoc = JsonDocument.Parse(await startB.Content.ReadAsStringAsync());
    var codeB = startBDoc.RootElement.GetProperty("data").GetProperty("pairingCode").GetString();
    Assert.False(string.IsNullOrWhiteSpace(codeB));

    var completeB = await client.PostAsJsonAsync($"/api/v1/children/{childId}/pair/complete", new
    {
        pairingCode = codeB,
        deviceName = "DeviceB",
        agentVersion = "test"
    });
    Assert.Equal(HttpStatusCode.OK, completeB.StatusCode);
    using var completeBDoc = JsonDocument.Parse(await completeB.Content.ReadAsStringAsync());
    var deviceIdB = completeBDoc.RootElement.GetProperty("data").GetProperty("deviceId").GetString();
    var tokenB = completeBDoc.RootElement.GetProperty("data").GetProperty("deviceToken").GetString();
    Assert.False(string.IsNullOrWhiteSpace(deviceIdB));
    Assert.False(string.IsNullOrWhiteSpace(tokenB));

    // Heartbeat from both devices (auth required once paired).
    using var hbAReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat");
    hbAReq.Headers.Add("X-Safe0ne-Device-Token", tokenA);
    hbAReq.Content = JsonContent.Create(new
    {
        deviceName = "DeviceA",
        agentVersion = "test",
        sentAtUtc = DateTimeOffset.UtcNow
    });
    var hbA = await client.SendAsync(hbAReq);
    Assert.Equal(HttpStatusCode.OK, hbA.StatusCode);

    using var hbBReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat");
    hbBReq.Headers.Add("X-Safe0ne-Device-Token", tokenB);
    hbBReq.Content = JsonContent.Create(new
    {
        deviceName = "DeviceB",
        agentVersion = "test",
        sentAtUtc = DateTimeOffset.UtcNow
    });
    var hbB = await client.SendAsync(hbBReq);
    Assert.Equal(HttpStatusCode.OK, hbB.StatusCode);

    // Device list should contain both devices and lastSeen populated per-device.
    var devicesBefore = await client.GetAsync($"/api/local/children/{childId}/devices");
    Assert.Equal(HttpStatusCode.OK, devicesBefore.StatusCode);
    using var devicesBeforeDoc = JsonDocument.Parse(await devicesBefore.Content.ReadAsStringAsync());
    var arrBefore = devicesBeforeDoc.RootElement.GetProperty("data").EnumerateArray().ToList();
    Assert.True(arrBefore.Count >= 2);

    var aBefore = arrBefore.Single(x => x.GetProperty("deviceId").GetString() == deviceIdA);
    var bBefore = arrBefore.Single(x => x.GetProperty("deviceId").GetString() == deviceIdB);
    Assert.True(aBefore.TryGetProperty("lastSeenUtc", out var aSeen) && aSeen.ValueKind != JsonValueKind.Null);
    Assert.True(bBefore.TryGetProperty("lastSeenUtc", out var bSeen) && bSeen.ValueKind != JsonValueKind.Null);

    // Revoke device A token only.
    var revokeA = await client.PostAsJsonAsync($"/api/local/devices/{deviceIdA}/revoke", new { revokedBy = "test", reason = "unit" });
    Assert.Equal(HttpStatusCode.OK, revokeA.StatusCode);

    // Heartbeat with revoked token should be unauthorized.
    using var hbA2Req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat");
    hbA2Req.Headers.Add("X-Safe0ne-Device-Token", tokenA);
    hbA2Req.Content = JsonContent.Create(new
    {
        deviceName = "DeviceA",
        agentVersion = "test",
        sentAtUtc = DateTimeOffset.UtcNow
    });
    var hbA2 = await client.SendAsync(hbA2Req);
    Assert.Equal(HttpStatusCode.Unauthorized, hbA2.StatusCode);

    // Device B must still be authorized.
    using var hbB2Req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/children/{childId}/heartbeat");
    hbB2Req.Headers.Add("X-Safe0ne-Device-Token", tokenB);
    hbB2Req.Content = JsonContent.Create(new
    {
        deviceName = "DeviceB",
        agentVersion = "test",
        sentAtUtc = DateTimeOffset.UtcNow
    });
    var hbB2 = await client.SendAsync(hbB2Req);
    Assert.Equal(HttpStatusCode.OK, hbB2.StatusCode);

    // Device record for A remains but is revoked.
    var devicesAfter = await client.GetAsync($"/api/local/children/{childId}/devices");
    Assert.Equal(HttpStatusCode.OK, devicesAfter.StatusCode);
    using var devicesAfterDoc = JsonDocument.Parse(await devicesAfter.Content.ReadAsStringAsync());
    var arrAfter = devicesAfterDoc.RootElement.GetProperty("data").EnumerateArray().ToList();

    var aAfter = arrAfter.Single(x => x.GetProperty("deviceId").GetString() == deviceIdA);
    Assert.True(aAfter.TryGetProperty("tokenRevoked", out var revoked) && revoked.ValueKind == JsonValueKind.True);

    // Device B still present and not revoked.
    var bAfter = arrAfter.Single(x => x.GetProperty("deviceId").GetString() == deviceIdB);
    Assert.True(bAfter.TryGetProperty("tokenRevoked", out var bRev) && bRev.ValueKind == JsonValueKind.False);
}
}
