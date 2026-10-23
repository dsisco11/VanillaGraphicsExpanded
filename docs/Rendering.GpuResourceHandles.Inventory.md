# GPU resource handle implementation inventory

Planning companion to [the approved proposal](Rendering.GpuResourceHandles.Proposal.md) and
[implementation checklist](Rendering.GpuResourceHandles.todo). This records the source inventory and
chosen implementation mechanics; it does not describe an implemented resource registry.

## Wrapper and ownership inventory

Paths below are relative to `VanillaGraphicsExpanded/Rendering` unless stated otherwise. All wrappers
inherit `GpuResource`'s local disposal, `Detach`/`ReleaseHandle`, captured manager/context and default
ownership unless a row says otherwise. All native IDs are integers except `GpuFence`'s pointer-sized
sync. Registration must occur when a native object exists, not merely when a CPU declaration is created.

| Kind | Existing wrappers / acquisition | Metadata and disposition |
| --- | --- | --- |
| Texture | `GpuTexture`: `Texture2D`, `Texture3D`, `DynamicTexture2D` (including `DepthTexture`, `StencilTexture`, `DepthStencilTexture`), `DynamicTexture3D`, `DynamicTextureCube`, `PmjJitterTexture`, `PBR/Clouds/CloudBlueNoiseTexture` | Allocation helpers generate/bind/allocate. Target, dimensions, internal format, allocated mips, immutable-layered flag and storage generation participate in validation/dependencies. Replacement attaches a new handle; in-place storage mutation advances the existing handle revision. Filter mode is an owner policy, not a complete snapshot of arbitrary GL sampler parameters. |
| Texture | `BorrowedTexture(int)` | Validates `IsTexture`, queries target/base dimensions/format and mip widths. Non-owning; transfer rejected; dispose is local only. Same live native object must share the owner's handle. |
| Texture | `GpuBufferView`, derived `GpuBufferTexture` | Own the texture name, not the buffer. Buffer ID, format, offset and size describe the view; track texture association changes. Whole-buffer views currently use size zero as a sentinel, not a cached capacity; preserve that convention. |
| Texture | `GpuTextureView` | Owns a distinct texture object sharing immutable storage, with source reference/ID/generation, target, format, base/count levels and layers. It gets its own object handle, never the source texture's handle. Preserve current source-generation validation. |
| Buffer | `GpuBufferObject`: `GpuVbo`, `GpuEbo`, `GpuUniformBuffer`, `GpuShaderStorageBuffer`, `GpuAtomicCounterBuffer`, `GpuIndirectBuffer`, `GpuPixelPackBuffer`, `GpuPixelUnpackBuffer` | CreateBuffers with GenBuffer fallback; size/usage are storage metadata. Target is the wrapper's intended binding role. EBO index type/count are authored interpretation/content extent, not discoverable from GL bytes. Pack/unpack methods return mapping pointers to their callers; they do not retain a mapping snapshot for notification to refresh. |
| Framebuffer | `GpuFramebuffer`: `DynamicGpuFramebuffer`, `StaticGpuFramebuffer` | Generated owners and external non-owning wraps. Attachment dictionary, wrapped extent, surface/pass metadata and dirty publication. External attachment changes refresh actual attachment descriptions, preserving ownership of existing image wrappers. Window-surface format/samples remain on the explicit surface publication path; default framebuffer zero is not a native object handle. Static attachment locks stay in force. |
| Texture / Renderbuffer | `GpuFramebufferAttachment` | Constructors immediately allocate owned backing storage, or factories retain a typed/raw external image; base `OwnsResource=false`, but owned backing storage is released through its existing disposal queue/finalizer mechanism. Share the backing handle. Selected mip/layer/cube face remain local view choices. External dimensions/format/samples must refresh. `Detach` rejected. |
| Renderbuffer | `GpuRenderbuffer.Create` / `Wrap` | Create owns; Wrap borrows. Storage format, width, height and actual sample count refresh from native storage. Preserve current transfer semantics rather than treating every wrapper as an owner. |
| VertexArray | `GpuVao.Create` | Wrapper retains ID/debug name, not a complete attribute mirror. `GpuVertexAttribBinding` is a value configuration helper holding VAO/index/buffer/offset/stride/divisor. Preserve authored geometry descriptions; make stale helper reuse detectable by handle/revision, or resolve current facts before fallback use. No invented global VAO layout cache. |
| Program | `GpuProgramObject.Create` / `Adopt`, `GpuComputePipeline` | Binding cache/layout and executable revision/context. Compute declarations can begin without an executable and later install one; acquire on installation in its actual context. Relink/binary replacement invalidates prepared executable metadata; existing owner preparation must establish a usable layout again. `Shaders/GpuProgram` and `GpuComputeProgram` are composition owners, not additional GpuResource subclasses. |
| Shader | `GpuShaderModule` | Adopt/compile/load shader, fixed shader type and authored binding contract. Shader compilation data is not a mutable storage descriptor; program link preparation remains responsible for reflected interfaces. Explicit no-storage-metadata handler. |
| Query | `GpuQuery` | Generated name; results are queried on demand. No retained mutable resource metadata. |
| Sync | `GpuFence` | FenceSync creates pointer-shaped native identity; signal/wait result is queried, not cached storage metadata. |
| TransformFeedback | `GpuTransformFeedback` | Generated object; no retained binding/configuration snapshot in wrapper. StateCache binding state remains separate. |
| Sampler | `GpuSampler` | Generated name; setters do not retain a complete parameter snapshot. No mutable wrapper metadata to refresh. |
| ProgramPipeline | `GpuProgramPipeline` | Generated name; stage/program calls and validation query native state, no retained stage mirror. |
| Semaphore / MemoryObject | `GpuSemaphore`, `GpuMemoryObject` | EXT creation/import, capability gated. No represented mutable metadata; importing handles does not change ownership semantics. |

The `new IsValid` properties in concrete wrappers must also consume shared validity; changing only the
base property would leave bypasses. Debug labels and authored policies remain wrapper-local.

## Lifecycle and existing enforcement

`GpuResource.Dispose` checks retirement, clears the local ID, invokes hooks and schedules owned deletion.
`Detach` performs the same existing guard check, clears local state and transfers the ID without deletion.
`GpuResourceManager` queues kind/ID/context today; its drain has no incarnation token. Both immediate
and queued switches use StateCache for Buffer, VertexArray, Texture, Framebuffer, Renderbuffer, Program,
Sampler, and direct GL for Query, Sync, TransformFeedback, ProgramPipeline, Shader, Semaphore, MemoryObject.
All fourteen kinds need a common deletion notification route. EXT operations need explicit adapters;
`EngineStateCallMap` currently matches methods on `GL`, not nested `GL.Ext`.

Existing enforcement sites: `GpuResource.RequireMutableStorage`/retirement; texture allocation and
dynamic resize; renderbuffer allocation; framebuffer Configuration/PassMetadata/SurfaceMetadata/
Publication; `StaticGpuFramebuffer` frozen attachment references; `RenderPassTargets` pass references;
`RenderPassSequence` lifetime references; `GpuProgramLifetime` and compute scopes. Move their alias-wide
counters to the handle, retaining the same checks and releases. Declaration-only state remains local
until realization. Do not introduce a second mutation guard at the adapter boundary.

`GpuResourceManager` drains managed cleanup before native deletion, defers live foreign contexts and
abandons dead contexts. `RenderContextRegistry` identifies handle plus registration generation and emits
`Retiring`; absence of registration is not authority for persistent external tracking. Preserve the
context-free declaration path and existing shutdown exception policy.

## Metadata command matrix

The controlling coverage matrix is [Rendering.GpuResourceHandles.ApiCoverage.md](Rendering.GpuResourceHandles.ApiCoverage.md). It derives all applicable command families and variants from the OpenGL API, independently of existing callers. The wrapper inventory above maps represented fields to those families. Current call-site receipts below are integration examples only and cannot justify omitting an API operation.

## Concrete implementation decisions

These are selected mechanics within the proposal, not additional acceptance requirements.

* `GpuResourceHandle` carries immutable `(contextHandle, contextGeneration, kind, nint id)` and a unique
  managed incarnation, `Live -> Retiring -> Deleted` or `Live/Retiring -> ContextRetired`. Terminal states
  never revive. Metadata availability and `ulong MetadataRevision` are separate from native existence.
* `StateCache.AcquireResource(kind, id)` returns the existing live handle or registers a validated object;
  `AssociateResource(handle, wrapper)` / `UnregisterResource(handle, wrapper)` maintain weak wrapper
  references. The context registry holds handles strongly until observed deletion/context retirement,
  so collecting all wrappers cannot create competing incarnations. Prune expired associations on
  acquisition, dispatch, unregister and context cleanup; keep no strong wrapper references. Retention
  is bounded by live registered native objects plus live associations, not by number of acquisitions.
  Owning creation uses `RegisterCreatedResource(kind, id)` at the existing creation boundary: generated
  names can be reserved before first bind and have unavailable storage metadata until initialized.
  External acquisition must validate the realized object; a generated name alone is insufficient.
  CPU-only compute declarations and default framebuffer zero have no object handle. Compute installation
  transfers the candidate handle association with its executable; it does not create a new incarnation.
  `StateCache.Current` is thread-static today, so the resource lookup must be keyed by registered context
  generation rather than treated as another thread-local binding dictionary. Keep the common lookup in
  StateCache's resource partial, synchronize retirement/registration access, and subscribe once to
  `RenderContextRegistry.Retiring`. Thread-local binding invalidation must not clear live identities.
* `GpuResource.HandleMetadataChange(in GpuResourceMetadataChange change)` is abstract. The description
  carries family, affected subresource and revision plus established values when available. Native
  metadata readers are typed; common dispatch does not inspect texture fields. Mark affected metadata
  unavailable before dispatch, visit every live association even if one fails, and publish a wrapper's
  current revision only after its complete refresh succeeds. Publish handle metadata availability only
  after all applicable refreshes succeed; one failure therefore keeps dependent use fail-closed across
  aliases. Exceptions follow existing caller policy. A later successful refresh can restore metadata,
  never a deleted handle. New associations must refresh against the current revision before becoming usable.
* Default mutation mechanics use targeted native readback of represented fields after the command;
  do not drain `GetError` or assume a nonthrowing GL call succeeded. Initialize query outputs to invalid
  sentinels, validate the complete result and object/context identity. A rejected command may therefore
  report unchanged actual storage; it must not report requested storage. Unqueryable/unsupported facts
  remain unavailable. Existing caller-owned error boundaries remain untouched. Use arguments without
  readback only where an existing validated operation establishes them; measure refresh costs separately.
* `GpuDeletionCommand` carries the originating handle/incarnation in addition to the existing context.
  Owner retirement atomically marks Retiring; drain deletes only that registered incarnation in its
  live origin context. Observed external deletion terminally retires it and removes the lookup, so an
  old queue item/owner cannot delete its replacement. Raw deletion adapters still execute the requested
  native command and apply binding consequences; they resolve notification identity at execution.
  Failed deletion leaves managed use unavailable and preserves existing shutdown policy. List adapters
  preserve native ordering and per-object outcomes rather than treating a list as an atomic transaction.
  Managed enqueue paths capture the identity before leaving the owning context; they must not reacquire
  a handle by numeric ID at drain time. Worker retirement changes only thread-safe handle state and
  queues work; registry mutation and native metadata queries remain on the origin context thread.
* Wrapper native replacement detaches its old association and acquires a new handle. `Detach` removes
  only the transferring wrapper association, keeping the live registry entry and other aliases intact.
  No native deletion is attached to handle release. Context retirement removes entries and invalidates
  aliases without issuing GL calls. Shared-context namespaces remain outside scope.
* Metadata revisions augment existing `ShaderInputValidation.InputKey`, not a parallel history. Retain
  complete-input validation before publication and independent StateCache binding comparisons. Metadata
  refresh also feeds existing attachment observers and source-generation dependencies. Availability is
  checked at dependent operations, including concrete `new IsValid` paths.

## Native coverage and consumer gates

`EngineStateTargets` scans only VintagestoryLib, VSEssentials, VSSurvivalMod and VSCreativeMod. Its IL
discovery matches exact GL member references from `EngineStateCallMap`; generic methods are rejected.
Current adapters cover single texture/buffer/sampler/framebuffer/renderbuffer/VAO/program deletion and
`DeleteBuffers(int, ref int)`. They do not currently cover storage mutation or all deletion overloads.
Third-party mods, dynamically emitted calls and nested EXT calls are not automatically intercepted.

Installed engine IL already confirms `ClientPlatformWindows.CreateFramebuffer` and
`SetupDefaultFrameBuffers` use the pointer `TexImage2D` signature and `FramebufferTexture2D`;
setup also uses `TexImage2D<float>(..., float[])` and `BufferData(BufferTarget,int,IntPtr,BufferUsageHint)`.
`DisposeFrameBuffers` uses single `DeleteTexture(int)` / `DeleteFramebuffer(int)`; `DisposeFrameBuffer`
uses the latter. `SystemRenderOITLayers.BeforeOIT.rebuild` uses pointer `TexImage2D`/`TexImage3D` and
`FramebufferTexture2D`/`FramebufferTextureLayer`; `freeResources` uses single texture deletion.
Reconcile all of these, including liquid-depth rebuild, before persistent depth acquisition.

Closed generic upload overloads need explicit exact map entries: the current `GL.GetMethod` lookup
cannot resolve a generic definition merely from `float[]` argument types. Installed calls include
`TexImage2D<float>` and `TexImage2D<int>`. Extend map construction to pair every applicable closed generic
MethodInfo with a stack-compatible concrete adapter. Extend `EngineStateTargets` token discovery to
resolve the corresponding MethodSpecification tokens as well as MemberReferences; merely adding an
adapter would not discover a method that only calls that closed generic operation. This is distinct
from the existing rejection of generic containing engine methods. Do not generalize to arbitrary
generic engine callers. Nested extension deletion likewise covers every applicable declaring
type/signatures, not a broadened scan of unrelated assemblies.

Cloud `CloudNativeMapAdapter` validates native map storage at allocation and keeps publication/world/
upload/generation certificates. `CloudStoredVolumeConsumer` currently constructs map, material, primary
visibility and liquid-depth borrowers per use. Material depth is already owned and should be shared
directly. `DepthHierarchyRenderer` retains a primary-depth borrower until resize/world retirement;
`ScreenResourceManager` invalidates state and invokes ordered consumers after framebuffer rebuild.
These intended consumers may gain persistent tracking only after their exact allocation/mutation/
deletion signatures are adapted and exercised through actual redirection. Allocation hooks retain any
content-certificate responsibility; they are not the common identity mechanism.

Unsupported third-party raw IDs keep current conservative validation/invalidation. This is a deliberate
external boundary, not permission to omit the installed engine's required cloud/depth paths. Native
signatures are preserved in [resource-command-inventory.txt](../artifacts/gpu-resource-handles-baseline/resource-command-inventory.txt)
(VGE, CloudRendererMap and ClientPlatformWindows, with adapter/discovery flags) and
[engine-gl-inventory.txt](../artifacts/gpu-resource-handles-baseline/engine-gl-inventory.txt)
(broader engine callers plus built-in assembly references). `target=True` means the containing method
is discovered for some supported call; it does not mean its unadapted mutations are intercepted.

`CloudRendererMap.makeTexture` uses pointer `TexImage2D` (adapter false, target true);
`FreeGlResources` has four single `DeleteTexture(int)` calls and one `DeleteFramebuffer(int)`
(adapter and target true). Its framebuffer attachment call is currently unadapted. This is a covered
deletion path with required mutation adapter additions, not a need for another producer teardown hook.
The VGE IL confirms `NamedBufferData(int,int,IntPtr,BufferUsageHint)`, both buffer-texture range
signatures in the matrix, and `BufferStorage(BufferTarget,int,IntPtr,BufferStorageFlags)`.

## Validation and restoration dependencies

`ShaderPreparedSubmission` checks lifetime each use; `ShaderInputValidation` keys already contain wrapper,
numeric ID, target/format/dimensions/mips or view/range/capacity. They lack incarnation/revision and assume
wrapper fields are current. Raw sampler submission checks `IsTexture` and invalidates its unit before
binding. Typed publication can suppress known-equal binds. Preserve both paths until coverage permits migration.

`StateCache.TextureLifetime` records boundary retirement, invalidates image slots and resets matching
texture bindings after deletion. `StateCache.ResourceSlots` keys all image view parameters and indexed
buffer ranges. `StateCache.BoundaryBindings` captures/reconciles incoming state and refuses restoration
of retired names. Context retirement and unknown-state recovery remain independent of resource validity;
a shared live handle is never evidence that an object is bound. Review source as authoritative over the
historical findings in `Rendering.ShaderBindingState.md`; its Prepared resource submission section
describes the current validation path.


## Reproducible baseline

Source HEAD: `35e1481c9d6f94ef8de4fdded5499bccf6a11867`; runtime implementation unchanged.
[Baseline artifacts](../artifacts/gpu-resource-handles-baseline/) retain the source archive, final test
instrumentation patch/source manifest, full test-output ZIP, canonical package ZIP, binary/package
SHA256 manifests and exact fixture outputs. [replay.ps1](../artifacts/gpu-resource-handles-baseline/replay.ps1)
records the commands and process-only observation settings. Use the retained baseline source plus
instrumentation for comparison, not a later checkout's runtime DLL. Restore dependencies before using
its `--no-restore` test command. SDK: .NET 10.0.401; Release; Windows x64; RTX 4090; reported GL 4.3.0,
NVIDIA 610.88. Binary identities include the installed engine, API, VSEssentials and OpenTK.

Subagent verification: [focused suite](../artifacts/gpu-resource-handles-baseline/baseline-existing.trx)
105 passed, zero failed/skipped; [final calibrated fixtures](../artifacts/gpu-resource-handles-baseline/baseline-final.trx)
3 passed, zero failed/skipped. [Canonical package](../artifacts/gpu-resource-handles-baseline/package.log)
used `./build.ps1 --configuration=Release --spirv=true` and passed. Packaged runtime SHA256 matches tested
`A5D61F8844D81D1C2E1C5F14C5813F26E793452E6308AAA12100742468116176`; all 761 SPIR-V files match test outputs,
and 762 shader/manifest entries match canonical generated assets. The final test DLL is
`D5ADB644588F1853238C851C7DB33DC27597B56DC345B673C83092B6AD2066A8`.

| Measured interval | Native texture binds | Texture metadata queries |
| --- | ---: | ---: |
| Cloud map acquisition | 5 | 12 |
| Eight stable cloud publication generations | 0 | 0 |
| Four cloud input borrower constructions | 0 | 24 |
| Typed cloud view draw | 68 | 0 |
| Depth first publication | 4 | 6 |
| Depth stable publication | 2 | 0 |

Each interval has a named `.counts.txt` receipt. `ResourceCallObserver` calibrates all four intercepted
OpenTK entry points: BindTexture and the bound/DSA texture-parameter/level queries. Counts cover those
entry points, not all GL queries, IsTexture checks, FBO queries, CPU costs or whole-frame time. No
registration/dispatch implementation exists in this baseline; later measurements must report its cost
separately. Calibration restores its native binding and is excluded from measured intervals.

The cloud fixture uses a 20x20 map and 18x14 view with authored stored fields, constructs the four
borrowers and runs actual typed view submission. Its six output images exactly match the owned-input
fixture; draw/read FBO, active unit and texture binding are restored. `cloud-typed-view-output.f32`
retains 20,160 bytes. This is not a full live CloudStoredVolumeConsumer scheduler measurement.
The depth fixture runs the actual renderer with a 7x3 target, checks complete mip chains of 0.4 then
0.7, and retains two 100-byte `.f32` outputs. Existing engine boundary/FBO tests provide complementary
restoration/rebuild coverage. None of this establishes game appearance or frame-time improvement.

Initial sandboxed restore/test attempts stalled and were canceled; successful runs used the available
restored packages with process-only observer settings. Logs also preserve corrected test-only compiler
failures (source removal during enumeration and an ambiguous Buffer type). The final candidate passed;
no runtime baseline failure was observed in the selected checks, and no failing test was waived. The removed call-site inventory
fixture's historical output is optional integration context, not required API coverage evidence or a
fixture to restore in the final replay.
