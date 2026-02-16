using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Pairing;

/// <summary>
/// Kid-side pairing helper. Uses a one-time pairing code to enroll and receive a device token.
/// Persists token via <see cref="AgentAuthStore"/>.
/// </summary>
public sealed class EnrollmentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public EnrollmentService(IHttpClientFactory httpClientFactory, ILogger<EnrollmentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(bool Ok, string Message, PairingCompleteResponse? Data)> EnrollByCodeAsync(string pairingCode, string? deviceName, string? agentVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pairingCode))
            return (false, "Pairing code is required.", null);

        try
        {
            var client = _httpClientFactory.CreateClient("ControlPlane");
            var req = new PairingCompleteRequest(pairingCode.Trim(), deviceName ?? Environment.MachineName, agentVersion ?? "ChildAgent");

            using var resp = await client.PostAsJsonAsync("/api/local/devices/enroll", req, JsonDefaults.Options, ct);
            var parsed = await resp.Content.ReadFromJsonAsync<ApiResponse<PairingCompleteResponse>>(JsonDefaults.Options, ct);

            if (parsed?.Ok != true || parsed.Data is null)
            {
                return (false, parsed?.Error?.Message ?? $"HTTP {(int)resp.StatusCode}", null);
            }

            var auth = new AgentAuthStore.AgentAuthState(parsed.Data.DeviceId, parsed.Data.DeviceToken, parsed.Data.IssuedAtUtc);
            AgentAuthStore.SaveAuth(parsed.Data.ChildId, auth);
            AgentAuthStore.SaveCurrentChildId(parsed.Data.ChildId);

            _logger.LogInformation("Paired successfully. ChildId={ChildId} DeviceId={DeviceId}", parsed.Data.ChildId.Value, parsed.Data.DeviceId);
            return (true, "Paired successfully.", parsed.Data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnrollByCode error");
            return (false, "Pairing failed due to a network or server error.", null);
        }
    }
}
