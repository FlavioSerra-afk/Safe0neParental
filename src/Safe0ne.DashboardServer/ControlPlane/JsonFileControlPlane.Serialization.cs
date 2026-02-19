using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    private void LoadOrSeed()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                SeedUnsafe_NoLock();
                PersistUnsafe_NoLock();
                return;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var state = JsonSerializer.Deserialize<ControlPlaneState>(json, JsonDefaults.Options);

                if (state?.Children is null || state.Policies is null)
                {
                    SeedUnsafe_NoLock();
                    PersistUnsafe_NoLock();
                    return;
                }

                _children = state.Children;
                _archivedAtByChildGuid = (state.ArchivedChildren ?? new List<ArchivedChildState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.ArchivedAtUtc, StringComparer.OrdinalIgnoreCase);

                _localChildMetaJsonByChildGuid = (state.LocalChildMeta ?? new List<LocalChildMetaState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.MetaJson, StringComparer.OrdinalIgnoreCase);

                _localSettingsProfileJsonByChildGuid = (state.LocalSettingsProfiles ?? new List<LocalSettingsProfileState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.ProfileJson, StringComparer.OrdinalIgnoreCase);

                _localActivityEventsJsonByChildGuid = (state.LocalActivityEvents ?? new List<LocalActivityEventsState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.EventsJson, StringComparer.OrdinalIgnoreCase);

                _localAuditEntriesJsonByChildGuid = (state.LocalAuditEntries ?? new List<LocalAuditEntriesState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.EntriesJson, StringComparer.OrdinalIgnoreCase);

                _localLastLocationJsonByChildGuid = (state.LocalLocations ?? new List<LocalLocationState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.LocationJson, StringComparer.OrdinalIgnoreCase);


                _policiesByChildGuid = state.Policies
                    .ToDictionary(p => p.ChildId.Value.ToString(), p => p, StringComparer.OrdinalIgnoreCase);

                _statusByChildGuid = (state.Statuses ?? new List<ChildAgentStatus>())
                    .ToDictionary(s => s.ChildId.Value.ToString(), s => s, StringComparer.OrdinalIgnoreCase);

                _devicesByChildGuid = (state.Devices ?? new List<ChildDevicesState>())
                    .ToDictionary(
                        d => d.ChildId.Value.ToString(),
                        d => d.Devices,
                         StringComparer.OrdinalIgnoreCase);

                NormalizePairedDevicesUnsafe_NoLock();

                _pendingPairingByChildGuid = (state.PendingPairings ?? new List<PendingPairing>())
                    .Where(p => p.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    .ToDictionary(p => p.ChildId.Value.ToString(), p => p, StringComparer.OrdinalIgnoreCase);

                _commandsByChildGuid = (state.Commands ?? new List<ChildCommandsState>())
                    .ToDictionary(
                        c => c.ChildId.Value.ToString(),
                        c => c.Commands,
                        StringComparer.OrdinalIgnoreCase);

                _requests = state.Requests ?? new List<AccessRequest>();

                // Drop expired grants at load; they should never be active after restart.
                var now = DateTimeOffset.UtcNow;
                _grants = (state.Grants ?? new List<Grant>())
                    .Where(g => g.ExpiresAtUtc > now.AddDays(-1))
                    .ToList();

                _diagnosticsByChildGuid = (state.DiagnosticsBundles ?? new List<DiagnosticsBundleInfo>())
                    .ToDictionary(d => d.ChildId.Value.ToString(), d => d, StringComparer.OrdinalIgnoreCase);


                var loadedSchema = 0;
                try { loadedSchema = state.SchemaVersion; } catch { loadedSchema = 0; }
                if (loadedSchema <= 0) loadedSchema = 1;

                // Lazy upgrade: keep reading older snapshots but re-persist in the latest schema.
                if (loadedSchema < CurrentSchemaVersion)
                {
                    PersistUnsafe_NoLock();
                }
            }
            catch
            {
                // Corrupt/partial file: fall back to seed.
                SeedUnsafe_NoLock();
                PersistUnsafe_NoLock();
            }
        }
    }

    private void SeedUnsafe_NoLock()
    {
        // Deterministic seed so UI and integration tests can target a known child.
        var childId = new ChildId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        _children = new List<ChildProfile>
        {
            new(childId, "Demo Child")
        };

        _policiesByChildGuid = new Dictionary<string, ChildPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            [childId.Value.ToString()] = new ChildPolicy(
                ChildId: childId,
                Version: PolicyVersion.Initial,
                Mode: SafetyMode.Open,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedBy: "system",
                GrantUntilUtc: null,
                AlwaysAllowed: false)
        };

        _statusByChildGuid = new Dictionary<string, ChildAgentStatus>(StringComparer.OrdinalIgnoreCase);
        _devicesByChildGuid = new Dictionary<string, List<PairedDevice>>(StringComparer.OrdinalIgnoreCase);
        _pendingPairingByChildGuid = new Dictionary<string, PendingPairing>(StringComparer.OrdinalIgnoreCase);
        _commandsByChildGuid = new Dictionary<string, List<ChildCommand>>(StringComparer.OrdinalIgnoreCase);
        _diagnosticsByChildGuid = new Dictionary<string, DiagnosticsBundleInfo>(StringComparer.OrdinalIgnoreCase);

        _localLastLocationJsonByChildGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _localAuditEntriesJsonByChildGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _requests = new List<AccessRequest>();
        _grants = new List<Grant>();
    }

    private void PersistUnsafe_NoLock()
    {
        var devices = _devicesByChildGuid
            .Select(kvp => new ChildDevicesState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value))
            .ToList();

        var commands = _commandsByChildGuid
            .Select(kvp => new ChildCommandsState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value))
            .ToList();

        var state = new ControlPlaneState(
            Children: _children,
            Policies: _policiesByChildGuid.Values.ToList(),
            Statuses: _statusByChildGuid.Values.ToList(),
            Devices: devices,
            PendingPairings: _pendingPairingByChildGuid.Values.ToList(),
            Commands: commands,
            Requests: _requests,
            Grants: _grants,
            DiagnosticsBundles: _diagnosticsByChildGuid.Values.ToList(),
            ArchivedChildren: _archivedAtByChildGuid.Select(kvp => new ArchivedChildState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalChildMeta: _localChildMetaJsonByChildGuid.Select(kvp => new LocalChildMetaState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalSettingsProfiles: _localSettingsProfileJsonByChildGuid.Select(kvp => new LocalSettingsProfileState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalActivityEvents: _localActivityEventsJsonByChildGuid.Select(kvp => new LocalActivityEventsState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalAuditEntries: _localAuditEntriesJsonByChildGuid.Select(kvp => new LocalAuditEntriesState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalLocations: _localLastLocationJsonByChildGuid.Select(kvp => new LocalLocationState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            SchemaVersion: CurrentSchemaVersion
        );

        var json = JsonSerializer.Serialize(state, JsonDefaults.Options);

        // Safer replace: write temp then atomically swap into place on Windows.
        // If the destination doesn't exist yet, fall back to move.
        // See File.Replace docs.
        var tmp = _path + ".tmp";
        var bak = _path + ".bak";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, bak, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, _path);
        }
        if (File.Exists(tmp)) File.Delete(tmp);
        // Best-effort: don't accumulate backups forever.
        if (File.Exists(bak))
        {
            try { File.Delete(bak); } catch { /* ignore */ }
        }
    }

    private void EnsureChildProfileUnsafe_NoLock(ChildId childId)
    {
        if (_children.All(c => c.Id.Value != childId.Value))
        {
            _children.Add(new ChildProfile(childId, "Unnamed Child"));
        }
    }

    private static string GenerateNumericCode(int digits)
    {
        // Cryptographically strong code.
        var bytes = RandomNumberGenerator.GetBytes(8);
        ulong val = BitConverter.ToUInt64(bytes, 0);
        var mod = (ulong)Math.Pow(10, digits);
        return (val % mod).ToString().PadLeft(digits, '0');
    }



    private sealed record PairedDevice(
        Guid DeviceId,
        string DeviceName,
        string AgentVersion,
        DateTimeOffset PairedAtUtc,
        string TokenHashSha256,
        DateTimeOffset? LastSeenUtc = null,
        DateTimeOffset? TokenIssuedAtUtc = null,
        DateTimeOffset? TokenExpiresAtUtc = null,
        DateTimeOffset? TokenRevokedAtUtc = null,
        string? TokenRevokedBy = null,
        string? TokenRevokedReason = null);

    private sealed record PendingPairing(
        ChildId ChildId,
        string PairingCode,
        DateTimeOffset ExpiresAtUtc);

    private sealed record ChildDevicesState(
        ChildId ChildId,
        List<PairedDevice> Devices);

    // Local mode: child archive state (soft-delete), metadata, and UI settings profile blobs.
    // These are persisted as part of ControlPlaneState to survive restarts.
    private sealed record ArchivedChildState(
        ChildId ChildId,
        DateTimeOffset ArchivedAtUtc);

    private sealed record LocalChildMetaState(
        ChildId ChildId,
        string MetaJson);

    private sealed record LocalSettingsProfileState(
        ChildId ChildId,
        string ProfileJson);

    private sealed record LocalActivityEventsState(
        ChildId ChildId,
        string EventsJson);

    private sealed record LocalAuditEntriesState(
        ChildId ChildId,
        string EntriesJson);

    private sealed record ChildCommandsState(
        ChildId ChildId,
        List<ChildCommand> Commands);

    private sealed record ControlPlaneState(
        List<ChildProfile> Children,
        List<ChildPolicy> Policies,
        List<ChildAgentStatus>? Statuses = null,
        List<ChildDevicesState>? Devices = null,
        List<PendingPairing>? PendingPairings = null,
        List<ChildCommandsState>? Commands = null,
        // K8/P11: requests and time-boxed grants.
        List<AccessRequest>? Requests = null,
        List<Grant>? Grants = null,
        // K9: latest diagnostics bundle per child
        List<DiagnosticsBundleInfo>? DiagnosticsBundles = null,
        // Local mode: archive/restore without deleting
        List<ArchivedChildState>? ArchivedChildren = null,
        List<LocalChildMetaState>? LocalChildMeta = null,
        List<LocalSettingsProfileState>? LocalSettingsProfiles = null,
        List<LocalActivityEventsState>? LocalActivityEvents = null,
        List<LocalAuditEntriesState>? LocalAuditEntries = null,
        List<LocalLocationState>? LocalLocations = null,
        int SchemaVersion = CurrentSchemaVersion);

    private sealed record LocalLocationState(
        ChildId ChildId,
        string LocationJson);
}
