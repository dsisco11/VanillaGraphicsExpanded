# Unified texture references

Status: approved with the simplified consumer-only reference contract below. Implements the design portion of
[Phase 7](Rendering.GpuResourceHandles.todo). The existing
[resource contract](Rendering.GpuResources.md) and
[binding contract](Rendering.ShaderBindingState.md#prepared-resource-submission) remain authoritative.

## Proposed API

Use a `readonly struct GpuTextureRef` as the common input for texture binding and validation.
It retains one `GpuResource<GpuTextureMetadata>` reference, the common base of `GpuTexture`
(including `BorrowedTexture`) and `GpuTextureView`, with no native allocation, metadata discovery,
subscription or ownership transfer. Its only stored state is the private typed resource reference;
it is not another resource subclass. Identity and published metadata use
this common contract rather than separate owned-texture and native-view representations.

| Surface | Proposed contract |
| --- | --- |
| Implicit conversion from `GpuTexture?` / `GpuTextureView?` | Operators declared on the respective wrapper classes; query-free construction, with null producing the default empty reference. `BorrowedTexture` uses the `GpuTexture` conversion. |
| `IsEmpty` | True only when no wrapper was supplied. A retired reference is not empty. |
| `IsValid` | Delegates resource-usability checks to `GpuResourceManager`; does not acquire metadata or implement view-specific rules. |
| `TextureId`, `Target` | Read the current validated referenced object; reject an empty or retired reference. Target additionally requires available metadata. |
| `TryGetMetadata(out GpuTextureMetadata)` | Returns already-published facts only; false for empty, retired or unavailable input. No recovery or native queries. |
| Equality / hashing | Managed wrapper reference identity only. Independent aliases need not compare equal. Neither numeric ID nor metadata values define equality. |

Prefer this value representation over a new heap wrapper or public interface hierarchy: copies retain
the same managed object, ordinary use allocates no reference object, and existing inheritance stays intact.
Wrapper-owned operators call an internal `GpuTextureRef(GpuResource<GpuTextureMetadata>?)` constructor.
The reference type needs no concrete-wrapper conversion branches or public `From` factory. There is
no reverse conversion to the underlying resource. Callers can write `GpuTextureRef input = texture;`
or supply the wrapper directly to a parameter accepting `GpuTextureRef`.
There is no public underlying-resource accessor, `Dispose`, `Detach`, upload or resize API on the reference. Dropping the value releases only
its managed reference; normal wrapper ownership/finalization rules continue to apply. Holding it does
not prevent explicit disposal by the owner and does not acquire a render-pass usage guard.

## Identity and metadata

`GpuResourceManager` owns the complete resource-usability decision. Consolidate the checks currently
split between `GpuTextureView.IsValid` and `IsTextureViewSourceCurrent`: view metadata availability,
source validity, original source association, native ID, storage generation and context-local storage
identity. Retain the existing context/thread and retirement requirements. Existing wrapper validity
accessors and `GpuTextureRef` use this same manager path; do not make the manager call back into a
wrapper validity accessor that delegates to it.

The manager also supplies already-published metadata through the typed base contract. Neither
`GpuTextureRef` nor `GpuResource<TMetadata>` gains dependency validation, tracking state or native
queries. Resource-specific dependency checks and local-tool handling remain inside the manager.
Shader compatibility, selected-image bounds and active-pass feedback checks remain with their existing
consumer validators, using manager-certified resource facts; they are distinct from resource usability.

The value follows its retained wrapper's current native object. Intentional native-object replacement
by an owning wrapper is visible through existing references; replacing the managed wrapper itself
requires selecting a new reference. Construction may precede realization, but use requires the manager
to establish usability. Equality remains stable when the same wrapper changes its native allocation.

The manager preserves original-association retirement for borrowers and source-storage dependency
checks for native views. Following an owner's intentional replacement does not allow a retired borrower
to adopt a reused numeric ID or an old native view to follow replacement source storage. No registration
token, storage identity, metadata snapshot, revision or availability flag is captured by GpuTextureRef.
Context-local handling also stays in the manager, preserving its existing limitations around unobserved
external changes without adding another lifetime system.

Unavailable metadata rejects active use. Recovery remains explicit in the existing producer/manager
path; references expose usable facts again after successful recovery. Deletion, Detach, pending retirement
and context retirement retain existing wrapper/manager semantics. Native-view source checks remain in
force even where OpenGL could keep the shared storage alive. Context/thread checks precede publication.

Shader compatibility history must inspect current native identity, metadata and manager revisions on use;
GpuTextureRef equality alone must never suppress lifetime checks or revalidation after owner replacement.
StateCache independently compares actual binding state. The reference is an API boundary against accidental
lifecycle operations by consumers, not a security boundary or an additional owner.
## Binding and selection

Sampler submission accepts `GpuTextureRef`, including arrays. Required empty inputs fail; optional
empty inputs clear their slots; inactive inputs remain skipped. Preserve the existing required/optional
policy for invalid allocations, while never treating unavailable metadata as a valid cached input.
Validate the complete active input set before publishing it.

Change `GpuTextureBinding.Texture` to `GpuTextureRef`. Keep its explicit image access, mip level,
layered flag, layer and format. Framebuffer texture factories accept the same reference while keeping
mip/layer/cube-face/multiview selection separate. Renderbuffer attachments keep their existing route.

All selections use the referenced object's coordinates: level zero of a native view is its first
exposed mip, and layer zero is its first exposed layer. Use published per-level image facts for bounds
and format checks. Map selections back to shared storage only for the existing feedback/alias checks;
never add the source offset to the native view's GL binding arguments.

Retain `GpuPreparedBindings` as supplied-program-metadata preparation only. Unify resource input handling
in `ShaderPreparedSubmission` and `ShaderInputValidation`, using their existing compatibility history.
Preserve render-pass usage guards and shared-storage feedback detection across owned, borrowed and
view inputs, including differently named aliases of the same storage. StateCache remains the single
binding cache. Unknown raw-ID inputs remain at their conservative external boundary; creating a reference
does not establish interception coverage.

## Examples and migration

| Existing input | Consumer behavior |
| --- | --- |
| Owned material depth | Assign `GpuTextureRef input = depth` through the wrapper's implicit conversion. No borrowed wrapper is constructed. The reference follows native-object replacement by that owner; select a new reference if the owner wrapper changes. |
| Mutable engine cloud map | Retain `BorrowedTexture` through the existing producer selection logic, then expose its reference. Metadata changes reuse it; producer replacement selects a new borrower/reference. |
| `GpuTextureView` exposing source mips 2–4 | Reference the native view itself. Image level zero selects source mip 2. Source replacement invalidates the view under existing dependency rules. |
| Unavailable facts after failed mutation | Submission rejects reuse. Explicit manager recovery restores usability under the retained wrapper's existing lifecycle rules. |
| Two aliases used for sampling and rendering | Resolve their existing backing-storage relationship and apply current pass feedback rules; different reference equality does not imply independent storage. |

Implementation touches these boundaries:

- `GpuResourceManager`: provide common typed-resource identity, usability and published-metadata
  access; consolidate remaining wrapper-owned native-view dependency validation, including local tools.
- `GpuTexture`, `GpuTextureView`: declare their implicit conversion operators; `BorrowedTexture`
  uses the conversion on its `GpuTexture` base. Both operators use the internal typed-base constructor;
  preserve their allocation, mutation and disposal APIs. `GpuTextureMetadata` remains canonical and
  `GpuImageMetadata` remains the selected framebuffer projection.
- `GpuTextureBinding`, `GpuFramebufferAttachment` and render-pass selection/validation: consume
  references and retain explicit selections and existing ownership of attachment-created storage.
- `ShaderInputValidation`, `ShaderPreparedSubmission`, `ShaderContractGenerator` (including
  `RuntimeSubmissionEmitter`), generated desired-state equality and sampler arrays: recognize the
  value contract and read current native identity/metadata even when reference equality is unchanged, without recurring array allocation.
- Cloud, depth and postprocessing consumers: expose current references at binding boundaries;
  keep content certificates, readiness, frame qualification and producer selection where they are.

Place the value and its contract in `Rendering/GpuTextureRef.cs`; manager identity, usability and
metadata access belong in focused manager partials. Any resource-specific dependency dispatch belongs
inside the manager, not in the reference, generic base or consumers. Do not create another metadata hierarchy.

## Verification and approval

Verify owned, borrowed and native-view sampler/image/attachment equivalence; empty and incompatible
inputs; view-relative bounds; unavailable metadata and explicit recovery; replacement, deletion/name
reuse, Detach and context retirement; source dependency and shared-storage feedback checks. Verify
the common typed-base path applies native-view dependency checks and wrapper accessors agree with it,
including context-local cases, without recursive validity calls. Verify owner replacement through a retained
reference revalidates compatibility, and that no public reference API exposes lifecycle methods or the underlying resource. Verify
reference retention neither transfers ownership nor adds a native object, query or bind. Preserve
conversion coverage for owned and borrowed derived wrappers, native views and typed nulls, with no
reverse conversion or public resource accessor. Preserve
stable-binding suppression, surrounding state and cloud/depth outputs. Delegate affected regression
tests and canonical package verification, then perform second review and independent completion audit.

The approved design uses a readonly value holding one private GpuResource<GpuTextureMetadata> reference,
manager-owned usability validation and strong managed retention. It follows the wrapper's current native
object, exposes no public lifecycle methods or underlying-resource accessor, and adds no captured identity
or tracking machinery. The subsequent GpuCommand design consumes the implemented reference API.
