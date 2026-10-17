# PSO state relevance

Status: approved. Scope: design only; runtime application and boundary behavior remain unchanged.

## Recommendation

Derive which state can affect a draw from its existing PSO declaration. Retain complete authored
values, but apply only relevant native parameter groups through `StateCache`. An irrelevant
parameter keeps its inherited native value until a subsequent operation needs a declared value.
Do not add caller-supplied ignore flags, nullable state fields, or renderer-specific shortcuts.

A complete PSO must determine every observable effect of its draw. It need not normalize native
parameters that cannot affect that draw. Exact engine handback still includes every field the
owned work could mutate, even if that field becomes irrelevant to the last draw in the boundary.

## Current implementation

- `GraphicsPipelineDesc` validates authored values, then `PipelineCanonicalization` replaces several
  disabled parameters with defaults. Disabled blend factors, cull mode and stencil faces can lose
  their authored values before application.
- `StateCache.ApplyGraphicsState` establishes all supported parameters, including line width for
  filled triangles, a zero scissor rectangle when scissoring is disabled, and neutral blend state
  through every native output slot.
- `PipelineStateCoverage.From(GraphicsPipelineDesc)` requests all drawing fields. Its
  `CompleteGraphics` flag also expands coverage independently of the requested fields.
- `GraphicsCommandContext` validates the actual pass and geometry before application. Pass load
  operations have their own state requirements and run before drawing; a context can clear without
  any PSO. Boundary restoration already compares saved values with current cache knowledge.

These behaviors are consistent with the current
[complete graphics contract](Rendering.AuthoritativePipelineState.md). This proposal changes what
complete application means; it does not describe an optimization already implemented.

## Three separate concepts

| Concept | Meaning | Example |
|---|---|---|
| Declared value | Immutable authored intent, including resolved omissions | Line width defaults to 1; an explicit width of 2 stays 2. |
| Relevance | Whether a parameter can affect this operation | Width is dormant for filled triangles, relevant for lines. |
| Cache knowledge | Whether the shared owner knows the actual native value | A dormant inherited width may be known as 4 or remain unknown. |

Skipping an irrelevant field neither changes its cache value nor marks its declaration as applied.
When the field becomes relevant, the normal setter compares the newly selected declaration against
actual cache knowledge and queries or emits work according to the existing ownership rules.
An explicit default has exactly the same relevance rules as an explicit nondefault value.

## Derivation and ownership

Introduce one immutable derived relevance description alongside each prepared graphics pipeline.
Use field/native-call groups and indexed output masks, separate from the value records and their
validity bits. It is a derived result, not another editable source of state.

Descriptor construction provides the static dependencies: feature enables, assembly topology,
polygon mode, target formats/aspects/samples, sparse output slots and blend equations. Preparation
adds verified executable output-primitive information where needed. Vertex-only ordinary draws
use assembly topology directly. Geometry/tessellation stages can change the rasterized primitive;
use verified metadata if available, otherwise conservatively retain every affected primitive
parameter. Never infer their output primitive solely from input assembly topology or shader names.
Shader reload rebuilds preparation-dependent relevance with the existing executable realization.

The validated pass must match the declared target signature. Relevance uses that signature's
output slots, not attachment numbers or a cached arbitrary FBO route. No driver queries, settings
polling or shader source scanning are added to the draw loop.

Current dynamic state supplies viewport, scissor rectangle, stencil references and blend constant;
it does not dynamically toggle scissoring, blending, topology or polygon mode. Consequently its
values normally do not change relevance. Geometry adapters do not currently publish a common
indexed/nonindexed declaration: keep restart enable handling conservative rather than expanding
their public contract just to remove a setter. Any future dynamic selector must declare the union
of possible mutations before entry and resolve its operation mask before application.

`StateCache` remains the sole native state owner. The same resolved groups drive application,
boundary authority validation, and potential restoration coverage. Legacy partial `GlPipelineDesc`
overrides keep their explicit mutation semantics; do not reinterpret them as complete draws.

## Dependency rules

| State | Proposed relevance and required behavior |
|---|---|
| Line width, line smoothing and compatibility line stipple | Relevant when rasterization may produce lines, including polygon mode Line. Stipple parameters require stippling enabled; its enable is established when lines can occur. Filled triangles do not touch these values. |
| Fixed point size | Relevant when points can occur and program point size is disabled. Establish program-point-size enable when points can occur. Point sprite origin and supported point smoothing remain conservative for point draws. |
| Polygon modes and polygon-only controls | Establish polygon mode when polygon primitives can occur. Polygon smoothing/stipple and offset enables are applied for their applicable primitive kind; offset factor/units only when an applicable offset is enabled. Unknown emitted primitive kind retains all applicable groups. |
| Culling | Establish cull enable for polygon draws and cull mode when enabled. Keep front-face orientation for polygon draws even with culling disabled: fragment facing and two-sided stencil can observe it. Do not derive its relevance from cull enable alone. |
| Scissor | Establish enable for raster draws. Rectangle is relevant only when enabled. A disabled scissor draw leaves the inherited rectangle intact. |
| Depth | With a depth aspect, establish test enable; comparison and write mask are relevant when the test is enabled. Without a depth aspect these draw-only fields are dormant. Retain depth range and clip conventions conservatively: shader-visible fragment depth and clipping are not determined solely by the presence of a depth attachment. |
| Stencil | With a stencil aspect, establish test enable; functions, references, operations and write masks are relevant when enabled. Keep both faces conservative. A stencil clear independently requires write masks even with the draw test disabled. |
| Color write masks | Relevant for each routed color output, regardless of blend enable. Sparse holes and trailing unused slots are dormant. Do not infer irrelevance from missing fragment output reflection or a particular input's alpha. |
| Blending | Establish indexed blend enable for each routed blendable output. Establish equations only for enabled blending; factors matter only for equation channels that use factors. Integer attachments bypass blending. Unrouted outputs need no neutralization. |
| Blend constant | Relevant when an enabled routed blend equation actually uses a constant factor. MIN/MAX ignore factors. Keep channel analysis conservative where a factor depends on another channel. |
| Sampling | Coverage value/inversion require coverage enabled; mask words require mask enabled; minimum shading requires sample shading enabled. Retain feature enables conservatively, including for single-sample targets; target sample count alone is not a universal proof that a feature cannot affect fragment execution. |
| Assembly | Restart index requires programmable restart enabled and fixed-index restart disabled. Keep restart enable application conservative across geometry adapters. Patch vertex count requires patch topology. |
| Other state | Retain existing application unless a specific dependency proves irrelevance. In particular preserve viewport, clip-control conventions, custom clip-distance disabling, provoking-vertex policy, rasterizer discard, framebuffer sRGB, dithering and logic-op disable. Do not optimize discard-only pipelines or speculate about shader use of flat values here. |

Enable bits themselves are not automatically relevant: an inherited line-smoothing enable cannot
affect filled triangles. Conversely, skipping a dormant parameter must never skip disabling a
feature that could affect the current draw. For example, routed unblended color must disable
inherited blending, and an unclipped raster draw must disable inherited scissoring.

Native setters can update several logical fields at once. `BlendFuncSeparatei` updates RGB and
alpha factors together; if either channel needs a change, submit all four declared factors and
cover/restore the complete native factor group. The same rule applies to combined equation,
stencil and polygon setters. For contexts without independent blending, global factor/equation
writes affect all native slots: declare that aliasing coverage and retain existing compatibility
validation. Do not pretend a global write touched only the routed slots.

## Authored values, validation and identity

Preserve authored dormant values in `GraphicsPipelineDesc`. Remove destructive inactive-value
canonicalization; retain lossless/default representation normalization, such as equivalent stencil
mask widths and trailing all-ones sample-mask words where semantically valid. Validate authored
enums, finite numbers, ranges and supported configurations before relevance derivation, including
dormant settings. Relevance is not an escape hatch for invalid descriptions.

Use structural authored identity for equality/hashing, excluding labels as today. Two declarations
with different dormant widths or disabled blend factors remain distinct even if their current
draws produce the same pixels. This deliberately favors predictable retained intent over maximal
pipeline-cache deduplication. Derived relevance is deterministic metadata, not an independently
authored key, and native resource IDs/dimensions remain outside descriptor identity.

Keep the explicit dynamic-value contract: a declared dynamic slot still requires a valid supplied
value even when dormant. An undeclared scissor, stencil reference or blend constant continues to
resolve to its existing concrete default. Do not emit those defaults while irrelevant; if relevant,
establish them explicitly. Extra dormant values do not become ambient inherited intent.

Example: a filled-triangle PSO with width 2 leaves inherited width 4 untouched. A subsequent line
PSO with width 2 establishes 2. Another triangle draw leaves 2 untouched. Boundary cleanup restores
the original 4 because a line draw mutated it, regardless of the final triangle's relevance.

## Boundaries and clear operations

Separate complete-draw admission from unconditional all-field expansion in `PipelineStateCoverage`.
Completeness means validated operation ownership; field coverage is the potential native mutation
set. A complete boundary may therefore have sparse fixed-function coverage.

At entry, union the prepared pipelines' potential native write groups, existing resource/binding
footprints, and a bounded clear-operation envelope. Capture the incoming value of each covered
group before any mutation, using known cache values where possible. Keep the current entry API:
because pass descriptions are supplied later, conservatively include the shared clear helper's
potential groups even if a particular callback ultimately uses only Preserve passes. This may
retain some capture work; it must not cause setters for unused clear state.

The clear envelope includes the state actually written by `RenderPassOperations.Load`: rasterizer
discard, framebuffer sRGB, dither, scissor enable/rectangle, indexed color masks, depth write mask
and front/back stencil write masks, with global-alias expansion where necessary. Typed clear
values remain operation inputs, not PSO identity. Preserve/Discard-only loads issue no clear-state
application. Clear-only contexts use the helper envelope without pretending they have a raster
PSO. Existing other helper operations keep their own explicit mutation coverage.

A depth clear must enable depth writes even when the following draw disables depth testing.
A region clear must establish its own scissor even when the following draw disables scissoring.
Draw application then establishes the relevant draw state; it cannot rely on clear policy matching
the PSO. [Khronos documents clear write-mask and scissor dependencies separately from draw tests.](https://wikis.khronos.org/opengl/Framebuffer)

Retain the existing saved-value/current-value restoration comparison; a new renderer-local dirty
tracker is unnecessary. Untouched covered state remains equal and emits no restoration setter.
Where grouped coverage/knowledge currently cannot distinguish a dormant field, refine the shared
native group rather than invalidating a whole category. On setter failure, conservatively invalidate
the affected group and attempt its restoration; continue independent cleanup and propagate fatal
handoff failure as today. Restore covered enable bits and parameters even when the incoming feature
was disabled: exact engine handback includes dormant values that owned work changed.

## Expected examples and acceptance

- Fullscreen sky/cloud passes: no line-width/point-size setters, no disabled scissor-rectangle
  normalization, no blend factor/equation writes for disabled or unrouted outputs. Real routed blend
  transitions and per-pass framebuffer/shader transitions remain.
- Point resolve: program point size disabled means its declared fixed point size is applied even
  after a fullscreen pass. Program point size enabled leaves fixed point size dormant.
- Sparse MRT: enable/mask state is established only for routed outputs, with factors for enabled
  blending. A later pass routing a previously dormant slot establishes that slot before drawing.
- Partial clear followed by an unscissored draw: clear region is respected, draw is unclipped, and
  both inherited scissor enable and rectangle return exactly at boundary exit.

The implementation task must verify these transitions in focused GPU fixtures with hostile inherited
state, exact state handback and matched output comparisons. Include filled-to-wireframe/line/point,
unknown shader output primitives, dormant-to-active authored values, mixed indexed/global blending,
MIN/MAX, stencil/depth clears, clear-only and Preserve-only contexts, invalid declarations, reload,
skipped draws and failures. Verify declaration equality, unchanged cache knowledge for skipped fields,
and native-call-group coverage. Existing tests demanding unconditional dormant defaults must be
replaced with observable draw correctness and exact inherited restoration, not simply deleted.

Measured setter/restoration reductions establish reduced native work; they do not establish GPU
time or frame-rate gains. At implementation, reconcile the complete-graphics, canonicalization,
coverage and clear-only descriptions in `Rendering.AuthoritativePipelineState.md` with these
semantics. This proposal creates no separate implementation checklist and authorizes no runtime
change by itself. The approved design governs the separate implementation task.
