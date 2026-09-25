# Bounded shared geometry coverage

This implements the geometry-coverage task in [the throughput checklist](LumOn.WorldProbeSurfaceLighting.todo).
The shared, full-resolution geometry domain now supports 16, 32, 64, 128, 192 and 256 blocks per axis.
New configurations default to 192. The fixed 48-block NearField domain, voxel formats, publication
identity and shader resource contract remain unchanged.

## Storage choice

The recorded configuration requested Surface Cache pages across roughly 480 blocks while geometry
covered only 128. Page residency is still useful beyond geometry coverage, but cannot establish that
source geometry is available. The previous capture-admission task defers those pages explicitly.

The backend stores three four-byte voxel companions plus one readiness byte per 16-block cell and
2,097,796 bytes of shared lookup textures. A moving logical domain needs one extra alignment cell:
physical width is `max(48, logical width + 16)`. The following are payload ceilings, excluding driver
allocations, managed object overhead and the rest of the renderer:

| Logical width | Physical width | Texture bytes | Maximum retained capture-identity bytes | Physical cells |
| --- | --- | --- | --- | --- |
| 128 | 144 | 37,930,333 | 11,943,936 | 729 |
| 192 (new default) | 208 | 110,086,937 | 35,995,648 | 2,197 |
| 256 (maximum supported) | 272 | 243,586,485 | 80,494,592 | 4,913 |
| 480 (rejected dense option) | 496 | 1,466,414,819 | 488,095,744 | 29,791 |

The 192 setting extends centered coverage from 64 to 96 blocks per axis, providing 3.375 times the
logical volume at about 105 MiB of texture payload. The optional 256 setting extends it to 128 blocks
at about 232 MiB. Existing saved `ClipmapResolution: 128` values remain 128; choose 192 explicitly to
apply the larger domain to an existing configuration. Requests between supported tiers round upward,
with a hard 256-block ceiling. Direct renderer use is normalized too, before allocation.

Matching the whole 480-block residency envelope with dense storage would cost about 1.37 GiB of
textures and exceed the coordinator's 16,384-cell shared residency ceiling. It is deliberately not
supported. Coarser coverage levels would not provide the exact block faces required for material
capture. Sparse storage could extend exact coverage further, but requires a different address and
publication contract across all consumers. This change keeps the established full-resolution ring
and leaves outer-domain continuation to the separate fallback task.

## Work settings and ownership

These serialized settings live under `LumOn.LumonScene.TraceScene`:

| Setting | Default | Supported range / behavior |
| --- | --- | --- |
| `ClipmapResolution` | 192 | 16, 32, 64, 128, 192, 256 blocks |
| `SourceChunksPerFrame` | 2 | 0..8 new source snapshots; zero still allows cached publication |
| `CellUploadsPerFrame` | 16 | 0..32 complete 16-block cells; zero pauses service |
| `UploadBytesPerFrame` | 8,388,608 | Zero pauses; nonzero clamps to 65,536..8,388,608 bytes |

`TraceGeometryWorkBudget` detaches and validates the frame's settings. The partition applies the byte
limit to table staging and cell publication together, counting both readiness writes for publication.
It also obeys the existing per-registration and shared coordinator limits. Table uploads retain their
offset across frames; even the minimum byte setting can finish tables and a complete cell without
publishing partial inputs. Submitted bytes remain charged if a backend rejects publication.

Pausing service or changing work budgets does not recreate GPU resources or discard valid geometry,
pending payloads or publication leases. Dirty/streaming invalidations still withdraw stale readiness
immediately while paused; those small safety writes and initial allocation clears are outside the
publication upload budget. Changing coverage size creates a new resource generation as before.

Default per-frame work limits are unchanged. Source snapshots remain bounded to eight retained
entries and eight in-flight tasks, including cancelled tasks until completion. Retained source payload
is at most 3 MiB. Pending cell payload remains under the existing 64-lease bound (3 MiB). Published
capture identity grows only with occupied physical slots, as shown above. The renderer reports
effective logical `SurfaceResolution` and `WorkBudget` alongside measured residency and byte counts
in the existing shared-geometry diagnostics.

Source and cell queues order eligible work by distance from the logical domain center, preserving
the existing service opportunity for surface-only work alongside NearField priority. With only one
source admission or one feasible cell upload, preferred consumers alternate. Completed cells leave
the queue, so stationary finite demand progresses outward. Missing sources retain bounded retries;
they cannot masquerade as initialized air.

These are count and byte ceilings, not a render-thread time guarantee. Coverage enumeration and
dependency checks remain bounded by the chosen domain size. A larger domain takes more total work
to initialize at unchanged budgets; nearby ordering makes the first completed work useful sooner.

## Consumer validity and remaining work

Signed integer origins, world-height clipping, owner-specific ring retirement and readiness-last
publication continue through the same geometry owner. Surface Cache capture and optional L0 GPU
world-probe tracing see the expanded surface domain. Screen tracing and world-probe visibility keep
their independent 48-block NearField domain. No base-game camera matrix supplies world origins.

Coverage exits, unsupported geometry and traversal exhaustion remain unresolved outcomes. Larger
storage does not establish sky visibility or lift existing ray-step limits. The fallback, traversal
budget and lighting-update budget tasks remain open. No gameplay convergence or frame-time gain is
claimed from this change; those require the later user-run acceptance check.

## Controlled cost evidence

`SharedGeometryCostMeasurementsTests.StartupMovementAndEdit` ran the production partition and GPU
publication on an RTX 4090 with deterministic source completions, unchanged work budgets and
cell-aligned camera positions. Receipt: `artifacts/TestResults/expanded-geometry-focused-final.trx`.
The measured GPU texture payloads match the storage table above. Logical demand at these aligned
positions is smaller than the worst-case physical slot capacity.

| Width | Startup ready cells | Startup source reads | Startup frames | Startup uploaded bytes | 16-block move source reads / frames |
| --- | --- | --- | --- | --- | --- |
| 128 | 512 | 64 | 33 | 27,265,024 | 16 / 9 |
| 192 | 1,728 | 216 | 109 | 87,036,288 | 36 / 19 |
| 256 | 4,096 | 512 | 257 | 203,432,960 | 64 / 33 |

All three sizes retained at most 3,145,728 source-snapshot bytes and captured no duplicate source
revision. A one-chunk edit required one source read, two service frames and 393,240 upload bytes at
each size. All per-frame source, snapshot, staging and upload ceilings held.

Startup render-thread submission totals were approximately 98 / 448 / 2,174 ms, with cumulative
thread allocations of 88 / 324 / 1,018 MB for 128 / 192 / 256 respectively. These are single-run,
unequal-volume synthetic workloads, not isolated GPU execution, retained-memory peaks or gameplay
timings. They show the extra total work and allocation cost of scanning and filling larger domains;
they do not demonstrate a speedup. This cost is why 256 is optional and full residency-sized dense
storage is rejected. Worker completion cost is excluded from those submission measurements.

## Validation

Subagent-run validation established 284 distinct passing cases across the broad regression and final
targeted rerun, with no remaining failures or skips in that selection. Coverage includes all cell
offsets at signed large origins, 192/256 bounds, GPU capture beyond the old boundary, overlap and
teleport/reuse rejection, real Surface Cache-backed screen lighting, L0 GPU routing and CPU fallback,
source/cache admission, publication identity, small-byte progress and both consumers' service under
single-cell budgets. Zero/resumed work budgets preserve publication, and source admission never
exceeds the eight-worker ceiling, including after a live budget change.

The broad run passed 281 of 283 cases. Its two failures were obsolete test expectations that the
GPU resident commit path loads the CPU raster uploader shader. The corrected test retains all
radiance, zero-CPU-read and lighting/resource-lifetime assertions. The final targeted run passed
27 cases, including those four CPU/GPU lifetime combinations and one additional source-budget case.
Two readiness tests were also corrected to check their low flag bits while separately checking the
block identity now stored in the upper bits. Production build/deployment passed with zero warnings
and zero errors. `git diff --check` passed.

Five older `SealedRoomUsesSharedHitLighting` cases remain excluded: their fixture supplies voxel
lighting without a Surface Cache snapshot, while the current screen shader requires that snapshot.
Their initial failures are preserved; their assertions were not weakened. Expanded-domain screen
transport was instead validated through `ScreenProbesConsumePublishedCache`, which supplies actual
captured and lit cache pages. The previously documented ring-retention fixture gap remains outside
this selection. No game process was launched.

Receipts and reproduction commands:

- `artifacts/TestResults/expanded-geometry-focused.trx` (initial fixture failures).
- `artifacts/TestResults/expanded-geometry-focused-final.trx` (73 passing focused cases and cost evidence).
- `artifacts/expanded-geometry-cost-receipt.txt` (extracted cost observations).
- `artifacts/TestResults/expanded-geometry-regression.trx` (281 pass / two corrected assertions).
- `artifacts/TestResults/expanded-geometry-final-targeted.trx` (27 pass).
- `artifacts/expanded-geometry-command.ps1` and `artifacts/expanded-geometry-final-command.ps1`.
- `artifacts/expanded-geometry-deploy.log` (production build/deployment).
