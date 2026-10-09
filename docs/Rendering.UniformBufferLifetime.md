# Uniform publication lifetime implementation

Source contract: [proposal](Rendering.UniformBufferLifetime.Proposal.md).
Execution: [implementation plan](Rendering.UniformBufferLifetime.todo), under
[water performance](PBR.WaterPerformance.todo). Implemented and verified on 2026-10-05; scope and measurement limits are recorded below.

## Ownership and policy

CpuUniformBuffer retains packing, change revision and write guards. UniformPublication holds the
fixed usage and successful physical version; both named and prepared-slot publication use it.
SingleFrame is the explicit compatibility default. SingleDraw never reuses across independent
publications; scope restoration may conservatively allocate again and does not count actual draws.
No engine-wide draw hook or one-draw migration is inferred from shader activation.

The existing ring/frame controller owns both allocators. Transient epochs retain immutable copies;
persistent size-class pages hold independently owned versions. Slots are rounded from the native
alignment, pages hold at least eight slots and normally 64 KiB, and retained storage has an 8 MiB
budget per allocator. Exhaustion fails publication; it never overwrites a live range. Pages remain
resident for reuse until owner teardown. These choices cover inventoried 16–4560 byte typed blocks
without a native object per activation and remain internal policy, not shader layout contracts.

Every successful logical bind records latest use, even when StateCache suppresses the native bind.
Scope restoration republishes through the same owner. Frame fences group those uses; released
versions become free only after their latest serial completes. A signaled fence never releases a
still-owned current version. Conventional persistent pages are never orphaned; mapped storage uses
coherent writes or explicit flushes. Pool reclamation polls and does not block; pressure fails at the
budget rather than guessing completion from frame age.

Publication commits revision and dirty state only after upload and binding succeed. A failed candidate
is released, while the previous successful version and pending CPU bytes survive. Earlier successfully
published blocks remain committed if a later resource fails, matching existing nontransactional
submission. Prepared numeric interface extents reject undersized blocks before publication, including
after reload; explicit data ownership remains necessary for same-sized but semantically different layouts.
Compatible slot changes rebind the same physical version. Raw GPU block paths remain supported.

Logical UniformPublication disposal is terminal. CpuUniformBuffer.Dispose retains its established
reset semantics by disposing that publication and allowing a later fresh publication of its CPU bytes.
GpuProgram.OwnUniformBuffer explicitly attaches ownership and write guards; terminal shader disposal
releases registered blocks, while engine executable reload and borrowing shader disposal do not.

Uniform allocators belong to one renderer/context lifetime. Dispose them while that context is
current, before its teardown. Automatic context replacement is outside this work; existing
StateCache context policy is unchanged.

One unsafe historical compatibility behavior is intentionally tightened: publication after EndFrame
fails until BeginFrame. Otherwise GPU use could escape the fence protecting its storage. Repeated
BeginFrame with pending writes seals the previous epoch before reusing a page. Transient waits flush
the command stream and reject failed fences rather than treating failure as completion.

## Consumer inventory

| Data family | Writes and owner | Selected policy |
| --- | --- | --- |
| LiquidDrawParamsUbo, 80 bytes | Per terrain-pool draw; each liquid shader owns an independent instance | Explicit SingleFrame; SingleDraw awaits a one-draw owned execution contract. |
| LiquidFrameParamsUbo, 4560 bytes | Atmosphere, medium, lighting and liquid effect inputs; independent surface/volume instances | Explicit SingleFrame. |
| LiquidWaveParamsUbo, 32 bytes; LiquidDepthFrameParamsUbo, 64 bytes | Frame-varying phases/weather and depth projection | Explicit SingleFrame. |
| LumOnWorldProbeResolveParamsUbo, 16 bytes | Atlas dimensions change with resource size; each resolve shader explicitly owns its block | MultiFrame; two resolve shaders register terminal ownership. |
| PBR direct lighting/composite, 3552/272 bytes | Camera/light/transport and pass publication inputs | SingleFrame compatibility. No persistent-policy benefit inferred from shader longevity. |
| Height bake, 528 bytes | Per tile/pass/solver iteration; shared intentionally by bake orchestration | SingleFrame compatibility. |
| LumOn probe/near-field/combine/upsample/HZB/debug; debug line/orb blocks | Per probe/pass/camera/debug draw | SingleFrame compatibility; later tuning requires measured stable writes. |
| SurfaceLightingParamsUbo, 144 bytes | Domain snapshots, owned by query/trace batches or screen-probe shader | SingleFrame compatibility pending workload benefit and all owners' disposal migration. |
| Packed compute blocks | Dispatch-specific externally packed complete blocks with SetBytes comparisons | SingleFrame default; callers can explicitly request another lifetime. |
| Existing native GPU blocks | Explicit GPU resources borrowed by prepared bindings | Unchanged resource policy; not CPU logical publications. |

No stable water-only block was invented or forced into retained storage. Water participates through
its explicit transient policy and mixed workload verification alongside genuinely stable non-water
atlas dimensions. Other candidates remain optional measured consumer tuning, not deferred allocator safety.

## Requirement traceability

| Proposal requirement | Implementation / verification |
| --- | --- |
| Intent, baseline, lifetime, identity and publication | UniformBufferUsage, UniformPublication, CpuUniformBuffer; independent activation, dirty/failure/retry and snapshot tests. |
| Transient storage | GpuUniformRingBuffer epochs/fences; repeated index, closed frame, overflow and actual GPU immutable ranges. |
| Persistent storage, latest use and OpenGL | PersistentUniformStorage, UniformStoragePage/Version; conventional/coherent/explicit-flush tests, last-use retirement and generation reuse. |
| Sharing, reload and disposal | Explicit shader ownership, common publication, prepared interface extents; borrowed slots/owners, reload and terminal versus reset behavior. |
| Integration and source layout | Existing frame controller, GpuUniformBuffer and StateCache; renderer-owned teardown, native failure and targeted invalidation checks. |
| Consumer migration, verification, measurements and completion | Consumer table above and mixed liquid/resolve workload; allocation/copy/bind/resident/pending/wait counters. |

## Measured allocation and storage results

The matched fixture runs eight frames with four liquid draws per frame, identical shader settings,
and stable resolve parameters. The transient reference is a SingleFrame block in the same fixture,
not a separate run of the original checkout. Conventional and mapped uploads produce the same payload counts:

| Counter | Transient reference | Retained stable block |
| --- | ---: | ---: |
| Stable block allocations | 8 | 1 |
| Stable block copied bytes | 128 | 16 |
| Stable block logical binds | 32 | 32 |
| Water copied bytes | 40,192 | 40,192 |

The fixture retains 196,608 bytes of transient pages and adds one 65,536-byte retained page.
At the completed measurement point, pending retirement and transient fence waits are zero;
the final Debug run recorded 13 retirement polls on each backend; Release recorded 16 for conventional
and 15 for mapped storage. Poll counts depend on completion timing. These are fixture results,
not production memory totals or guarantees of zero waits. The production transient ring uses three
2 MiB pages; the measurement fixture deliberately uses smaller pages. Separate retirement tests retain
pending versions until latest-use completion. The persistent allocator never blocks on pressure;
it fails at its budget. The fixture uses completion synchronization for deterministic counters.
CPU/GPU and live frame timing are unmeasured; allocation/copy reductions alone do not establish
frame-time improvement.

## Review and acceptance

A second source review followed publication failure paths, unchanged rebinding, repeated frame
epochs and shader ownership. A separate completion-audit pass identified missing dedicated
direct-write/MarkDirty and transient-exhaustion tests; focused GPU tests now cover those contracts.
The audit reconciled the
full proposal and linked authoring, demand-loading, uniform-validation and pipeline-ownership
contracts with the requirement table above. Compatibility defaults and conservative consumer
selection are explicit; no required persistent-lifetime or retirement work is deferred.

Behavioral coverage lives in UniformLifetimeTests, UniformLifetimeSharingTests,
UniformLifetimeMeasurementTests, alongside existing shader,
compute, ring and mutation regressions. Tests observe native GPU values, including nested retained
restoration, both upload mechanisms, explicit flushing, latest-use protection, failed publication
and retry, shader reload/disposal. Context-replacement coverage was removed with that unsupported scope.

## Linked-contract evidence

- [ShaderAuthoring](ShaderAuthoring.md#cpu-uniform-upload-lifetime): packing/revision guards,
  validation-before-publication, named/numeric shared authority and immutable snapshots map to
  CpuUniformBuffer, ShaderPreparedSubmission and UniformPublication; ShaderInputSubmissionTests,
  ComputeInputSubmissionTests and UniformLifetimeTests exercise these paths.
- [Demand loading](GPU.ShaderDemandLoading.md#graphics-ownership): terminal ownership remains
  separate from executable reload. GpuProgram.UniformOwnership and GpuProgram.Preparation release
  only registered owned blocks; UniformLifetimeSharingTests checks reload, borrower disposal,
  terminal disposal and nested restoration.
- [Uniform validation](GPU.UniformContractValidation.md): existing prepared numeric metadata supplies
  minimum active block extents through GpuPreparedBindings. Undersized blocks fail before upload;
  this does not prove semantic compatibility between equal-sized layouts. No replacement reflection
  parser or source-text test was introduced.
- [Pipeline ownership](Rendering.AuthoritativePipelineState.Proposal.md#architectural-responsibilities):
  resource binding stays in GpuUniformBuffer/StateCache; the frame controller only coordinates lifetime.
  UniformStoragePage, UniformStorageVersion, PersistentUniformStorage and UniformPublication separate
  storage, range identity, allocation/retirement and logical publication. The user's explicit removal
  of context-replacement support governs this work; future pipeline/context changes remain separate.

SingleDraw is verified as fresh allocation per independent publication, with conservative restoration.
No production consumer is claimed to have an enforced one-draw lifetime. The native error test injects
an existing GL error before the upload success check; it verifies failure bookkeeping and retry, not
hardware failure or out-of-memory recovery. Latest-use retirement is tested deterministically by
completing the original fence, rebinding in a later unsealed interval and rejecting premature reuse.
## Validation receipts

Final shader-enabled Debug and Release builds passed. Each configuration passed 80/80 focused tests,
with zero failures or skips, including the four added direct-write/exhaustion cases. Build/analyzer
warnings remain; these were not warning-free builds. The second review and independent completion
audit found no remaining required lifetime or retirement issue within the agreed scope.

The final focused selection includes UniformLifetime, CpuUniformBufferTests,
ComputeInputSubmissionTests, GpuUniformRingBufferIntegrationTests, LiquidShaderProgramTests,
LumOnWorldProbeRadianceTileResolveFunctionalTests, PreparedSubmissionTests,
ShaderInputSubmissionTests, EngineStartupContextTests, ShaderUniformPublicationTests and
ShaderInputSubmissionStateTests. Each name is an OR-ed FullyQualifiedName substring filter.

Run from the repository root with NUGET_PACKAGES=C:/Users/Sisco/.nuget/packages:

```text
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj -c <Debug|Release> --no-restore -p:EnableSpirv=true --filter <selection above> --logger "trx;LogFileName=uniform-phase6-<debug|release>.trx" --results-directory artifacts/TestResults
```

The initial Debug build hit a shader-cache atomic-write access error; its retry succeeded without
source changes or disabling shader generation. Keep that failed attempt separate from test failures:
artifacts/uniform-phase6-debug-build.log and uniform-phase6-debug-build-retry.log.
Final run logs are artifacts/uniform-phase6-debug.log and uniform-phase6-release.log, with matching
TRX files under artifacts/TestResults. No game was launched and no live visual acceptance or
frame-time claim is implied by these headless receipts.