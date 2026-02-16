# Architecture (Overall) — Safe0ne Parental

Updated: 2026-02-04

## Components
- **Parent App** (Windows WebView2 first): configuration UI, approvals, reporting
- **Child App / Agent** (Windows first): enforcement + child UX
- **Control Plane**
  - Prototype: local service + local storage
  - Later: cloud API + storage + push wake hints

## Data flow
- Parent configures policies → Control Plane stores a policy version
- Child Agent syncs policy by version (offline-first; cached last-known-good)
- Child submits requests → Control Plane → Parent decides
- Agent emits heartbeat + aggregates → Parent dashboard renders

## Decision records
Capture non-trivial decisions as ADRs in `/Docs/00_Shared/ADRs`.


UI modularization note: see ADR-0003.
