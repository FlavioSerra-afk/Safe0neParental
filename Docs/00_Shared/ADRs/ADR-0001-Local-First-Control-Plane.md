# ADR-0001: Local-first Control Plane (Windows prototype)

Status: Proposed
Date: 2026-02-02

## Context
We want working prototypes with minimal cost and maximum iteration speed.

## Decision
Use a **local-first control plane** on Windows:
- Local service hosts APIs/IPC
- Local storage holds family/child/device/policy data
- Parent App talks to the service
- Child Agent syncs policy versions from the service

## Consequences
- Fast iteration and offline-friendly
- Cloud migration later must respect additive-only contracts
