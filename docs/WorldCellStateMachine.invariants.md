# WorldCell State Machine (Phase 9) - Allowed Transitions & Invariants

This document captures the generic, reusable invariants for the Phase 9 `WorldCell` desired/actual state model.

## State Definitions

- `WorldCellDesiredState`
  - `Unloaded`: Cell is out of scope; safe to reclaim resources.
  - `Loaded`: Cell should retain residency/assignment, but should not receive update work.
  - `Active`: Cell should participate in update scheduling.

- `WorldCellActualState`
  - A coarse lifecycle state intended for correctness/debug visibility.
  - Domain-specific substates (e.g., capture/relight readiness) live alongside it.

## Allowed Transitions (Actual)

The generic scheduler should converge `ActualState` toward `DesiredState` without skipping required intermediate steps.

- `Unloaded → Loaded → Active` (activation path)
- `Active → Loaded → Unloaded` (hysteresis + unload path)

Direct `Active → Unloaded` may be allowed when the system needs to aggressively reclaim resources, but it must be treated as a cancellation path (see below).

## Invariants

- **Eligibility gating**
  - Only `DesiredState=Active` cells are eligible for update work queues.
  - `DesiredState=Loaded` implies no update work is issued, but residency may be retained.

- **No illegal skips**
  - If a system requires intermediate work (e.g., "NeedsCapture" before "Ready"), it must enforce that the intermediate work is scheduled/finished before declaring the cell fully ready.

- **Cancellation is explicit**
  - If `DesiredState` regresses (e.g., `Active → Loaded` or `Loaded → Unloaded`), in-flight work should be removed/canceled as appropriate.
  - Cancellation should be idempotent and safe to run multiple times.

- **Cooldown/backoff**
  - If transition work fails due to resource pressure (allocation failure, budget starvation, missing chunk), the cell should set `NextEligibleTick` and avoid immediately re-queuing the same work.
  - Cooldown should be bounded and should reset on success.

## Notes

Phase 9.1 (LumonScene-specific) implements these invariants with a dual-window policy and a per-cell cooldown/backoff mechanism.
