# Engine-boundary restoration through pipeline state and StateCache

Status: approved by the user on 2026-10-05. Boundary inventory and implementation contracts are
complete. Categorized storage/cache-backed coverage, declared boundary entry and resolved snapshots
and scoped restoration/cleanup are implemented. Refraction and independent lighting/composite callbacks
are integrated and headless-tested. Caller reconciliation is complete; 11 source invocations remain
with explicit parent-plan prerequisites, including one dormant helper. User live acceptance was
received on 2026-10-05 after the startup registration correction; the bounded plan is complete. Evidence:
[Rendering.AuthoritativePipelineState.md](Rendering.AuthoritativePipelineState.md#engine-boundary-restoration).

Implementation plan: [Rendering.EngineBoundaryRestoration.todo](Rendering.EngineBoundaryRestoration.todo).

This proposal refines the engine-restoration mechanism in the approved
[authoritative graphics pipeline proposal](Rendering.AuthoritativePipelineState.Proposal.md)
and its [implementation plan](Rendering.AuthoritativePipelineState.todo). It approves a bounded
change in implementation order to address refraction capture before the complete graphics
submission system is available. It does not replace that architecture or mark its work complete.

## Problem and evidence

VGE interrupts engine rendering to capture world lighting before the local first-person draw.
The capture invokes the existing direct-lighting and composite passes. All three methods use
`CaptureLegacyFixedFunctionState()`, and all three establish equivalent fullscreen pipeline state:

- [WaterRefractionCapture.Capture](../VanillaGraphicsExpanded/PBR/Liquids/WaterRefractionCapture.cs)
- [DirectLightingRenderer.RenderLighting](../VanillaGraphicsExpanded/PBR/DirectLightingRenderer.cs)
- [PBRCompositeRenderer.RenderComposite](../VanillaGraphicsExpanded/PBR/PBRCompositeRenderer.cs)

The legacy scope queries global state and restores global blending. It cannot preserve the
different blend settings of individual draw outputs. Returning from capture consequently
overwrites the G-buffer's indexed blend policy.

The supplied `refraction-cutoff.rdc` capture was replayed during investigation. Before capture,
event 5989 had blending enabled on outputs 0–2 and disabled on outputs 3–7. At first-person
draws 6244 and 6343, blending was enabled on all eight outputs. The first-person shader writes
a negative normal-alpha marker, but 81,320 pixels in the final normal texture had alpha 5.
The final refraction publication contained held-item color and visibility depth where the
pre-overlay capture contained world color and depth. These observations establish a restoration
defect; they do not establish that every reported water artifact has the same cause.

Inspection of the installed engine IL also establishes a real restoration requirement:
`SystemRenderEntities.OnRenderOpaque3D` configures depth testing and blending before invoking
the player renderer. The interrupted first-person path does not reestablish all state changed
by our fullscreen passes. Removing preservation without a replacement would leave those draws
with depth testing and depth writes disabled.

The UV-only refraction shader's abrupt offscreen-to-undistorted fallback is a separate suspected
cause of the screen-bottom cutoff. That hypothesis was not experimentally isolated. Changes to
UV fallback, refraction algorithms, or aesthetic fading are outside this proposal.

## Architectural contract

VGE renderers declare drawing state through the PSO system. They do not query, save, or restore
individual native state fields. The engine continues to use its existing APIs, with supported
operations routed through StateCache by the existing engine adapters.

Three responsibilities remain separate:

| Owner | Responsibility |
| --- | --- |
| Pipeline descriptions | Declare the state a VGE draw requires. Current partial descriptors remain compatibility representations until complete pipelines are adopted. |
| StateCache | Maintain known native state; apply transitions; resolve unknown incoming values; preserve and restore declared boundary state through the same transition backend. |
| Engine boundary adapter | Define the interrupted engine contract, establish the restoration boundary, and coordinate existing shader/resource-binding scopes. |
| Future GraphicsCommandContext | Coordinate pass submission and explicit external boundaries, using the same cache restoration mechanism. |

There must be no renderer-specific restore PSO, hand-coded GL restoration sequence, second
native-state cache, or separate shader activation implementation. The boundary adapter expresses
policy; StateCache owns the mechanism. All drawing-state mutations in VGE passes use pipeline
application or declared dynamic state. Native setters remain backend/integration operations.

## Categorized state representation

Separate all currently tracked fixed-function pipeline values into categorized structs before
building boundary snapshots. This includes fields spread across StateCache partial files, such
as patch control-point count and provoking-vertex convention, as well as the dynamic viewport
coverage added here. It is an explicit architectural prerequisite, not optional file cleanup.

Use focused data categories such as depth, output blending/write masks, rasterization, primitive
assembly and dynamic state. Names such as `DepthState`, `BlendState`, `RasterizerState`,
`PrimitiveAssemblyState` and `DynamicState` are illustrative. Inventory assigns every existing
field to exactly one category and settles the static/dynamic policy for line width, point size
and related values against the approved PSO architecture. Do not add unsupported stencil,
sampling or other future state merely to populate a category.

Keep state values separate from cache knowledge. Category value structs contain concrete values;
StateCache retains corresponding per-field validity metadata, including per-output validity for
indexed state. Partial knowledge must remain representable: knowing a depth comparison does not
imply that depth-test enable or write mask is known. Invalidation withdraws knowledge without
turning unknown fields into native defaults. Unknown is neither an authored PSO value nor a valid
captured value for any field included in a successfully established boundary.

StateCache remains the single owner of native transitions, queries, alias handling, invalidation
and restoration. Category structs contain data; they do not issue GL calls or become independent
caches. Migrate existing readers, setters, engine adapters and invalidation paths to the categorized
storage together, without parallel authoritative copies of the old fields.

Reuse category value representations in boundary snapshots and future pipeline descriptions where
their semantics match. A snapshot retains explicit coverage and concrete resolved values; it does
not copy cache-validity metadata into a PSO or acquire complete-pipeline identity. Indexed payloads
must not expose mutable cache storage: snapshots and published descriptor values remain independent
of subsequent cache updates, including when a struct contains reference-backed storage.

Resource-binding caches are excluded from this representation change. Texture, sampler, buffer,
VAO, framebuffer and program bindings retain their existing owners, associations and retirement
rules. Do not fold them into fixed-function category structs or duplicate their tracking.

## Proposed boundary mechanism

Add a cache-owned restoration scope for an explicitly described engine boundary. Names such as
`EngineBoundaryState`, `PipelineStateSnapshot`, and `BeginEngineBoundary` are illustrative API
spellings, not additional resource hierarchies.

Conceptual use inside the refraction boundary adapter:

```text
BeginEngineBoundary(refractionCaptureContract)
    Preserve framebuffer bindings through the existing framebuffer scope
    Evaluate direct lighting using its pipeline and shader activation
    Evaluate composite using its pipeline and shader activation
    Restore scoped shader ownership and resource bindings
EndEngineBoundary()
```

The pipeline state scope covers the complete interruption, including setup and cleanup that
can change covered state. It does not surround every individual PSO application. Consecutive
VGE passes establish their own state directly; intermediate restoration is unnecessary.

### Boundary declaration and entry

The declaration identifies the state the operation may change and the incoming state the engine
requires preserved. Its effective preservation set includes every native field those operations
can affect, including indirect effects of global operations. It covers setup, clears, draw helpers,
and cleanup, not just the two main draw descriptors.

Derive pipeline-related coverage from the existing descriptors and dynamic-state declarations
where possible. Add only explicitly documented adapter/helper effects. Do not maintain a second
handwritten list of pipeline values in the renderer. At minimum, validate that every managed
mutation falls within the declared boundary coverage before issuing it.

At entry, retain resolved incoming values for that coverage:

- Reuse cache values whose knowledge is valid for the current GL context.
- Query only unknown required values, at the boundary before drawing-state mutation begins.
- A known engine contract may replace queries only where actual entry paths prove its values.
- Do not substitute defaults for unknown state or silently swallow failed capture queries.
- If entry cannot establish the required restoration contract, do not start the optional VGE
  operation. Refraction capture remains unpublished and the engine continues with unchanged state.

Saving the previous pipeline object is insufficient. Current descriptors are partial, and the
engine does not have a pipeline object representing its effective native state. Store resolved
values in an internal snapshot separate from authored descriptors and pipeline identity.

### Application and restoration

During the operation, normal pipeline application remains authoritative for each VGE draw.
Boundary preservation does not authorize inheriting missing state in future complete pipelines.

On exit, restore saved values through the same cache transition backend used by pipeline and
dynamic-state application. Compare against known current values and suppress redundant native
calls. If current knowledge has been invalidated, reestablish the saved values without inventing
knowledge or requiring steady-state draw-time queries. Invalidation must not erase the saved
incoming snapshot.

Restore only the effective preservation set. Do not call `InvalidateAll()` merely to force a
restore. Successful transitions update the cache; failures leave affected knowledge unknown and
must be surfaced through the boundary's failure policy. Entry failure and restoration failure
are different: a failed restore cannot promise the engine is safe to continue unchanged.

Scope disposal must be exception-safe and occur exactly once. Start with non-nested engine
boundaries and reject accidental nesting. Supporting nested boundary restoration would require
its own explicit semantics and evidence; it is not needed for the refraction chain. This also
preserves the approved design's initial rejection of nested graphics passes.

### Global and indexed state

Model effective blend state per draw-output index. A cached uniform/global value can be a fast
path only when all affected indices are known to agree. It is not a separate native value to
restore after the indexed values.

A global blend enable or factor mutation affects every supported draw-buffer index. Before such
an operation, the boundary must preserve that complete affected set, not merely the attachments
of the temporary framebuffer. Query the context's draw-buffer limit through cached capabilities;
do not hard-code eight or confuse output indices with attachment numbers.

Restore differing indexed values individually, or use a proven uniform baseline followed by
indexed differences. In either case, subsequent cached operations must agree with the resulting
native state. Global mutations must update or invalidate all overlapping indexed knowledge;
indexed mutations must invalidate any assertion of a uniform global value.

Apply the same affected-state rule to write masks and other global/indexed operations as their
coverage is introduced. The complete PSO refactor already requires per-output blend equations,
factors, enables, and write masks. The immediate change must cover every alias it can actually
overwrite; unsupported incoming state cannot be silently flattened.

## Dynamic state, bindings, and shader ownership

Viewport is currently changed directly by `GpuFramebuffer.BindWithViewport()` and restored by
the legacy scope's optional viewport handling. Add cache-backed viewport application and engine
observation for the supported call signatures. The compatibility helper delegates to that owner.
Future migrated draws supply viewport through `DynamicDrawState` and pass setup; viewport does
not enter static pipeline identity. Scissor rectangles and other dynamics follow the approved
dynamic-state contract when required by the migrated consumer.

Reuse existing resource-binding scopes where they already satisfy the boundary:

- Framebuffer scope preserves independent read/draw bindings. It does not preserve viewport,
  output routing mutations, or pipeline state.
- Shader `UseScope()` preserves engine-aware activation and `CurrentShaderProgram`. A raw program
  bind is not a substitute. Resource inputs continue through existing prepared-binding APIs.
- Other binding effects of draw helpers must be inventoried and explicitly preserved, reestablished
  by a verified engine contract, or excluded from the supported boundary.

The adapter owns cleanup ordering. Restore shader ownership and borrowed bindings before the
final pipeline-state restoration where their cleanup can mutate covered state. Framebuffer
rebinding alone must not be treated as reapplication of a framebuffer-specific blend policy.

## Authority, invalidation, and lifetime

Tie snapshots to the active GL context identity/generation. Do not restore a snapshot into a
replacement context. On context loss or replacement, discard obsolete cache knowledge and use
the established recovery boundary; native names and old state cannot be assumed valid.

The existing engine-call mappings remain the observation/integration mechanism. Their coverage
does not establish authority over arbitrary other mods or raw GL calls. Unknown external
operations must use a declared external boundary, end/suspend managed authority, and invalidate
the affected categories. An untracked mutation cannot be recovered by an automatic first-write
journal that never observed it.

An unexpected managed mutation outside the declared coverage fails before the native operation.
An operation requiring additional state must expand and validate its contract before execution.
Keep restoration bookkeeping out of PSO keys, shader executable identity, and resource ownership.

## Refraction integration and compatibility

Use the pre-overlay hook as the initial engine boundary. Inventory its complete setup/draw/cleanup
effects and implement only the state coverage needed to make that boundary correct.

Remove the outer duplicate `CapturePipeline` setup once direct lighting and composite own their
required state. Execute their capture work inside the single boundary, without nested legacy
snapshots. Their independently registered engine callbacks still need appropriate boundary
adapters; removing preservation from shared implementations must not break those entry points.

Retain framebuffer and shader scopes that carry distinct necessary contracts. Review the local
blanket invalidations individually after tracked mutation coverage is established. Do not assume
that replacing the legacy snapshot proves every invalidation unnecessary.

Inventory every remaining `CaptureLegacyFixedFunctionState()` caller before changing it. Record
whether it needs an engine boundary, belongs within an existing boundary, or no longer changes
state requiring preservation. Migrate proven callers and retire the helper only when no necessary
caller remains. A repository-wide deletion is not part of the immediate correction.

## Relationship to the approved PSO work

This mechanism implements the approved proposal's engine-boundary restoration responsibility.
It does not supply complete descriptors, target compatibility, prepared graphics pipelines, or
authoritative draw submission by itself.

Bringing the shared cache mechanism and refraction adapter forward is an approved sequencing
exception to the existing implementation plan. It requires the affected boundary inventory,
context policy, state coverage and validation first; it does not bypass those prerequisites or
complete their wider scope. The exception is recorded in the linked implementation plan.

As complete submission is adopted, `GraphicsCommandContext` coordinates the adapter, sequential
passes, and external handoff. The cache restoration implementation survives; renderer-specific
compatibility wiring is removed. Full pipelines remain authoritative within VGE. Partial overrides
remain explicitly identified compatibility operations outside complete submissions or at declared
boundaries. Framebuffer blend policy moves into complete pipeline descriptions as its consumers
migrate, preserving one owner of that policy. Viewport remains dynamic.

The [current caller dispositions](Rendering.AuthoritativePipelineState.md#legacy-scope-inventory-and-disposition)
assign lighting callback wiring to its parent consumer migration and composite/capture coordination
to theirs. GraphicsCommandContext reuses EngineBoundaryExecution/EngineBoundaryScope and ordered
existing-owner cleanup. FullscreenBoundary remains until its final caller has equivalent submission
coverage. Debug, bake, upload/resolve and water-volume consumers require their own adapters and
validation; legacy helper retirement follows the last proven caller migration or removal.

## Source organization

Keep native knowledge, transition application and snapshot mechanics within the existing
StateCache implementation, split into focused partial files where needed. Boundary declarations
and adapters belong in a dedicated rendering integration directory/namespace. Dynamic value
types and PSO coverage metadata belong with their respective pipeline responsibilities.
Place each category value type in its own responsibility-focused file within the pipeline/state
domain; keep category validity bookkeeping with StateCache. Grouping fields into partial files
alone does not satisfy the categorized-struct requirement.

Use responsibility-based type names without new Gl/GL/Gpu prefixes. Keep public composition
entry points thin. Do not combine state querying, engine policy, pipeline preparation, shader
binding and resource allocation in one boundary manager.

## Verification and acceptance

All build and test execution is delegated to subagents under repository instructions. No game
launch is required or authorized by this proposal. Implementation acceptance requires:

1. Cache/native agreement for global-to-indexed and indexed-to-global transitions, including
   mixed blend enables, factors and affected write masks. Assert actual GL state and rendered
   output, not only cache fields.
2. Entry with known, partially unknown and invalidated state; queries only for missing incoming
   coverage at the boundary. Verify an unchanged warm operation avoids unnecessary queries and
   native restoration calls. Measure these counts rather than claiming a performance improvement.
3. Declaration coverage checks, snapshot independence from later invalidation, context mismatch
   rejection, non-nesting enforcement and exactly-once cleanup.
4. Success, setup failure, shader failure and thrown draw paths. No optional draw begins after
   unsuccessful entry; supported failure paths restore native state and shader ownership.
5. Refraction capture integration with engine-like depth state and mixed indexed blending:
   outputs 0–2 enabled and metadata outputs disabled. Follow capture with real marker-writing
   draws and verify that negative first-person markers survive and the clean world pair is used
   in final refraction publication.
6. Ordinary direct-lighting/composite callbacks, isolated capture, viewport changes, independent
   read/draw bindings, resize, reload and disabled-refraction behavior. Check output equivalence
   and unchanged target ownership.
7. Review against the approved architecture and every affected entry point before removing old
   scopes. Update linked plans and implementation evidence without marking unrelated work complete.
8. Complete mapping of existing fixed-function fields to categorized storage, with no duplicate
   authoritative fields. Verify independent per-field/index knowledge, selective invalidation,
   unchanged native behavior and call suppression, and snapshot independence from mutable indexed
   payloads. Resource-binding associations and retirement behavior must remain unchanged.

User-supplied live rendering or a new RenderDoc capture must separately confirm first-person
appearance and refraction-source cleanliness. Existing capture evidence establishes the defect,
not the success of a future implementation. The screen-bottom UV cutoff remains a separate
investigation even if indexed-state restoration passes every check.
