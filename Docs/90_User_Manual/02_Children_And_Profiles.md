# Children and Profiles

## Children list/cards (implemented)
- The **Children** page displays child cards.
- Each card links to the child’s profile.

## Add Child modal flow (implemented)
- Use **Add Child** to create a profile.
- Avatar selection includes **crop tools**.

## Child profile tabs (implemented)
The child profile exposes multiple tabs for configuration and review (exact tab names may evolve with policy coverage).

### Expected behavior
- No UI regressions: cards, modal flow, avatar crop, and tabs must remain functional.


## Policy Versioning (Local Mode)

Safe0ne uses a **local-first policy envelope** (`/api/local/children/{childId}/policy`) with a monotonically increasing `policyVersion`.

- The Parent writes policy/profile updates into the **Local Control Plane (SSOT)**.
- The Kid Agent polls for policy changes.
- The Kid Agent keeps a **last-known-good cache** and will **ignore any policy response with a lower version** than what it already applied (replay guard).
- The Kid reports what it is enforcing back to the server via heartbeat (used for diagnostics + future UI).
