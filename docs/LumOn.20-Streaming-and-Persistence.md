# LumOn Streaming (No Disk Persistence)

> **Document**: LumOn.20-Streaming-and-Persistence.md  
> **Status**: Draft  
> **Dependencies**:
>
> - Phase 18 overview: [LumOn.16-World-Space-Clipmap-Probes.md](LumOn.16-World-Space-Clipmap-Probes.md)
> - Clipmap topology: [LumOn.17-Clipmap-Topology-and-Addressing.md](LumOn.17-Clipmap-Topology-and-Addressing.md)

---

## 1. Overview

This document defines how world-probe data streams as the clipmap origin shifts.

World-probe payloads are **in-memory only**: they may persist across frames, but are **never persisted to disk**.

---

## 2. CPU-side cache

The CPU tracks regions of probe data to support streaming:

- **Key**: `(level, regionCoord)` where `regionCoord` is snapped in world space
- **Value**: Probe payloads for that region (or a handle to GPU data)
- **Metadata**: lastUpdated, dirtyFlags, layoutVersion

Region size should align with the clipmap resolution to simplify reuse.

---

## 3. Eviction and reuse

When the clipmap origin shifts:

1. Identify regions that are now out of range.
2. Evict or recycle their slots.
3. Mark new regions as dirty.

Recommended policy:

- **Near levels**: always refresh on entry.
- **Far levels**: reuse until explicitly invalidated.

---

## 4. Determinism requirements

To keep results stable:

- Origin snapping must be deterministic for a given camera path.
- Probe update selection should be stable when budgets are equal.
- In-memory cache invalidation must be deterministic (layout version + topology changes).

---

## 5. Streaming diagram

```mermaid
flowchart TD
  Move["Camera moves"]
  Snap["Snap origins per level"]
  Shift["Detect region shift"]
  Evict["Evict or recycle regions"]
  Dirty["Mark new regions dirty"]
  Update["Schedule probe updates"]

  Move --> Snap --> Shift --> Evict --> Dirty --> Update
```

---

## 6. Decisions (locked)

- SH order: L1
- Trace source: iterative async voxel traces on the CPU
- Visibility: ShortRangeAO direction (oct-encoded) + confidence
- Persistence: none (world probes are RAM-only)
