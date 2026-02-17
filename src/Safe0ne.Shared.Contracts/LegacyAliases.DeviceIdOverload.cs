/*
 * Intentionally left empty.
 *
 * Historical note:
 * - Earlier patches introduced a partial type here to add a DeviceId overload.
 * - ChildHeartbeatRequest now includes DeviceId directly in LegacyAliases.cs, so the extra
 *   partial declaration/constructor would cause duplicate member/partial errors.
 */
namespace Safe0ne.Shared.Contracts;
