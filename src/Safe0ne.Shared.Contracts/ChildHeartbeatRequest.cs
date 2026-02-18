// This file intentionally contains no type declarations.
//
// Historical note:
// - Older callers and older persisted payloads used the DTO name `ChildHeartbeatRequest`.
// - The back-compat alias for that legacy name lives in:
//     src/Safe0ne.Shared.Contracts/LegacyAliases.cs
//
// Canonical contract (use this everywhere new):
// - `ChildAgentHeartbeatRequest` in:
//     src/Safe0ne.Shared.Contracts/ApiContracts.cs
//
// Keep this file as a guard against accidental re-introduction of duplicate types.
