using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// DTO returned by policy endpoints (Local Mode and v1 alias). Policy payload is represented
/// as JsonElement to preserve flexibility during Phase A/B while remaining contracts-stable.
/// </summary>
public sealed record PolicyEnvelopeDto(
    [property: JsonPropertyName("childId")] Guid ChildId,
    [property: JsonPropertyName("policyVersion")] long PolicyVersion,
    [property: JsonPropertyName("effectiveAtUtc")] DateTimeOffset? EffectiveAtUtc,
    [property: JsonPropertyName("policy")] JsonElement Policy);

/// <summary>
/// DTO returned by effective policy endpoint. For now EffectivePolicy may equal Policy, but
/// the shape is explicit for future server-side derivation (routines, schedules, defaults).
/// </summary>
public sealed record EffectivePolicyResponseDto(
    [property: JsonPropertyName("childId")] Guid ChildId,
    [property: JsonPropertyName("policyVersion")] long PolicyVersion,
    [property: JsonPropertyName("effectiveAtUtc")] DateTimeOffset? EffectiveAtUtc,
    [property: JsonPropertyName("policy")] JsonElement Policy,
    [property: JsonPropertyName("effectivePolicy")] JsonElement EffectivePolicy);
