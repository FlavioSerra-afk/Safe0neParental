/*
 * Back-compat aliases for older tests/clients. Additive only.
 */
namespace Safe0ne.Shared.Contracts;

using System;
using System.Text.Json.Serialization;

// NOTE: This file is patched by PATCH_16W26K to ensure ChildHeartbeatRequest can be extended safely.
public sealed partial record ChildHeartbeatRequest(
    ChildId ChildId,
    Guid DeviceId,
    string? DeviceName = null,
    string? AgentVersion = null,
    DateTimeOffset? SentAtUtc = null,
    long? LastAppliedPolicyVersion = null,
    DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    string? LastPolicyApplyError = null
);
