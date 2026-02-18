# ADR-0004: Legacy Compat Deprecation Strategy

**Status:** Accepted  
**Date:** 2026-02-18  

## Context

As we migrate from earlier “local-first”/prototype endpoints and contract shapes toward the canonical ControlPlane API surface, we occasionally need **temporary compatibility shims** ("legacy compat") to keep the solution building and to avoid breaking in-flight vertical slices.

Without an explicit strategy, these shims tend to accumulate (duplicate helpers, overlapping overloads, inconsistent naming), which slows delivery and increases regression risk.

## Decision

We will:

1. **Allow legacy compat code** only when it unblocks compilation/tests or preserves an already-shipped slice while canonical APIs are introduced.
2. **Mark legacy compat code explicitly** with a standard header comment and a single-line tag:

   - `// LEGACY-COMPAT: <why> | remove when <condition>`

3. **Maintain a Legacy Code Registry** (single source of truth) listing:

   - file/class
   - why it exists
   - canonical replacement
   - removal condition
   - owner (optional)

4. Prefer **one canonical implementation** plus thin wrappers. Avoid "parallel implementations".

## Consequences

- Clear, auditable deprecation plan.
- Less "compat churn" because new patches must either:
  - extend the canonical path, or
  - update the registry and provide removal criteria.
- Slightly more up-front documentation work, but reduced long-term friction.

## Removal criteria examples

- ParentApp + ChildAgent are both migrated to new contract type `X`.
- Old endpoint `/api/legacy/...` is no longer referenced anywhere (search shows zero callers).
- A release branch has shipped with canonical endpoints for at least one full iteration.
