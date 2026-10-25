# GPU resource metadata and lifetime

`GpuResourceManager` owns canonical metadata for the originating rendering context. Its concurrent
registry uses `GpuResourceHandle`, a resource-kind/native-width-ID value, as the key. Each entry holds
one typed metadata snapshot, availability and revisions, shared usage guards, and weak wrapper recipients.
Registration grants no native ownership. Metadata operations and native queries run on the context thread;
concurrent containers support identity lookup and recipient removal across threads.
Tracked command inputs, operation admission, deferred link submission and executable transfer validate
the originating context and thread before dispatch or metadata withdrawal. Reusing an available snapshot
does not bypass that check. Explicit operation steps recheck affinity after callbacks.

## Wrappers and initialization

`GpuResource` supplies identity, ownership, disposal and deletion scheduling. `GpuResource<TMetadata>`
stores a snapshot, exposes it read-only to derived wrappers, and assigns complete snapshots through
`HandleMetadataChanged`. Tracking, availability, revisions and recipient membership belong to the manager.
Resource properties read the supplied snapshot; allocation policy, upload strategy and selected
subresources remain with their existing wrappers and helpers.

Resource-specific initialization records expose legal inputs and automatically record assigned fields,
including explicit zero and false values. Manager creation methods validate those requests and use
StateCache to allocate and configure native objects. A nullable metadata result reports detected failure;
identity and a complete initial snapshot are handed to the wrapper only after successful initialization.
Partial allocation cleanup preserves the original exception and the originating deletion context.

## Command results and queries

StateCache owns native dispatch and the resulting metadata effects. Controlled operations publish inputs,
guaranteed defaults and derived facts. Existing failure handling remains authoritative: reported failure
does not publish success, and interruption withdraws affected knowledge. No universal post-command query,
error drain or separate requested/actual metadata cache is used. Available covered aliases reuse canonical
facts; external unknown state and explicit recovery use manager-owned readers.

Driver-produced shader compilation/link results and interfaces retain their completion checks. Allocation
uses cached target/format sample capabilities, with a single sample-count read when those capabilities
cannot supply the result. Unsized image formats may require a resolved-format read. These exceptions do
not authorize full descriptor scans after controlled allocation. Native readers choose supported named
or binding-preserving query paths internally.

An operation captures its original identity, withdraws affected availability, constructs a complete result
and commits once. Participating internal steps receive the operation explicitly. Unrelated same-resource
reentry is rejected before dispatch. Cleanup does not recover or retry mutations; recovery is explicit.
Deferred program linking retains its separate submission/completion boundary. Snapshot assignment precedes
availability and dependent notification. Selected framebuffer images project backing metadata without
another canonical entry or reader when facts are available.

## Binding knowledge and retirement

`GpuTextureRef` is a readonly consumer value holding one private `GpuResource<GpuTextureMetadata>`
reference. Implicit operators on `GpuTexture` and `GpuTextureView` supply it; borrowers use the texture
base conversion. It exposes identity and published metadata without public disposal, mutation or
underlying-resource access. Null wrappers produce an empty value. Equality compares managed wrappers;
intentional owner replacement is visible through retained references. The value adds no registration,
native allocation, captured lifetime token or metadata cache.

The manager checks usability and native-view source dependencies using existing associations and local
storage certificates. Metadata access performs no recovery. Explicit recovery remains with the producer;
unavailable facts reject active submission. Shader compatibility history examines current identity and
metadata revisions even when the reference value compares equal. Texture-reference retention does not
acquire a pass guard or prevent explicit owner disposal.

Resource validity and metadata availability do not prove that an object is bound. StateCache separately
tracks selectors, image parameters, buffer ranges and framebuffer routing. Controlled known-equal bindings
can be suppressed. Arbitrary external selectors preserve exact execution and withdraw uncertain knowledge
for lazy recovery. Fallback scopes preserve separate read/draw framebuffer selections and original VAO
element-buffer associations. Vertex helpers retain authored fields and resolve only required unknown
facts through the manager; the canonical VAO schema contains no full layout mirror.

Deletion removes the original entry and invalidates its recipients. Reusing the numeric ID creates a new
entry; old wrappers and queued work retain their original association and cannot affect the replacement.
Owner retirement withdraws usability before deferred deletion. Borrowed disposal only removes its recipient;
it never deletes external storage. Context retirement clears registrations and abandons work that cannot
run in its originating context. Weak recipients are pruned during acquisition, publication and removal.

## External coverage and consumers

Compiler-checked adapters redirect supported calls discovered in the built-in engine assemblies through
StateCache. Allowed OpenTK owners are GL (also named Core by adapters), GL.Ext and GL.Arb. Other auxiliary
owners and legacy fixed-function facilities are outside interception. Generic discovery handles concrete
value-type instantiations from built-in assembly references; arbitrary mods and dynamic callers are outside
that boundary. A registry entry does not extend interception to unsupported callers.

Cloud and depth consumers retain typed borrowers across stable selections and share existing owned material
depth directly. Their content readiness, upload/generation certificates, frame qualification and producer
selection remain separate from resource metadata. Unsupported raw sampler IDs retain conservative validation
and slot invalidation. See [shader bindings](Rendering.ShaderBindingState.md#prepared-resource-submission)
and [cloud ownership](PBR.Clouds.md#borrowed-map-contract) for consumer contracts.
