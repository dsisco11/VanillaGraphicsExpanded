# Surface Cache reset diagnostics

Reproduce the black-to-yellow restart while stationary, then inspect the active data directory's `Logs/client-main.log` for these `[VGE]` entries. This checkout's configured launch uses `D:/CODE/VintageStory/VintagestoryAutomation/Data`, rather than the default AppData directory.

- `Surface cache reset`: old/new scene IDs, invalidation revisions and atlas IDs, followed by aggregated source and geometry events since the previous history reset.
- `Surface cache recapture`: matching scene/revision, capture-history revision, resident page count and previously captured pages discarded.
- `Surface cache relight reset`: matching scene/revision/history, atlas/settings changes and discarded published/fully seeded page counts. Published pages may contain only partial valid lighting.
- `Trace geometry recreate` and `Trace geometry release`: resource lifecycle changes, including resolution/material generation and outstanding events from a departing scene.

Event categories distinguish engine chunk notifications (`loaded`, `created`, `dirty`), first observations, unavailable/replaced chunk instances, stale cell source dependencies, demanded-cell invalidation, republication, and window/residency departures. Counts are event counts, not unique cells. Each category retains its latest coordinate; chunk coordinates use 32-block units and cell coordinates use 16-block units. Source events can occur outside resident demand, so their presence alone does not prove they caused the reset. Correlate stale/departing cells and revision changes.

The collector has fixed call-site categories, retains no growing event list, and synchronizes game callbacks with render-thread draining. Logs are emitted on resets or resource lifecycle changes, not every frame or notification. This instrumentation does not change invalidation or lighting behavior.

## Captured evidence and removal

The 2026-09-24 run recorded resets at 21:34:22 and 21:34:36. At 21:34:36, one `chunk-event-dirty` notification for source chunk `(16000,0,16000)` coincided with eight stale source cells and eight demanded-cell invalidations. Scene ID remained 1, atlas ID remained 498, invalidation revision advanced from 8 to 16, and settings were unchanged. The reset queued all 216 resident pages for recapture and discarded 71 published lighting pages, including 61 fully seeded pages. The preceding reset discarded the same page counts. This establishes the dirty-source-to-global-reset path; it does not establish which engine activity caused the notification or whether relevant surface contents changed.

Before those resets, 145 of the 216 resident pages remained capture-pending while indirect failures continued accumulating. That backlog needs separate investigation from the global-reset policy.

The temporary collector, diagnostic scene IDs, and correlated reset/recreate/release messages are not needed by the rendering algorithm and can be removed now that this evidence is preserved. Keep existing readiness counters/self-checks and failure warnings. The refresh work and diagnostic cleanup are tracked in `docs/LumOn.WorldProbeSurfaceLighting.todo`; retaining these logs until implementation is optional, not a correctness requirement.
