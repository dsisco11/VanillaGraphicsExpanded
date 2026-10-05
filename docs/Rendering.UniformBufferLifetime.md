# Uniform publication lifetime: inventory and storage decisions

Source contract: [proposal](Rendering.UniformBufferLifetime.Proposal.md).
Execution: [implementation plan](Rendering.UniformBufferLifetime.todo), under
[water performance](PBR.WaterPerformance.todo). Decisions below are submitted for review;
implementation and later validation are outside this staged change.

## Storage decisions

Keep CPU packing and revisions in CpuUniformBuffer. Add a separate logical publication owner,
with SingleFrame as the compatibility default. SingleDraw must allocate per independent
publication until an owned execution boundary establishes stronger consumption semantics.

Use the existing ring/frame controller as the lifetime owner for both transient and retained
storage. For retained versions, select aligned size-class slots, pages of at least eight slots
and normally 64 KiB, with an 8 MiB allocator budget. Exhaustion fails publication rather than
overwriting live data. Pages remain resident until owner teardown. These are internal allocation
choices, not shader layout contracts; the inventoried typed blocks include sizes from 16 to 4704 bytes.

Support conventional non-orphaned retained pages and persistent mappings with coherent writes
or explicit flushes. Record every successful bind, including unchanged rebindings and restoration.
Group latest uses behind frame fences. Reclaim only released versions whose latest use completed;
never reclaim a still-owned current version. Poll retirement without blocking; report pressure failures.

Tie physical provenance to the registered context generation. Replace lost-context allocators
through the existing frame controller, preserving CPU contents for republication. Existing
GpuUniformBuffer, GpuFence, capability and StateCache owners retain native responsibilities.
Live independent contexts require separate owners; context replacement is not live migration.

Keep CpuUniformBuffer.Dispose reset compatibility distinct from terminal logical-publication
and shader-owner disposal. Borrowing shaders must not retire shared data. Executable reload
must revalidate prepared interface compatibility while preserving compatible logical instances.

## Consumer inventory

| Data family | Writes and owner | Proposed policy |
| --- | --- | --- |
| LiquidDrawParamsUbo, 80 bytes | Per terrain-pool draw; each liquid shader owns an independent instance | Explicit SingleFrame; SingleDraw awaits a one-draw owned execution contract. |
| LiquidFrameParamsUbo, 4704 bytes | Camera, atmosphere, medium, lighting and frame inputs; independent surface/volume instances | Explicit SingleFrame. |
| LiquidWaveParamsUbo, 32 bytes; LiquidDepthFrameParamsUbo, 64 bytes | Frame-varying phases/weather and depth projection | Explicit SingleFrame. |
| LumOnWorldProbeResolveParamsUbo, 16 bytes | Atlas dimensions change with resource size; each resolve shader has its own block | MultiFrame; register terminal ownership with both resolve shaders. |
| PBR direct lighting/composite, 3552/272 bytes | Camera/light/transport and pass publication inputs | SingleFrame compatibility. No persistent-policy benefit inferred from shader longevity. |
| Height bake, 528 bytes | Per tile/pass/solver iteration; shared intentionally by bake orchestration | SingleFrame compatibility. |
| LumOn probe/near-field/combine/upsample/HZB/debug; debug line/orb blocks | Per probe/pass/camera/debug draw | SingleFrame compatibility; later tuning requires measured stable writes. |
| SurfaceLightingParamsUbo, 96 bytes | Domain snapshots, owned by query/trace batches or screen-probe shader | SingleFrame compatibility pending workload benefit and all owners' disposal migration. |
| Packed compute blocks | Dispatch-specific externally packed complete blocks with SetBytes comparisons | SingleFrame default; other lifetimes require the planned API. |
| Existing native GPU blocks | Explicit GPU resources borrowed by prepared bindings | Unchanged resource policy; not CPU logical publications. |

No stable water-only block was invented or forced into retained storage. Water participates through
its explicit transient policy and mixed workload verification alongside genuinely stable non-water
atlas dimensions. Other candidates remain optional measured consumer tuning, not deferred allocator safety.

## Reference workload and measurement limits

The reference workload is eight frames with four liquid draws per frame: one 4704-byte
LiquidFrame update per frame, one 80-byte LiquidDraw update per draw, and an unchanged
16-byte resolve block published four times per frame using SingleFrame storage.

| Reference counter | Value |
| --- | ---: |
| Water allocations | 40 |
| Water copied payload bytes | 40,192 |
| Stable block allocations | 8 |
| Stable block copied payload bytes | 128 |
| Stable block logical binds | 32 |
| Total logical publications | 96 |
| Transient resident bytes in fixture | 196,608 (three 65,536-byte pages) |

These reference counts were checked by the transient-reference branch of the mixed workload
in the unreviewed implementation's UniformLifetimeMeasurementTests, not by a separate run of
the original checkout. The broader mixed fixture also publishes a retained comparison block;
its comparison results and test source belong to later review. Receipt:
artifacts/uniform-lifetime-complete-debug.log and the corresponding TestResults TRX.
The completed mixed fixture reported zero transient fence waits. It uses GL.Finish each frame,
so that result does not establish production wait behavior. Transient storage retires at page
fences; there is no independently retained-version pending-byte baseline. CPU, GPU and live
frame times remain unmeasured. The fixture's page size is not the production ring size.

## Linked contract reconciliation

Preserve generated validation-before-publication, std140 packing, mutation guards and prepared
numeric slots from ShaderAuthoring.md and GPU.UniformContractValidation.md. Preserve terminal
owner disposal versus executable reload from GPU.ShaderDemandLoading.md. Keep native binding
and capability authority with existing rendering owners as required by
Rendering.AuthoritativePipelineState.Proposal.md. Equal layouts alone never establish sharing.