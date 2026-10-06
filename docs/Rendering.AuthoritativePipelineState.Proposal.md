# Authoritative graphics pipeline state

Status: approved on 2026-10-03. Implementation plan: [Rendering.AuthoritativePipelineState.todo](Rendering.AuthoritativePipelineState.todo). No runtime implementation is included in this document.

## Intent

Evolve VGE's pipeline descriptions and GL state cache into an authoritative graphics submission system. A VGE-owned draw should establish its shader, fixed-function state, target compatibility, geometry bindings, resource bindings, and required dynamic values through a documented contract. Its behavior must not depend on undocumented state left by a previous draw.

Keep `StateCache` as the mechanism that suppresses redundant native operations. Build pipeline, render-pass, and submission responsibilities above it, preserving existing shader preparation, resource ownership, and engine integration.

The expected benefits are predictable draw behavior, earlier compatibility errors, reusable pipeline descriptions, and fewer scattered state operations. CPU savings, GPU savings, and reduced driver compilation stalls require measurement; none are established by this proposal.

## Current implementation and gaps

The name “GlPipelineState system” currently refers to several cooperating types rather than a complete graphics pipeline object:

| Existing code | Current responsibility | Gap relative to this proposal |
| --- | --- | --- |
| [GlPipelineDesc](../VanillaGraphicsExpanded/Rendering/GlPipelineDesc.cs) | Default/non-default intent masks and selected fixed-function values | Omitted values inherit ambient state; shader, vertex layout, topology, and target compatibility are absent |
| [GlPipelineStateId](../VanillaGraphicsExpanded/Rendering/GlPipelineStateId.cs) | Twelve tracked state categories | Rasterizer, stencil, blending, and multisample coverage is incomplete |
| [GlPipelineStateValidation](../VanillaGraphicsExpanded/Rendering/GlPipelineStateValidation.cs) | Mask and payload consistency | Descriptor constructor validation is currently conditional on DEBUG; no whole-draw compatibility contract |
| [StateCache](../VanillaGraphicsExpanded/Rendering/StateCache.cs) and its partial files | Cached fixed-function state, bindings, and redundant-call suppression | Best-effort synchronization with external GL mutations; no authoritative pipeline identity |
| [GpuProgram](../VanillaGraphicsExpanded/Rendering/Shaders/GpuProgram.cs) and preparation/input partials | Shader readiness, activation, persistent input publication, and engine ownership | These contracts need to be composed into graphics submission rather than bypassed |
| [GpuPreparedBindings](../VanillaGraphicsExpanded/Rendering/Spirv/GpuPreparedBindings.cs) | Validated executable resource assignments | Reusable foundation for pipeline resource-layout compatibility |
| [GpuFramebuffer](../VanillaGraphicsExpanded/Rendering/GpuFramebuffer.cs) | Attachment binding, validity, routing-related operations, viewport convenience, and clears | Target operations are invoked independently of pipeline application |
| [GpuFramebuffer.Blending](../VanillaGraphicsExpanded/Rendering/GpuFramebuffer.Blending.cs) | Optional per-output blend configuration | A second owner of state that belongs to the graphics pipeline |
| [DirectLightingRenderer](../VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs) | Composes state application, target setup, shader inputs, and rendering | Representative consumer for migration to an explicit pass and submission contract |
| [FramebufferBindingHook](../VanillaGraphicsExpanded/HarmonyPatches/FramebufferBindingHook.cs) | Observes selected engine framebuffer setters | Useful integration boundary, not proof that every engine/mod state mutation is observed |

`GlPipelineDesc` copies indexed arrays during construction but exposes arrays through getters. A readonly struct therefore does not make its complete payload immutable. It also has no explicit structural pipeline-key contract.

`StateCache.Current` is thread-local and relies on explicit invalidation at renderer and external-operation boundaries. Per the subsequent user scope decision, this work assumes one live rendering context; context switching/loss/replacement recovery is deferred. GpuSupport retains its capability registration lifecycle.

## Pipeline model

Separate stable graphics pipeline configuration from the resource instances used by a particular draw. A complete pipeline combines shader and vertex interfaces, blend/rasterizer/depth-stencil state, primitive topology, target formats, and sample count.

OpenGL realization remains a sequence of program and fixed-function operations through the state cache. The existing `GpuProgramPipeline` is a shader-stage composition object and does not, by itself, implement this complete graphics PSO contract.

Pipeline interning, prewarming, and asynchronous preparation are separate capabilities. A complete descriptor provides the identity and validation foundation for them; it does not by itself eliminate driver compilation stalls.

## Naming and namespaces

Use responsibility-based type names without `Gl`, `GL`, or `Gpu` prefixes. Namespaces already identify the rendering domain; namespace qualification or aliases can resolve ambiguity where necessary.

Proposed types use names such as `GraphicsPipeline`, `GraphicsPipelineDesc`, `RenderPassDesc`, `DynamicDrawState`, `GraphicsCommandContext`, and `StateCache`. Preserve meaningful distinctions such as graphics versus compute rather than encoding the backend or execution device in every name.

References to existing prefixed types in this document identify the current source accurately. As those types are migrated, use unprefixed names and update their references together. Naming changes preserve their behavior and ownership contracts; they do not require replacement resource hierarchies. New types follow this convention from the outset.

## Scope and exclusions

The initial authority boundary is VGE-owned graphics passes. Engine-owned rendering remains behind explicit adapters and restoration/invalidation boundaries until each relevant path is covered.

This proposal does not require a render graph, deferred command lists, multithreaded GL submission, a Vulkan/D3D backend, a new shader reflection system, or replacement of resource base classes. Compute submission should retain its existing implementation; common binding and lifecycle contracts can be reused without forcing graphics fixed-function state into compute pipelines.

Resource hazards and memory barriers remain explicit pass responsibilities. Pipeline application does not imply synchronization. A future render graph could automate dependencies without changing pipeline identity.

## Architectural responsibilities

The following type names are illustrative. Their responsibility boundaries are the proposed contract.

| Component | Owns | Excludes |
| --- | --- | --- |
| `GraphicsPipelineDesc` | Complete static configuration, shader/vertex interface identity, topology, target signature, dynamic-state declaration | Actual framebuffer and resource instances |
| `GraphicsPipeline` | Prepared and validated pipeline realization for a shader executable revision within one live rendering-context lifetime | Render-target allocation and application resource ownership |
| `RenderPassDesc` | Concrete target, output routing, attachment load/store intentions, clear values, render area | Shader and blend policy |
| `DynamicDrawState` | Declared dynamic values such as viewport, scissor rectangle, stencil reference, blend constant | Implicit inheritance from arbitrary GL state |
| Existing shader input/binding infrastructure | Actual shader resources and uniform values, validated against the executable contract | Fixed-function pipeline policy |
| Geometry binding description/adapter | Vertex/index buffer instances, offsets, index type, draw range, compatible vertex layout | Shader preparation |
| `GraphicsCommandContext` | Pass lifetime, pipeline selection, draw validation, submission ordering, external-state boundaries | Resource allocation algorithms and a second shader binding implementation |
| `StateCache` | Known native state and minimal state transitions | Pass policy, pipeline registry, resource ownership |

Dependencies flow from renderers into pass/pipeline/submission APIs, then into existing resource and shader abstractions and the state cache. The command context is a thin composition root. Compatibility validation, pipeline keys, pass descriptions, and engine adapters each belong in separate files with one clear responsibility.

## Complete descriptions and partial overrides

Preserve partial state descriptions for engine overlays and compatibility adapters where “change only these settings” is intentional. Give that behavior an explicit override/patch identity so it cannot be confused with a complete PSO.

A complete pipeline must resolve every supported static category against explicit project defaults. Unspecified construction fields may select those documented defaults; they may never mean “whatever GL currently contains.” Dynamic categories must be explicitly declared and supplied before a draw.

Pipeline creation rejects unsupported settings and invalid combinations in all builds. Expensive diagnostics can remain optional. Disabled features can have canonical inactive values for hashing, but application must still establish all state capable of affecting the draw. Re-enabling a category through a different pipeline must apply that pipeline's full configuration.

The old masks can remain an adapter representation. Existing stable bit numbering need not be rewritten to make the new complete descriptor possible.

## Static and dynamic state coverage

Approved configurable raster-state amendment: complete descriptions support individual user
clip-distance enables, clip origin/depth convention, alpha testing, point/line/polygon smoothing,
line/polygon stipple and point-sprite coordinate origin. These are static pipeline values, including
alpha comparison/reference, line repeat/pattern and the immutable 32-by-32 polygon mask. Existing
neutral values remain defaults, not support restrictions. Capability validation distinguishes core
features from compatibility-only features. Enabled clip distances require compiler-derived output information from the
final vertex-processing stage of the actual compiled variant. Preparation must reject missing or
unverifiable outputs before publishing a pipeline; authored shader masks are not required.
Alternate clip conventions require matching projection/reconstruction and viewport/front-face policy;
they do not implicitly select reversed-Z. Window depth range remains explicitly [0,1].
The shared StateCache owns native transitions, observation and boundary restoration, including disabled
parameters. Polygon mask transfers borrow and restore pixel-transfer layout and buffer bindings.
Logic operations, viewport arrays and fixed-function lighting remain outside this amendment.

| Category | Required description |
| --- | --- |
| Shader | Prepared graphics executable/variant identity, active stages, executable generation, resource-layout identity |
| Vertex input | Attribute locations, scalar interpretation, components, layout/divisor requirements, compatibility with the shader interface |
| Primitive assembly | Primitive topology, primitive-restart policy where supported, patch control-point count for tessellation |
| Rasterizer | Cull enable/mode, winding, polygon mode, depth bias, depth clamp/clip policy where supported, rasterizer discard, provoking convention where relevant |
| Depth/stencil | Depth enable/comparison/write mask; stencil enable, front/back comparisons/operations/read/write masks |
| Output blending | Per-draw-output enable, RGB/alpha equations, RGB/alpha factors, per-output color write masks |
| Sampling/output interpretation | Sample count compatibility, multisample controls, sample mask/coverage and alpha-to-coverage where supported, framebuffer sRGB policy |
| Dynamic declaration | Which values must be provided by pass/draw state rather than pipeline construction |

Viewport and scissor rectangles should be dynamic. Stencil reference and blend constant should also be dynamic when used. Line width and point size need an explicit policy; default them to static configuration unless a consumer needs declared dynamic values. Depth range must be explicitly owned by dynamic state or a documented fixed convention.

The implementation must inventory additional draw-affecting GL state used by production shaders and engine paths, such as clip distances and program-controlled point size. Unsupported features must be rejected or excluded by a verified boundary contract, rather than silently inheriting external values.

Blend entries are indexed by fragment draw-output slot. A pass maps these slots to color attachments. Sparse routing must preserve the distinction between output location, draw-buffer index, and attachment index.

## Target signatures and render passes

A target signature describes the enabled color output slots and their formats, optional depth/stencil format/aspects, and effective sample count. Pipeline reuse requires a compatible signature, not the same FBO ID or dimensions.

Use exact normalized format/sample matching initially. Any broader compatibility rule should be added with a documented justification and tests. Fragment output scalar classes and locations must agree with the routed targets; intentional discarded outputs require an explicit policy.

The render pass supplies the actual `GpuFramebuffer` and its `GpuFramebufferAttachment` instances. Existing owning/borrowing semantics remain intact: FBOs retain attachment references but do not acquire ownership. Pipelines do not own target attachments. An immediate pass retains references during its lifetime and rejects resources explicitly retired by their owners.

Attachment replacement, resize, or backing-resource changes require pass compatibility/completeness to be rechecked at a safe boundary. Changes during an active pass are rejected or require ending and beginning the pass. A same-format resize need not create another pipeline, but the render area and viewport must be refreshed.

Wrapped engine/default framebuffers need an explicit metadata provider or refresh boundary. Unknown format/sample metadata cannot silently pass strict validation. Default framebuffer handling must be deliberate rather than assuming it is an ordinary managed FBO.

Load/store intentions should initially support preserve, clear, and discard semantics where meaningful. Preserve retains contents; clear applies typed per-attachment values; discard permits contents to become undefined. Resolve operations and readback/blits remain explicit operations. Clear execution must establish the masks and scissor/render-area behavior it needs, independently of the graphics pipeline's write masks.

The current `BindWithViewport()` and clear helpers may remain compatibility APIs. Migrated passes use pass setup to coordinate these operations. FBO-stored blend settings migrate into pipeline blend descriptions.

## Prepared pipelines, identity, and lifetime

Pipeline preparation validates the shader stages, vertex interface, output interface, binding layout, static state, target signature, and context capabilities before publishing a usable pipeline.

Reuse `GpuBindingContract`, `GpuPreparedBindings`, generated shader inputs, and existing readiness/activation code. Add missing vertex/output interface validation at preparation time instead of querying linked interfaces for every draw.

The descriptor must have deeply immutable payloads and structural equality. Canonical keys include every behavior-affecting static field, dynamic declaration, target signature, vertex layout, and shader variant/layout identity. Debug labels are excluded. Hash collisions require equality comparison. Native object names alone are not durable identities because they can be reused.

Distinguish a reusable description key from a live realization bound to an executable revision and renderer lifetime. Shader reload invalidates dependent realizations; incompatible reloads fail validation before the new realization is published. Failed preparation must not leave a partially valid pipeline available for drawing. Preserve existing shader ownership and deferred-deletion mechanisms.

The prepared object retains the dependencies needed for immediate submission without taking ownership of externally owned shader resources. Explicit disposal still invalidates use. If a future implementation creates pipeline-owned native objects, their retirement must follow existing `GpuResource`/disposal-queue rules. A managed aggregate should not invent a native `ResourceId` merely to inherit `GpuResource`.

Start with explicit prepared objects. Interning can follow once keys and invalidation are proven. A later registry needs bounded retention or eviction; it must not keep obsolete shader generations alive indefinitely.

## Submission contract

Conceptual usage, not a committed API:

```text
BeginPass(targets, load/store intentions, clear values, render area)
SetPipeline(preparedPipeline)
SetDynamicState(viewport, scissor, required dynamic values)
AssignShaderInputs(existing generated input API)
Draw(geometryBindings, drawArguments)
EndPass()
```

Before a draw, the command context must:

1. Confirm the pass, target references, shader executable revision, and pipeline realization remain valid within the same live rendering-context lifetime.
2. Validate target signature, geometry layout/topology, required dynamic state, and resource contract.
3. Activate the shader through its established engine-aware lifecycle and publish current inputs through the existing submission path.
4. Establish pipeline, dynamic, geometry, and resource state through the appropriate adapters/cache.
5. Issue the draw only after setup succeeds.

The exact native ordering should minimize transitions while preserving these requirements. Shader activation must respect `ShaderProgramBase.CurrentShaderProgram`, overlapping-owner restrictions, failure cleanup, and prepared-input publication. A direct cached `GL.UseProgram` call is not an adequate replacement for that lifecycle.

Every draw must account for input changes even when the pipeline identity is unchanged. “Same pipeline” cannot imply “same textures or uniforms.” An adapter that invokes an engine draw helper must document all state the helper mutates and validate/observe those effects.

Untracked GL calls inside an authoritative pass are prohibited. A deliberate external operation must end/suspend authority and dirty the affected cached categories before subsequent managed draws. Start with non-nested immediate passes; reject accidental nesting until restoration semantics are explicitly designed.

Preparation/setup failure prevents submission and exits through exception-safe cleanup. EndPass applies store intentions and the documented external restoration policy; it does not dispose borrowed resources.

## Native error-checking policy

Ordinary cached raster transitions validate managed inputs and capability support, suppress known
unchanged values, and issue the necessary native commands without unconditional error polling.
They do not promise immediate detection of every driver error. Explicit external mutation and
resource-lifetime invalidation remain necessary for truthful cache authority.

Detailed raster-transition checks are opt-in through GlDebug.CheckStateTransitions, disabled by
default in both Debug and Release. A changed transition checks before and after its native command
when enabled, withholding affected knowledge on failure. Boundary restoration owns its checks
instead of repeating this diagnostic pair inside each setter. Known-equal transitions consume no
native errors even when diagnostics are enabled.

Checked restoration retains independent field-level attempts and targeted invalidation. Known-equal
fields require no native work or per-field error polls; changed or unknown fields are checked before
and after restoration. A pre-existing error rejects that checked attempt rather than being attributed
to its command; the affected field becomes unknown and independent cleanup continues. A native error
reported after a grouped command invalidates the entire affected group, not a guessed individual
component. Cleanup failure remains an unsafe handoff and propagates with the original operation error.

Pixel pack/unpack layouts validate their inputs before mutation and use optional transition diagnostics.
Changed layouts and scope restoration do not poll errors by default; checked boundary restoration owns
its checks. Pixel-transfer buffer changes remain checked safety operations before native code interprets
a managed pointer using those bindings. Known-equal bindings/layouts do not poll. Polygon-stipple transfer reuses those checked owners and
checks the transfer itself; restoration does not repeat checks already owned by a checked transfer.
Cold state reads retain native error checks so a failed query cannot publish a default as known state.
Unavoidable safety checks and optional diagnostics are accounted separately from state-value reads
and native mutations. Reduced call counts alone do not establish CPU or GPU timing improvements.

## Engine integration and cache authority

There are three separate concerns:

- Knowing what GL currently contains.
- Establishing what a VGE draw requires.
- Restoring what engine code expects after VGE returns.

Harmony observation hooks improve the first concern. They do not automatically satisfy the other two.

Define an engine boundary adapter that captures the necessary incoming state or receives it through a known engine contract, runs the VGE pass, restores required state and engine-side ownership bookkeeping, and invalidates anything it cannot prove synchronized. State queries are acceptable at these boundaries and for diagnostics; avoid queries during steady-state managed draws.

Inventory engine mutation entry points by category and mark each as observed, explicitly restored, or unknown. Hooks should record successful mutations without recursively issuing the same native call. Known hooks do not establish coverage for arbitrary other mods or raw GL calls.

Confine cached state, scopes and prepared realizations to the same live rendering context. Dispose realizations at renderer teardown and preserve explicit cache invalidation at lifecycle/external boundaries. Do not add context generations to StateCache or pipeline identities in this work; context loss/replacement/switching recovery is deferred by user direction. Capability readiness remains owned by GpuSupport.

Resource retirement and native name reuse must invalidate affected bindings. Keep existing texture/buffer/VAO-aware tracking; extend equivalent lifecycle handling where the complete pipeline path exposes gaps.

## Interpretation of existing project TODOs

The entries in [project.todo](../project.todo) need to be reconciled with these ownership boundaries when implementation is planned:

| Existing intent | Proposed treatment |
| --- | --- |
| Expected renderbuffer binding | Resource-operation binding/cache support; not a graphics PSO field. Existing `BindRenderbuffer` and scope support should be audited for coverage |
| Expected framebuffer binding | Concrete target in render-pass setup; compatible format/sample signature in the graphics pipeline |
| Expected shader binding | Prepared executable dependency in the graphics pipeline, activated through existing shader ownership/submission APIs |
| Engine Harmony state hooks | Verified observation at identified engine boundaries, with explicit fallback for unobserved mutations |
| Remove all invalidation and legacy capture calls | Replace only where synchronization and restoration are proven; retain boundary mechanisms where needed |

This proposal links to those tasks without marking them complete or assuming all hooks are absent. Existing hooks must be inventoried before implementing additional ones.

## Adoption strategy

Introduce complete state descriptions alongside the existing partial descriptions. Establish immutable payloads, release-build validation, target signatures, and preparation contracts before migrating consumers.

Use direct lighting as the first complete consumer: a bounded fullscreen graphics pass with a known target set. Preserve its shader input APIs, image formats, clear behavior, engine restoration, and output. Follow with composite and capture passes, then indexed-blend/MRT consumers. Terrain, tessellation, debug overlays, and engine-owned draws require separate adapters because their state and geometry contracts differ.

During coexistence, partial overrides are allowed only outside complete submissions or through a declared compatibility boundary. They cannot mutate a supposedly authoritative pipeline invisibly. Migrate framebuffer blend policy when its consumer adopts complete pipeline state.

Choose actual source names and namespace placement during implementation, preserving existing resource types and keeping each new file focused. Do not build a monolithic pipeline manager that owns preparation, pass policy, resource storage, and engine hooks.

## Validation and acceptance

### Descriptor and preparation checks

- Equal descriptions with distinct input arrays compare equal; callers cannot mutate a published description.
- Every behavior-affecting field changes identity as intended; debug labels and framebuffer dimensions do not.
- Invalid combinations reject in release as well as debug builds.
- Unsupported capabilities, vertex interface mismatches, and output format/sample mismatches reject preparation or pass binding.
- Shader reload and explicit disposal invalidate live realizations without aliasing recycled native IDs. All submission remains within one live rendering-context lifetime; replacement recovery is outside this scope.

### Headless graphics checks

- Apply a pipeline after deliberately different preceding depth, stencil, rasterizer, blend, mask, and sample state; verify identical relevant state and rendered results.
- Exercise A-to-B-to-A transitions, global/indexed blend interaction, sparse output routing, and per-output masks/equations.
- Reject draws missing required dynamic values or valid resource bindings; ensure failure emits no draw.
- Confirm clear behavior is independent of preceding write masks/scissor state and preserves unrequested aspects.
- Exercise attachment replacement, same-format resize, incompatible format/sample changes, and explicit retirement.
- Confirm changed shader inputs publish when the pipeline is unchanged.
- Verify engine shader ownership and state restoration on success and exceptions.
- Verify an unchanged managed draw avoids redundant native state calls without skipping changed resources or uniform data.

### Integration and measurements

Compare the migrated direct-lighting pass against its existing output using deterministic targets. Extend coverage as each consumer migrates. Validate engine-boundary hooks against actual mutation paths, including paths that bypass a tracked setter.

Record focused test commands and outcomes with implementation documentation. Builds/tests run through delegated test agents under repository instructions. Live Vintage Story/RenderDoc validation remains user-run; headless checks do not establish engine-wide correctness or GPU performance.

Measure native state-call counts, steady-state GL queries, CPU submission cost, pipeline preparation cost, cache growth, and representative GPU timing. Use comparable workloads; report correctness and performance separately.

Acceptance requires complete state ownership for each migrated draw, compatible targets/resources, preserved engine restoration, validated lifetime behavior, and evidence that unchanged submissions suppress redundant work. Engine-wide authority may only be claimed for the mutation paths actually covered.

## Decisions to resolve during implementation design

- The complete supported GL state inventory and explicit defaults, including framebuffer sRGB, clip conventions, restart policy, and shader-controlled point size.
- The representation of vertex layout and geometry adapters for engine `MeshRef` draws versus VGE-owned VAOs.
- Exact output compatibility rules for intentionally unused/discarded fragment outputs and sparse routing.
- How wrapped/default framebuffer metadata is supplied and refreshed without per-draw queries.
- Which existing shader lifecycle identity can represent executable generations, or where an explicit generation must be introduced.
- The restoration set for each engine boundary and whether a known-state contract can replace capture at that boundary.
- Which dynamic values, beyond viewport/scissor/reference/constant, production consumers actually need.

These bounded choices are now specified in [Graphics submission design contract](Rendering.AuthoritativePipelineState.md#graphics-submission-design-contract), including supported defaults, consumer adapters, metadata providers, executable revision and boundary policies. Runtime implementation and its validation remain scheduled in the implementation plan. Retain existing restoration behavior until the replacement path satisfies those contracts.
