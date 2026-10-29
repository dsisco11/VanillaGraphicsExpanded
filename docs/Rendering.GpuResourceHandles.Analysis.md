# Binding and metadata acquisition analysis

Planning findings for phase 4d of [the checklist](Rendering.GpuResourceHandles.todo), based on
source at `82e1651b5232663a1ca39aa6ff6528744f868e38`. This is an analysis record, not the
architecture decision or a claim that the proposed repairs are implemented.

## Contract and evidence

- [Proposal](Rendering.GpuResourceHandles.Proposal.md), Responsibilities, Canonical metadata,
  Publication and Command coverage: manager-owned canonical facts; wrappers receive snapshots;
  preserve binding suppression, errors, original identity and context; arguments alone do not prove acceptance.
- [Inventory](Rendering.GpuResourceHandles.Inventory.md), schemas, registration, publication and
  validation/restoration: distinguish declarations, realized objects, untracked tools, image projection,
  mutable storage and executable facts. The current full-reader mechanics are the subject of review.
- [API coverage](Rendering.GpuResourceHandles.ApiCoverage.md), scope, destinations and T1–P1:
  GL/Core, EXT and ARB interception only, modern retained operations, no content-only notifications.
  Direct VGE operations have a separate boundary. VAO metadata is intentionally an empty revision schema.
- [Shader binding](Rendering.ShaderBindingState.md#prepared-resource-submission): preparation and
  input validation consume published facts; these are distinct from native binding knowledge.

Every row below maps a named checklist method to its implementation, caller families, downstream
readers and disposition. Paths in code references are relative to `VanillaGraphicsExpanded/Rendering`.
The companion source-call index preserves individual production call sites, including exact adapter
overloads; grouped caller descriptions below do not narrow their scope. Query counts stated as source
counts describe calls along the specified successful branch, not GPU synchronization or timing.
Native probe receipts are recorded separately. No runtime behavior is changed by this analysis.

The buffer and memory-object reader/observer findings are superseded by the implemented maintained-fact
routes in [the lifecycle inventory](Rendering.MetadataLifecycle.Inventory.md#maintained-buffer-and-memory-object-routes).
Ordinary buffer association no longer discovers or automatically recovers descriptors. The historical
rows below remain migration input for the other families; they do not authorize buffer query fallbacks.

Query, fence and semaphore lifecycle behavior is likewise superseded by the maintained synchronization
routes in the lifecycle inventory. Their schemas remain empty; only query/wait outputs, the approved
targeted semaphore-import lifecycle check and deletion outcomes remain native reads. Missing covered
identities cannot authorize discovery or automatic recovery.

### Shared scenario rules

These rules apply to every row, including the reader and observer rows:

1. An available live registry entry can supply facts whether its original object was VGE-created,
   externally created, or passed back as a raw numeric alias. Ownership alone does not certify bindings
   or future observation coverage. Forced refresh is a policy choice, not an inherent property of raw IDs.
2. Missing entries require initial discovery unless the creation/command boundary already establishes
   all represented facts. Unavailable entries require recovery; old snapshots cannot simply be reused.
   Retiring/deleted entries and original associations must not adopt reused IDs.
3. Native observers generally select **existing** entries and never register every engine object.
   With no manager, storage observers are no-ops; selector cache observers still operate. Managed
   wrapper commands use the manager's static context-local acquisition path and can still read GL.
4. Context-local tools have no persistent canonical entry. Their repeated reads are current conservative
   behavior, not evidence that VGE owns no facts. Changing this requires an explicit architecture decision;
   do not introduce another registry or tracking machinery in `GpuResource<T>`.
5. Native `Is*`, descriptor getters, binding getters and reflection calls are separate counts. No
   claim that each call stalls the GPU follows from this inventory. First-use capability initialization
   is separate from the steady-state counts below.

## Binding paths

The binding increment in CommandPublication (`3f307365:docs/Rendering.CommandPublication.todo`) implements these
repairs; the rows below retain the original source baseline for comparison. Controlled selector
transitions now retain inputs without post-bind queries. EBO commands retain per-VAO facts independently
of revision publication. Known framebuffer scopes no longer issue binding getters, while cold
single-direction scopes acquire only that direction. Helper result construction retains supplied fields;
stale DSA getters acquire only the requested unknown field. Exact external selectors still dispatch
unchanged and recover uncertain results lazily. These changes do not imply completion of the acquisition
and publication findings later in this report.

| Method and source | Production route and observations | Disposition |
| --- | --- | --- |
| `StateCache.UseProgramBinding` — `StateCache.Bindings.cs` | `UseProgram`, program-owner/scope and restoration paths converge here. Changed or unknown selection clears state, calls `UseProgram`, then `GetCurrentProgram`: one bind plus one query. Known equal selection skips both only with `currentProgramStagesKnown`. | Avoidable readback candidate for established managed transitions. Keep separate knowledge of full-stage installation: EXT stage/uniform-destination selectors cannot be equated to a full `UseProgram`. Raw rejected selectors need a defined recovery contract. |
| `StateCache.BindVertexArray` — same file | `GpuVao`, geometry, helpers, scopes, boundary restoration and mapped engine selectors. One bind plus one `VertexArrayBinding` getter on changed/unknown state; known equal skips. | Uniform readback includes VGE-owned VAOs; selection validity and canonical availability are different. Preserve rejected-selector correctness without requiring the same recovery policy for every trusted command. |
| `StateCache.BindProgramPipeline` — same file | Pipeline wrapper, scopes/restoration and mapped native selectors. Changed/unknown: one bind plus one getter; known equal: zero. | Same policy question; equal suppression is present, not removed. |
| `StateCache.BindBuffer(ElementArrayBuffer)` — same file | EBO wrappers/upload fallback, geometry, scopes and engine calls. Equal known per-VAO association skips. Otherwise vertex observation drops EBO knowledge before/after dispatch; returned call immediately queries actual EBO. Unknown VAO adds a selector query. | Confirmed lost opportunity to retain established association independently of revision notification. Not all buffer targets incur this readback. |
| `StateCache.VertexArrayElementBuffer` — `StateCache.VertexNamedCommands.cs` | `GpuVao` named EBO configuration. Native command plus vertex observation; no immediate getter, but next EBO access must recover it. | Previous explicit cache notification is absent. Named update need not destroy current-VAO selection; restore per-VAO binding knowledge when result is established, independently of helper revision. |
| `GpuVertexAttribBinding` constructors — `GpuVertexAttribBinding.cs` | `GpuVao` returns a binding helper; its two update methods construct replacement helpers. Private constructor always forces four named getters when ARB DSA is available, even with supplied fields and the current revision. | Confirmed repeated acquisition of supplied configuration. The public arbitrary-binding constructor may need discovery; the managed result constructor has stronger facts. Do not conflate these. |
| `GpuVertexAttribBinding.BindVertexBuffer` — same file | Calls `RequireCurrentFacts`, named command or authored fallback, then the private constructor. Successful DSA update rereads buffer, offset, stride and divisor. | Requested buffer/offset/stride and previously current divisor are available at this boundary; establish accepted results centrally rather than discard all facts. Fallback stores an authored description for later per-attribute commands. |
| `GpuVertexAttribBinding.SetDivisor` — same file | Same pattern; requested divisor and retained buffer/offset/stride feed a constructor that rereads all four. | Narrow mutation becomes full binding refresh. Fallback has no equivalent immediate native binding-index mutation. |
| `GpuVertexAttribBinding.RequireCurrentFacts` — same file | Every getter/configuration method. Matching registered revision returns without queries; forced refresh or changed revision reads four values. No-entry DSA calls repeat reads because the fast path requires an entry. Non-DSA changed registered revision throws; no-entry fallback retains authored fields. | Preserve stale-copy/identity checks, but coarse VAO revisions refresh unrelated binding fields. This also explains repeated reads in tools. A full VAO mirror is not implicitly authorized by this finding. |

### Scopes and boundary restoration

`BindScopeTracker.Begin/End` use current getters and only invoke transitions when IDs differ.
For known A → B → A, VAO/pipeline scopes currently add two native binds and two post-bind queries;
an unknown incoming selector adds one initial query. An unchanged known scope adds neither.
Program scopes also carry program-owner/full-stage semantics and must not be reduced to ID equality.
`BindBufferScope` adds one initial query for unknown generic bindings; non-EBO changed enter/exit adds
two binds without post-bind queries. EBO enter/exit adds two result queries, plus any unknown VAO/EBO
capture. These are source counts with no intervening mutation or failure.

`BindFramebufferScope` unconditionally queries **both** read and draw bindings at entry, even with
known state or a read-only scope. It restores directions independently. This is extra acquisition cost
for fallback framebuffer readers and discovery, not permission to collapse split read/draw bindings.
`BindTextureScope` captures active unit and selected binding through cached getters; cold state adds
their queries. Cube image readers use this path even with DSA because faces are read separately.
Buffer/renderbuffer fallback readers similarly acquire and restore temporary bindings.

`CaptureBoundaryBindings` reuses known entries and queries unknown ones. With an empty resource footprint,
cold core binding capture includes active texture, VAO, array buffer, EBO and independent read/draw FBO
selectors; requested texture/sampler/image/indexed slots add their own queries. A known unchanged boundary
does not need those captures again. Restoration uses the owning cache operations: changing VAO/EBO
therefore inherits the post-bind reads above. Strict boundary diagnostics and resource-retirement checks
are separate from metadata queries and must remain intact. Category restoration skips known-equal
fields; external invalidation can defeat that suppression but is not itself a metadata refresh.

## Native observation pairs

All exact native adapters are ordinary compiled methods under `HarmonyPatches/NativeAdapters`.
They capture a token, run their exact GL overload, set `returned`, and complete in `finally`.
`returned` means **no managed exception**, not native success. Static map precedence means some
engine signatures go through managed cache methods instead. The call index records both routes.

| Begin / Complete and source | Before / after behavior, callers and queries | Structural assessment |
| --- | --- | --- |
| `BeginNativeVertexMetadata` / `NativeVertexMetadata.Complete` — `StateCache.NativeVertexMetadata.cs` | Managed vertex commands and external generic-attribute/vertex-binding adapters. Resolve bound VAO only if needed, capture original entry, withdraw availability; EBO operations drop its cache. On returned dispatch, publish empty schema and metadata revision; no descriptor read. On exception, leave unavailable. | Revision invalidates helper copies, even on native rejection; that does not require EBO invalidation for every accepted EBO command. Token preserves original identity. No-entry path still handles EBO cache. |
| `BeginNativeBufferBinding` / `NativeBufferBinding.Complete` — `StateCache.NativeBufferBinding.cs` | Exact buffer-binding adapters remove target cache before/after. EBO delegates to vertex observer; other targets publish no metadata. Next consumer resolves unknown binding. | New overload coverage uses invalidation; it is not the same behavior as managed `BindBuffer` argument retention. Token holds cache/target, no original entry except nested EBO observer. No result read in completion. |
| `BeginNativeVertexSelection` / `NativeVertexSelection.Complete` — `StateCache.NativeVertexSelection.cs` | External selector adapters only clear VAO before/after, no resource revision or read. | Post-selection invalidation is required when result is unknown; two-sided clearing protects against reentrant cache fills. Whether a before/after token is worth retaining for a pure selector is a design question. |
| `BeginNativeProgramSelection` / `NativeProgramSelection.Complete` — `StateCache.NativeProgramSelection.cs` | External program/pipeline selectors; clears affected selector and, for program selection, scope owner/full-stage knowledge. No reads or resource revision. | Preserve distinction between uniform destination, stage selection and pipeline binding. Do not infer full-stage installation from `CURRENT_PROGRAM`. Two-sided clearing handles reentry; manager unavailability does not disable cache handling. |
| `BeginNativeFramebufferRouting` / `NativeFramebufferRouting.Complete` — `StateCache.NativeFramebufferRouting.cs` | External routing adapters. Read-route returns default (no cache mirror). Draw-route resolves named/draw FBO, checks existing mutability rule and invalidates draw routing twice. No descriptor refresh. | Newly covered external mutation; do not label this removal of managed draw-routing caching. Token retains original cache/FBO. Required unknown-route recovery can be lazy; arguments may permit narrower established updates later. |
| `BeginNativeBufferStorage` / `NativeBufferStorage.Complete` — `StateCache.NativeBufferStorage.cs` | External allocation adapters, not the managed wrapper overloads in `StateCache.BufferStorage`. Capture existing bound/named entry and reader, withdraw storage facts. Completion refreshes full buffer metadata even after managed exception; suppresses secondary failure when dispatch threw. | Original entry prevents reused-ID publication. Generated buffer realization matters. Known size/usage exist at managed commands, but this external observer deliberately has no requested descriptor. Separate admission/result policy from token shape. |
| `BeginNativeRenderbufferStorage` / `NativeRenderbufferStorage.Complete` — `StateCache.NativeRenderbufferStorage.cs` | External storage adapters; reject unrelated bound targets, resolve only needed binding, capture/withdraw; full reader after return or exception. | Actual sample count is driver-derived; dimensions/format and sample request are known at owned creation. Keep targeted result discovery, not necessarily full descriptor refresh. |
| `BeginNativeTextureMetadata` / `NativeTextureMetadata.Complete` — `StateCache.NativeTextureMetadata.cs` | External storage/view/base-max/mipmap/renderbuffer-association adapters. Normalize cube faces, exclude proxy/invalid targets, resolve bound/unit selectors. Capture original entry, change kind, availability and association renewal evidence. Full texture reader normally follows; mutable mip generation uses descriptor comparison. Available immutable mip generation returns default. | Immutable content-only generation already avoids metadata work. Parameters need not rescan all images; mutable allocation/generation requires allocation-result evidence. Target/identity and association proof cannot be replaced by requested values alone. |
| `BeginNativeBufferTexture` / `NativeBufferTexture.Complete` — `StateCache.NativeBufferTexture.cs` | External buffer-texture adapters, not managed fresh-view helpers. Capture requested native-width range and previous certified descriptor, withdraw destination. Returned dispatch calls association verifier then publishes; exceptions leave unavailable without read. | Previous descriptor distinguishes rejected commands and whole-buffer policy. Original entry matters; narrower result validation already exists, but queries overlap known command/backing facts. |
| `BeginNativeFramebufferMetadata` / `NativeFramebufferMetadata.Complete` — `StateCache.NativeFramebufferMetadata.cs` | External attachment/default adapters. Capture existing FBO, enforce existing guards, withdraw availability. Returned dispatch refreshes complete descriptor; exception skips read. Finally notify framebuffer dependents, preserving original exception and avoiding old-context callbacks. | Dependencies must be invalidated on failure; full scan after every narrow edit is a separate policy. Context and original identity safeguards are meaningful. |
| `BeginNativeFramebufferParameter` — same file | Filters five represented attachmentless defaults and delegates to the shared FBO token. Other parameters no-op. | One default change currently causes all attachment/default reads. Candidate for targeted update, not blanket removal of shared failure behavior. |
| `BeginNativeFramebufferAttachment` — same file | Attachment adapters retain command evidence for array-selection mode, using cached limits and existing texture-level facts; no extra native read in this prelude beyond FBO selector resolution. | Some attachment modes cannot be reconstructed unambiguously from a name/level alone. Keep command evidence separately from native descriptor discovery; shared completion still scans every slot. |
| `BeginNativeProgramMetadata` / `NativeProgramMetadata.Complete` — `StateCache.NativeProgramMetadata.cs` | Link/binary/block/unit adapters plus managed program commands. Resolve active program, possibly pipeline's active program via one getter. Ignore numeric/unrepresented unit locations. Withdraw entry/preparation and capture original entry; returned supported mutation runs full program reader. Unsupported bindless units remain unavailable. | Linking needs driver reflection; changing one sampler unit or block binding does not change the whole linked interface. Current refresh is too broad for such narrow changes. Retain executable-change versus unit-change distinction and deferred linking. |

### Cross-cutting completion findings

Capturing original entry identity is justified independently of query policy: completion must never look
up a replacement object by reused numeric ID. Capturing the original cache also prevents accidental
updates to a newly selected context's cache. Manager refresh checks original context/thread and live
registration. Vertex/program tokens check context before publication; buffer/renderbuffer/texture
refresh checks it in the manager. FBO completion explicitly suppresses old-context dependent calls.
Selector tokens clear the original cache without native reads; they do not claim a new context's state.

Pre-command unavailability protects reentrant consumers and interrupted updates. Its necessity for each
synchronous selector is less obvious: selector tokens have no canonical entry to withdraw. Two-sided
cache clearing prevents a reentrant observation from being retained as the final result. A simpler
execution boundary could enforce this, but phase 4d does not select that design.

Exception policy is inconsistent across families: buffer/renderbuffer/texture attempt recovery during
unwind; vertex/program/buffer-texture skip it; FBO skips read but not dependent invalidation. Original
dispatch exceptions are generally preserved, while publication failure after normal return can throw.
Native rejection is handled by actual readback or conservative revision, without draining `GetError`.
An interrupted group intentionally differs: it must not publish a partial configuration.

`MetadataUpdate<T>` groups managed texture/FBO commands by wrapper identity, not canonical entry.
It defers `ObserveMetadata`, then reads once at Dispose after Complete; failure leaves facts unavailable.
Exact external observers do not consult this group set, and `RequireNativeAttachment` still queries
inside a group. This is two execution/publication mechanisms with overlapping responsibilities. Review
alias/reentry behavior and exactly-once publication in 4e; neither mechanism is automatically the winner.

## Manager acquisition and publication

| Method and source | Callers, actual read path and frequency | Disposition |
| --- | --- | --- |
| `AcquireResource<TMetadata>` — `GpuResourceManager.Registry.cs` | Wrapper/image/program acquisition, backing dependencies and buffer-association verifier. Available live entry + default false returns with no reader. Missing: one TryRead then publication. Unavailable/forced: RefreshMetadata. | Cache reuse works; do not replace it just because its name says Acquire. Forced refresh and indirect dependency reads are the costs. |
| `AcquireWrapperMetadata<T>` — `GpuResourceManager.MetadataAcquisition.cs` | BorrowedTexture; GpuRenderbuffer.Wrap; adopted GpuProgramObject; GpuShaderModule; buffer registration; initial FBO binding/pass publication. Compute-pipeline construction/preparation explicitly passes false; others default true. Generic texture borrow preserves established target and rejects buffer textures. | Forced refresh also affects live VGE aliases. Missing/external initial facts need discovery; subsequent covered aliases can reuse. Context-local path always reads and clears failed snapshots. |
| `AcquireBufferTextureBacking<TMetadata>` — `GpuResourceManager.BufferTextureDependencies.cs` | RefreshMetadata/PublishMetadata only, nonzero buffer associations. Verifies original backing identity then AcquireResource default false; records entry. | Query-free for available backing; missing/unavailable requires buffer reader. Necessary dependency admission, not itself an unconditional association read. |
| `AcquireRenderbufferTextureBacking<TMetadata>` — `GpuResourceManager.RenderbufferTextureDependencies.cs` | Same publication entry points for nonzero renderbuffer associations; default cache reuse. | Same disposition. Its comment describes upstream evidence; this method does not query the association itself. |
| `AcquireExternalImageTexture` — `GpuResourceManager.ImageAcquisition.cs` | FromExternalTexture, attachment discovery, attachment reconciliation with unknown target. Forces refreshExisting true, including raw IDs of VGE-owned textures; context-local full reader. | Confirmed stable-entry reread. Keep unsupported external coverage conservative, but distinguish covered raw aliases from unobserved external objects. |
| `AcquireImageMetadata` — same file | Attachment creation/replacement/validation and FromExternalImage. Existing original association returns; managed owner uses backing entry; raw images force refresh unless acquiredTexture supplied. Missing entry acquires; association projects metadata. Context-local branch reads unless texture snapshot supplied. | Managed projection is already query-free. Supplied snapshot avoids duplicate acquisition in the common managed registry route. Local validation repeatedly reads; metadata availability/replacement safety must survive any change. |
| `AcquireCreatedProgramMetadata` — `GpuResourceManager.ProgramAcquisition.cs` | Newly created GpuProgramObject only. Publishes known empty unlinked executable and installs reader without a GL query. | Good existing example of creation-established facts. No generalized creation certificate needed. |
| `AcquireProgramMetadata` — same file | GpuProgramInterface preparation. Reuses available executable entry; missing/unavailable reads full program, rejects unlinked; no-manager always reads. | Correct reuse boundary; reflected interface is driver output. Supplied snapshots avoid this route altogether. |
| `RegisterBufferWrapper` — `GpuResourceManager.MetadataAcquisition.cs` | Vbo/Ebo/uniform/storage/atomic/indirect/pixel pack/unpack constructors. Register declaration, IsBuffer, then AcquireWrapperMetadata which invokes reader.IsBuffer again when realized. | Confirmed duplicate validity test for realized owned buffers. GenBuffer names may legitimately be unrealized, so remove repetition by preserving creation/realization distinction, not assuming every generated name has storage. |
| `RefreshMetadata<TMetadata>` — `GpuResourceManager.MetadataPublication.cs` | AcquireResource recovery/forced refresh; native storage/FBO/program observers; managed observation/link completion. BeginRefresh, full reader, FBO-mode reconciliation, dependency acquisition, publish and dependent callbacks. | One reader per refresh, not per wrapper. Full-reader policy repeats unchanged fields; dependent publication can acquire missing backing. Failure retains unavailable state and notifies dependents. |
| `ObserveWrapperMetadata<T>` — `GpuResourceManager.MetadataObservation.cs` | Managed buffer/renderbuffer commands; ObserveMetadata/group completion; program completion. Register/associate then RefreshMetadata; local path direct reader. | Common publication authority works, but managed size/format inputs are discarded before generic read. Local failure discards snapshot. |
| `PublishMetadata<TMetadata>` — `GpuResourceManager.MetadataPublication.cs` | Acquired candidate, observed fact publication, empty VAO notification, dependency propagation. No direct GL query; dependency helpers may query missing/unavailable backing. | Documentation's unqualified query-free description is too strong. Already available dependencies make this query-free. Do not remove dependency validation. |
| `PublishObservedMetadata<T>` — `GpuResourceManager.MetadataObservation.cs` | Empty schemas, fresh program, verified buffer/texture views. Register/associate/publish; local snapshot assignment only. | No direct read, but managed publication can indirectly acquire backing as above. Creation-known facts can already enter through this boundary. |

## Native readers and narrow view verification

All readers below run for both owned and adopted resources whenever the caller chooses full acquisition.
The reader generally does not know that its input was just supplied by VGE. Available-entry reuse
prevents invocation; it does not make the reader's own queries cheaper. All counts exclude capability
initialization, scope capture and any downstream dependency reads unless stated otherwise.

| Method / source | Query footprint and downstream use | Known inputs versus necessary discovery |
| --- | --- | --- |
| `GpuBufferMetadataReader.TryRead` — `Metadata/Native/GpuBufferMetadataReader.cs` | IsBuffer + size + usage: three calls. Named DSA or CopyReadBuffer scope. Used by all buffer acquisition/refresh and texture backing admission. | Owned allocation already supplies size/usage; realization/result failure needs explicit treatment. New external buffers need both descriptor facts. Duplicate IsBuffer at registration is independently redundant. |
| `GpuTextureMetadataReader.TryRead` — `Metadata/Native/GpuTextureMetadataReader.cs` | IsTexture; optional target query; immutable/view parameters; width for every legal level/face; height/depth/format for allocated images, samples for multisample; base/max. Cube/fallback binds temporarily. | VGE supplies target, allocation dimensions/format/levels/view range. Sparse external images require discovery; samples/unsized format resolution and failed allocations cannot be assumed. Full level scan after base/max change repeats unchanged storage. Unknown target without DSA remains unsupported rather than speculative binding. |
| `GpuRenderbufferMetadataReader.TryRead` — `Metadata/Native/GpuRenderbufferMetadataReader.cs` | IsRenderbuffer plus width/height/format/samples: five calls, named or bound scope. | Actual samples can differ from request; preserve that discovery. Owned dimensions/format need not be reread just because samples do. |
| `GpuRenderbufferTextureMetadataReader.TryRead` — `Metadata/Native/GpuRenderbufferTextureMetadataReader.cs` | Capability guard, texture scope, one association getter; called by texture reader after validity/target handling. Publication then acquires backing renderbuffer if needed. | Existing dependency available: no second backing read. Association command already carries requested backing but rejection must be resolved. Retained EXT operation is in scope even though token names contain NV. |
| `GpuFramebufferMetadataReader.TryRead` — `Metadata/Native/GpuFramebufferMetadataReader.cs` | IsFramebuffer; type for every color/depth/stencil slot; name for occupied slots; four additional texture-selection values, optionally two multiview values; five attachmentless defaults where supported. Fallback scope always adds two binding getters. | Managed attachments/defaults already carry most facts; full scan repeats untouched slots and may repeat RequireNativeAttachment. External FBO discovery needs descriptors, but its schema does not need to rediscover backing image storage on every descriptor refresh. |
| `GpuShaderMetadataReader.TryRead` — `Metadata/Native/GpuShaderMetadataReader.cs` | IsShader + DeleteStatus + ShaderType: three calls. GpuShaderModule constructor after VGE CreateShader uses it before checking stage equality. | Confirmed redundant stage discovery for successful owned creation: CreateShader's requested stage is immutable. Adopted/deletion-pending objects need a separate validity policy. No shader content query belongs here. |
| `GpuProgramMetadataReader.TryRead` — `Metadata/Native/GpuProgramMetadataReader.cs` | DeleteStatus/LinkStatus; for linked program seven interface count/name-capacity queries, one resource-property call per resource, names except atomic buffers, one GetUniform per sampler/image array element. Does not call its IsResource helper. | Link/binary results and active interface require driver discovery. Unit/block reassignment does not alter most of that interface; full reflection there is confirmed excess scope. Fresh unlinked creation already bypasses it. |
| `StateCache.ObserveBufferView` — `StateCache.TextureViews.cs` | Managed fresh buffer-view constructor only, via four whole/range named/bound helpers. Reads actual buffer and format (two), then publishes supplied native-width range; fallback texture scope. | Intentionally targeted result certification, not full descriptor acquisition. Both values duplicate requested facts, but fresh association acceptance is currently proven by them. Removing these needs an accepted-result contract, not just ownership. |
| `StateCache.TryObserveBufferAssociation` — `StateCache.NativeBufferTexture.cs` | External token completion only. Two association/format reads; backing AcquireResource; up to two representable range checks, additional old-range checks on a mismatch. Detach exits early. | Existing backing reused. Whole-buffer mode and wide range cannot be inferred from narrow getters alone; keep command evidence and old certified descriptor. Narrow verification may still be reducible with established admission facts. |
| `StateCache.ConfigureTextureView` — `StateCache.TextureViews.cs` | GpuTextureView constructor: TextureView then full texture reader, compares view range, publishes. Source wrapper already provides immutable dimensions/range/format context. | Whole-image scan duplicates derivable view geometry, but accepted view creation/format compatibility and exposed range need a defined result guarantee. Original source storage identity remains separate. |

For a texture reader, with F faces, L scanned levels and A allocated face-level images, the image
getter count is F×L + 3×A (+ one sample getter per allocated multisample image), plus parameter and
validity/optional-target queries. It scans absent upper levels too. For an FBO, empty slots cost one
type getter; occupied renderbuffers cost two; textures cost six or eight with multiview, plus defaults
and validity. These formulas identify scale; they are not measured frame costs.

## Additional named entry points found

These extend the checklist rather than hiding additional work in a generic catch-all.

| Method | Trace and finding |
| --- | --- |
| `StateCache.TryGetBufferBindingQuery` | GetBoundBuffer uses this for unknown generic bindings; AtomicCounterBuffer currently maps to All.AtomicCounterBufferStart rather than All.AtomicCounterBufferBinding. The retained gl.xml and pinned enum inventory show these are distinct tokens. This is a confirmed selector-mapping defect, separate from redundant-query policy, to carry into binding repair. No runtime correction in this analysis. |
| `StateCache.BindFramebufferScope` | Readers, FBO operations and attachment discovery use it. Two unconditional read/draw binding getters even for known same-ID state; preserve split restoration while avoiding redundant capture. |
| `StateCache.RequireNativeAttachment` | Managed attachment commands, including grouped updates. Targeted accepted-result checks (two for renderbuffer/detach, more for texture selection) precede full reader later. Depth-stencil recursively checks both slots. Confirmed repeated descriptor queries across verification/publication. |
| `StateCache.ObserveFramebufferAttachment` | Calls RequireNativeAttachment, records array mode evidence then ObserveMetadata. Grouping removes full refresh per command but does not remove its targeted verification. |
| `StateCache.BindFramebufferResource` | Initial managed bind triggers AcquireWrapperMetadata when HasNativeMetadata false; a newly realized empty FBO incurs full descriptor/default discovery. Existing available wrapper avoids it. |
| `StateCache.BeginTextureStorageUpdate` | Texture allocation loops, including cube faces/mips, defer reader until complete group. Known allocation facts are not collected; full scan once. Failure intentionally stays unavailable. |
| `StateCache.BeginTextureMetadataUpdate` | Texture mip-range updates group base/max commands into one full texture reader; storage descriptors unchanged. Candidate for targeted parameter publication. |
| `StateCache.BeginFramebufferAttachmentUpdate` | FBO pending-image reattachment grouping; per-slot verification plus final full scan. Alias identity/grouping scope needs architectural review. |
| `StateCache.MetadataUpdate<T>.Dispose` | Complete branch calls ObserveWrapperMetadata once; failed branch calls FailWrapperMetadataUpdate with no read. No global grouping of external observers or aliases. Preserve complete configuration publication. |
| `GpuResourceManager.RefreshGeneratedTextureMetadata` | Native mutable mip observer. Full texture reader, compares allocated levels, storage revision only if layout differs; recovery notifies. Immutable usable generation skips this altogether. Broad scan is a policy candidate, revision distinction is correct and must remain. |
| `StateCache.SubmitProgramLink` | Managed async link submission withdraws executable metadata and deliberately discards token without Complete; no immediate query. This is an intentional deferred operation, not a missing finally. |
| `StateCache.CompleteProgramLink` | Shader completion coordinator reaches explicit dependency and performs full reflection for tracked program; context-local coordinator uses ObserveWrapperMetadata. Reflection is necessary here, not at submission. |
| `StateCache.SetProgramUniformUnit` | Shader preparation: known-equal canonical unit skips call. Changed unit uses token and full program reflection; non-DSA scope also adds program binding work. Preserve equal suppression, narrow changed-unit update. |
| `GpuFramebuffer.PublishRenderPassMetadata` | External publication scans slot types, discovery reads occupied descriptors/backing metadata, then AcquireWrapperMetadata scans FBO again. It intentionally starts from empty borrowed helper but need not rediscover facts already established in the same transaction. |
| `GpuFramebufferAttachmentDiscovery.Read` | Returns known attachment without native reads unless forceNative; otherwise scope, descriptor queries and external image acquisition. Called from pass discovery and image consumers. Necessary cold discovery; forceNative repeated acquisition needs explicit coverage rationale. |
| `GpuFramebuffer.ReconcileAttachmentMetadata` | Existing matching image references reused; changed descriptors use FromExternalImage. Unknown target calls AcquireExternalImageTexture; known target but no supplied snapshot can still force an image acquisition. Deduplicates equal descriptors within one pass. Candidate to reuse canonical backing facts consistently. |
| `GpuFramebufferAttachment.ValidateSlot` | Local validation invokes AcquireImageMetadata again; registered images receive manager projection instead. Local tools pay repeated storage reads; retain failure clearing and replacement safeguards. |
| `GpuResourceManager.AssociateImageResource` / `AssignImageMetadata` | Association and selected-image projection only; no GL queries. Missing/invalid selection remains unavailable. Preserve this query-free design. |
| `StateCache.CompleteResourceDeletion` / `WasResourceDeleted` | All deletion routes: Is-kind getter, and delete status for retained shader/program objects. These are outcome checks, not metadata acquisition; record separately so future query policy includes owned deletion without assuming immediate physical reclamation. |
| `GpuPreparedBindings` constructor / `ReadBlocks` | Production GpuProgramInterface supplies acquired metadata; optional fallback still queries GL. Already approved phase 4h removes this responsibility; do not duplicate that implementation in acquisition repairs. |

## Architectural decision inputs

The discussion below records the questions raised by this analysis. The resulting
approved proposal (`3f307365:docs/Rendering.CommandPublication.Proposal.md`) and
implementation checklist (`3f307365:docs/Rendering.CommandPublication.todo`) now govern their resolution and repairs. The user subsequently approved controlled inputs/defaults/
derivations with existing failure handling rather than universal post-command verification. Historical
readback recommendations above are evidence of the reviewed implementation, not requirements to retain
those reads. The proposal now maps each method/family to its narrowed disposition.

Confirmed repeated work: realized-buffer double validity checks; shader stage discovery after creation;
helper result constructors rereading supplied binding fields; full texture storage scan after base/max
change; full executable reflection after a unit/block update; targeted FBO verification followed by full
scan; unconditional FBO scope selector capture; forced raw-alias refresh despite available covered facts.
Some are presently used to certify accepted commands. This finding identifies repetition, not approval
to publish requested state after rejected operations.

Keep query-free available-entry reuse, image projection, empty metadata publication and fresh-program
publication. Keep discovery of external unknown state, driver-produced executable facts, actual samples,
and recovery from unavailable state where no accepted-result evidence exists. Narrow these reads where
other fields are already established. Coverage-qualified raw aliases and unsupported third-party IDs
must have distinct dispositions.

The analysis identified these decisions: command acceptance evidence for controlled versus arbitrary external
commands; whether begin/complete tokens, scoped execution or centralized execution best enforce it;
how full acquisition differs from narrow publication; how binding knowledge survives metadata updates;
how groups/aliases/deferred linking fit the same rules; and how local tools retain conservative recovery.
No new guards, lifetime keys, parallel registries, per-wrapper tracking or full VAO layout mirror are
implicitly selected. Repairs remain ordered binding first, acquisition second, supplied preparation third.

## Command-publication implementation dispositions

The original rows above retain their measured baseline. The following reconciliation maps every
named acquisition/observer/binding finding to the implementation; verification receipts are recorded
in the command-publication checklist (`3f307365:docs/Rendering.CommandPublication.todo`). Preparation consumes supplied
metadata; its caller owns any required acquisition.

| Analyzed entry points | Implemented disposition and evidence families |
| --- | --- |
| UseProgramBinding, BindVertexArray, BindProgramPipeline, BindBuffer(ElementArrayBuffer), VertexArrayElementBuffer | Managed transitions retain selected values and equal suppression; exact external selector operations withdraw uncertain knowledge. Binding/selector and EBO tests cover errors and reuse. |
| GpuVertexAttribBinding constructors, BindVertexBuffer, SetDivisor, RequireCurrentFacts | Retain supplied fields, original owner and revision; manager-owned GpuVertexBindingReader resolves only unknown fields. Helper tests cover DSA/fallback stale copies. |
| TryGetBufferBindingQuery, BindFramebufferScope, BindScopeTracker.Begin/End, BindBufferScope, BindTextureScope, CaptureBoundaryBindings | Correct atomic-counter token; known state avoids reads, fallback restores independent selections. Buffer-wrapper scopes and mapped-buffer fallback use those same scopes; element mapping uses a neutral copy-write selector. Managed-array upload fallback preserves prior selection. Binding-cache, boundary, unknown-buffer-binding and bound-acquisition tests retain coverage. |
| BeginNativeBufferBinding, BeginNativeVertexSelection, BeginNativeProgramSelection, BeginNativeFramebufferRouting and their completions | Internal synchronous StateCache execution bookkeeping; compiler-checked adapters only forward. Unknown selector/routing recovery stays lazy and exact external dispatch remains unsuppressed. |
| BeginNativeVertexMetadata / Complete | Exact vertex overload execution now belongs to StateCache Commands/VertexArrays. Original entry remains unavailable during dispatch; completion publishes only empty revision metadata. No layout/content mirror or descriptor read. Vertex/adaptor/reentry tests cover this boundary. |
| BeginNativeBufferStorage / Complete | Replaced by InvalidateNativeBufferStorage inside exact StateCache storage commands. Pending storage effects survive success/interruption; explicit acquisition recovers later. Buffer-storage/recovery tests cover rejected requests and aliases. |
| BeginNativeRenderbufferStorage / Complete | Internal exact storage execution, guarded original entry, external descriptor recovery after dispatch. Controlled allocation publishes inputs and only the approved sample output. Renderbuffer and native-observer tests cover both policies. |
| BeginNativeTextureMetadata / Complete, RefreshGeneratedTextureMetadata | Internal command ownership; affected parameter/image/range reads for external changes, full discovery only for unknown complete storage. Immutable mip content does not publish storage changes. Texture publication/generation tests cover partial cubes and failed commands. |
| BeginNativeBufferTexture / Complete, TryObserveBufferAssociation | Internal command ownership and narrow association/range proof; original backing identity retained. No eager untracked registration. Buffer-view dependency/alias tests cover whole/range and rejection. |
| BeginNativeFramebufferMetadata, BeginNativeFramebufferParameter, BeginNativeFramebufferAttachment / Complete | Internal exact dispatch with original-entry unavailability; targeted slot/default recovery preserves unrelated facts and mode evidence. Framebuffer publication/discovery tests cover failures and split bindings. |
| BeginNativeProgramMetadata / Complete, SubmitProgramLink, CompleteProgramLink, SetProgramUniformUnit | Internal immediate mutation or explicit deferred original-context completion. Reflect only executable output; narrow unit/block reads, no numeric-content metadata work. Executable publication/deferred-link tests retain coverage. |
| AcquireResource, AcquireWrapperMetadata, AcquireExternalImageTexture | Available-entry reuse precedes discovery. BorrowedTexture/renderbuffer Wrap no longer force recovery or duplicate object checks. Unsupported bypass mutations require explicit invalidation or RecoverResource/RecoverWrapperMetadata. External-acquisition reuse and lifecycle tests exercise both boundaries. |
| AcquireImageMetadata, AssociateImageResource, AssignImageMetadata, GpuFramebufferAttachment.ValidateSlot | Raw registered aliases share available entries. Existing associations receive manager projection; failure notifications do not trigger automatic recovery. Local typed owners supply snapshots without queries; raw local IDs retain discovery. Projection/reuse/failure tests cover missing images and replacement identity. |
| AcquireBufferTextureBacking, AcquireRenderbufferTextureBacking | Reuse available original dependencies; acquire missing facts only. Publication preserves original backing certificates. Buffer/renderbuffer texture dependency tests cover deletion, replacement and unavailable backing. |
| AcquireCreatedProgramMetadata, RegisterBufferWrapper | Superseded by CreateProgram/CreateBuffer and shared creation handoff. Empty program and buffer realization facts are published directly without duplicate validity reads. Creation tests cover nullable cleanup and generated names. |
| AcquireProgramMetadata | Reuse available executable; unknown executable uses driver reflection. Local raw IDs have no persistent registry and retain discovery. Prepared owners can supply their existing snapshot. Program acquisition/publication tests cover this boundary. |
| RefreshMetadata, ObserveWrapperMetadata, PublishObservedMetadata, PublishMetadata | Explicit recovery is separate from supplied-result commit. ObserveWrapperMetadata remains for synchronous link output. Missing dependencies can require acquisition; generic publication does not read its own descriptor. Unused ReadTextureOperationResult and PublishObservedMetadata were removed; creation handoff and operation-owned PublishMetadata are the active commit paths. Operation/recovery tests cover interrupted publication. |
| GpuBufferMetadataReader, GpuTextureMetadataReader, GpuRenderbufferMetadataReader, GpuRenderbufferTextureMetadataReader, GpuFramebufferMetadataReader, GpuShaderMetadataReader, GpuProgramMetadataReader | Retained for unknown external discovery/recovery and named output exceptions. Capabilities select named/fallback queries internally; no callers thread API-choice flags through the reader. Bound-acquisition and creation/publication tests distinguish required discovery from zero-query controlled results. |
| ObserveBufferView, ConfigureTextureView | Controlled creation derives association/range/view geometry through CreateBufferTexture/CreateTextureView; superseded post-create verification removed. Buffer/image creation and source-identity tests cover the result. |
| RequireNativeAttachment, ObserveFramebufferAttachment, BindFramebufferResource, PublishRenderPassMetadata, GpuFramebufferAttachmentDiscovery.Read, ReconcileAttachmentMetadata | Controlled attachment candidates replace verification-plus-scan. Explicit external discovery reuses one descriptor/backing set; forceNative remains an intentional recovery request. Framebuffer creation/discovery/projection tests cover reuse and unknown external state. |
| BeginTextureStorageUpdate, BeginTextureMetadataUpdate, BeginFramebufferAttachmentUpdate, MetadataUpdate.Dispose | Superseded by explicit operation-owned candidate construction and commit. Disposal only withdraws/cleans up; no implicit refresh. Operation foundation tests cover alias reentry, failure and context retirement. |
| CompleteResourceDeletion, WasResourceDeleted | Retain separate per-kind lifecycle outcome checks, including retained shader/program delete status and independently rejected list members. Do not infer name retirement from descriptor equality or physical reclamation. Deletion/retirement tests cover old associations, duplicate/zero names and retained references. |
| GpuPreparedBindings constructor, ReadBlocks | Preparation requires a complete supplied program snapshot. PrepareBlocks projects its block facts without native queries; missing facts reject preparation. GpuProgramInterface owns any needed manager acquisition, while graphics/compute candidate owners supply existing snapshots. Supplied-metadata preparation and acquisition-boundary tests cover this separation. |

## Verification status

Source tracing, second review and independent completion audit are complete. Every named task and
additional entry point has a disposition above; phase 4d is complete. Architecture approval is recorded
in the retained proposal; the command-publication repairs are implemented. Integrated qualification and
its evidence limits are recorded in Phase 6 of the parent checklist.
The phase 4d independent audit verified all 803 recorded source/binary identities against the files at
that review. [Probe receipts](../artifacts/gpu-resource-phase4d/README.md),
[counts](../artifacts/gpu-resource-phase4d/counts.json),
[source identities](../artifacts/gpu-resource-phase4d/source-identities.json) and
[call-site index](../artifacts/gpu-resource-phase4d/source-calls.txt) retain reproducible evidence.

The delegated Release build passed with zero errors; the calibrated native probe passed 1/1 with no
skips on RTX 4090, NVIDIA 610.88, GL 4.3 with ARB DSA. Changed/unknown program, VAO and pipeline
selections measured one bind and one getter each; equal known selections measured zero. Changed
scopes measured two binds and two getters; an unknown VAO scope added a third getter. Named EBO
mutation followed by recovery measured one named call plus one getter. Each helper constructor or
returned helper measured four indexed getters; unchanged registered helper access measured zero.
Available AcquireResource and refreshExisting:false wrapper acquisition measured zero calls; forced,
default-wrapper and unavailable recovery measured one IsBuffer and two parameter getters each.
Cold empty-footprint boundary capture measured six getters, warm capture zero. The unknown EBO
scope probe requested the already-native binding: two recovery reads, no bind; it is distinct from
a changed EBO scope.

Temporary probe source is archived and removed from the permanent test project. These receipts
qualify successful ARB DSA paths only. Non-DSA, exceptions, rejected commands, local tools and
dependency fan-out are source analysis, not measured branches. No GPU synchronization, CPU/frame
time, game appearance or package qualification is claimed. A package rebuild is unnecessary for
this analysis-only deliverable; future repairs retain their own build/package obligations.
