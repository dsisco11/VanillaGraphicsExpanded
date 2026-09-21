# WorldPartition residency and scene work invariants

`PartitionCoordinator` is the only owner of scene residency. Consumers retain content queues and GPU storage, and expose observations of coordinator state.

- Sources select world-zero, half-open cells. Required cells target Active; explicitly loaded bounds and prefetch target Loaded. Feedback heat adds a required source within the loaded scene envelope.
- Loaded acknowledges a complete resident representation. Near-scene slot residency does not imply that every material page has valid lighting. Page capture and relight flags remain authoritative for lighting.
- Active enables domain participation. Capture and relight queues require both desired and acknowledged Active residency, a current slot assignment, and an expired domain cooldown.
- Tracing work obtains an immutable coordinator request before dispatch. Registration generation, incarnation, revision and request identity are checked before the GPU upload callback runs. Source versions are checked before and after upload.
- Cancelled workers retain their shared in-flight credit until they acknowledge completion. Late results cannot recreate cells. Dirty notifications invalidate publication authorization immediately on the owning thread.
- Domain-selected updates share residency, capture, dispatch and upload limits with automatically scheduled providers. A completed result blocked by the current upload budget remains queued; an impossible payload reports a byte shortfall until invalidated or reconfigured.
- Only the render/owning thread changes residency. Worker acknowledgements and game dirty notifications cross concurrent queues.
- The slot ring and occupancy textures remain scene backend responsibilities. Their existing movement clearing and page sampling rules are preserved. Coordinator readiness is separate from page lighting validity and from the occupancy consumer's existing content-refresh behavior.
- Reset unregisters a consumer before a replacement world or configuration can publish. Packed work keys are local to a consumer; shared identities include registration instance and world scope.

The former cell-driven state machine, window hysteresis helper, global kind-keyed registry and scene transition queue have been removed. Coverage, retention and acknowledged state changes now use the shared coordinator.