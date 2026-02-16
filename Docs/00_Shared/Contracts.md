# Contracts (v1) — Safe0ne Parental

Updated: 2026-02-02

Purpose: define the first stable “surface area” between Parent App ↔ Control Plane ↔ Child Agent.

## Contract rules
- Additive-only once used in prototypes.
- Add fields; do not remove/rename existing fields.
- Control Plane is the source of truth for policy versions and decisions.

## Entities (logical)
Family, ParentAccount, ChildProfile, Device, PolicySet(versioned), Grant, Request, Alert, ReportAggregate, AgentHeartbeat

## P1 implementation (initial DTOs)
Implemented in `src/Safe0ne.Shared.Contracts`:
- `ChildId` (Guid wrapper)
- `PolicyVersion` (monotonic long)
- `SafetyMode` (Open/Homework/Bedtime/Lockdown)
- `ChildProfile` (Id, DisplayName)
- `ChildPolicy` (ChildId, Version, Mode, UpdatedAtUtc, UpdatedBy)
- `ApiResponse<T>`, `ApiError`
- `UpdateChildPolicyRequest` (Mode, UpdatedBy)
