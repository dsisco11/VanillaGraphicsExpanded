# Bounded depth-sorted transparency

Status: proposal for later review. Implementation is not approved. The approved immediate
change is RGBA32F storage for the existing three weighted accumulation buckets; that change
retains the current transparency algorithm.

## Problem and intended result

The installed transparent shader distributes each fragment into three overlapping depth-weighted
buckets. Each bucket adds premultiplied RGB and weighted alpha; separate revealage tracks coverage.
Resolve divides accumulated RGB by accumulated alpha and combines the bucket estimates.
This is approximate depth ordering. Its unnormalized sums can overflow even when their final
normalized color would fit. Wider accumulation storage addresses that intermediate failure,
but does not recover individual surface ordering or colored transmittance.

Replace this representation with a bounded depth-sorted sample set per pixel. Preserve individual
depth, premultiplied lighting and RGB transmittance while capacity permits, then merge excess
samples through front-to-back color/transmittance composition. Resolve the resulting set over
the completed opaque background before camera metering and display conversion.

This is bounded multi-layer alpha blending, not an unlimited exact fragment list. It remains
approximate once samples merge, and later insertion within an already merged depth interval
cannot reconstruct the original surfaces. Submission order may affect that overflow approximation.

## Proposed representation and composition

Start with capacity four samples per pixel; evaluate bounded capacities up to sixteen only if
their additional memory and insertion cost justify a quality setting. Each contains positive finite view-space depth in
metres, nonnegative premultiplied scene lighting, and RGB transmittance in [0,1]. Maintain a
per-pixel sample count. Define deterministic equal-depth handling before implementation.

For ordinary alpha transparency, emitted sample color is `C=alpha*sourceColor` and transmittance
is `T=vec3(1-alpha)`. A material with colored transmission instead supplies its own RGB T and
compatible C. This defines a transport interface; existing water contributions must be mapped
to it explicitly rather than assigning their coverage alpha a new meaning.

Insertion orders samples by depth. If capacity is exhausted, combine the remaining farther
sample with the last retained sample using:

```
Cmerged = Cnear + Tnear * Cfar
Tmerged = Tnear * Tfar
```

Resolve front to back with initial `C=0, T=1`. For each retained sample, add `T*sample.C`, then
multiply T by `sample.T`. The final scene is `C + T*opaqueBackground`. All operations occur in
linear lighting units. Preserve the photometric proposal's frozen frame scale where implemented;
transmittance, depth and count are dimensionless or geometric metadata and are never pre-exposed.

For ordinary passive alpha layers these operations avoid an overdraw-dependent unnormalized
color sum. Additive materials, emission, nonfinite inputs and oversized individual radiance still
need explicit range contracts; the algorithm does not make every HDR source safe automatically.

## Storage and synchronization

Evaluate compact samples containing FP32 depth and two packed R11/G11/B10 unsigned floating-point
words for color and transmittance, held in integer image storage with shader packing/unpacking.
That is 12 bytes per sample, plus a 4-byte sample count: four samples require
52 logical bytes per pixel. The current three RGBA32F accumulation layers require 48 bytes per
pixel; the compact sample set alone is therefore slightly larger. This comparison excludes
revealage, glow, temporary targets, alignment and driver allocation. Packed color precision must
be qualified against the photometric torch/dark-lighting envelope; compactness is not acceptance.
Do not add a new copy of the existing depth buffer solely for this representation.

Concurrent fragments at one pixel must serialize the read/insert/merge/write transaction. Use
supported fragment-interlock or equivalent ordered per-pixel access, with image/resolve barriers
defined at existing pass boundaries. Shader memory barriers alone do not serialize concurrent
updates, and a fragment spinlock is not an acceptable substitute. The existing GLSL 330 baseline
does not establish these capabilities. Verify extensions, shader compiler support and actual
driver behavior before selecting an executable path.

On unsupported hardware retain the existing RGBA32F weighted-bucket path. Keep the replacement
as a capability-gated choice until correctness, memory and performance justify adoption. The
fallback is part of this design, not permission to silently drop transparent surfaces.

An optional transmission cutoff may later avoid processing sufficiently hidden farther fragments.
A threshold of 0.05 is an evaluation candidate, not an accepted error budget: bright farther
emitters may remain significant through small T. Start correctness qualification without this
shortcut, then establish a radiance-aware error bound before enabling it.

## Existing owners and integration boundaries

Use existing shader, binding, resource, pipeline and frame-publication owners. A focused
transparency resource owner may hold sample storage and count, while engine adapters remain
thin. Allocate on initialization/resize/capacity changes, clear at the transparency pass boundary,
and retire complete generations on reload/world teardown. Do not allocate or inspect source
lights per frame to support insertion. Resource publication must not mix old and new dimensions.

Trace the engine's OIT begin, shader outputs and transparent resolve boundaries together before
replacing them. Route terrain transparency, forward entities, held items, particles and liquids
according to their actual render stages. AfterOIT and late consumers retain explicit ordering;
a sorted set for one pass cannot sort fragments submitted only after that pass has resolved.
Preserve opaque depth rejection, glow eligibility, fog ownership, grading and UI/offscreen isolation.

Water requires explicit reconciliation of RGB medium transmittance, scalar interface revealage,
refraction replacement of the opaque background and scattering already applied by the water
composite. Do not apply absorption or background transmission twice. This refactor alone does
not solve multiple refracted paths or heterogeneous volume transport. Preserve both lighting
modes and their existing receiver/resource availability contracts.

The controlling integration contracts are [shared display](PBR.SharedDisplay.md),
[postprocessing](PBR.Postprocessing.md), [camera exposure](PBR.CameraExposure.md),
[water medium](PBR.WaterMedium.md), [water refraction](PBR.WaterRefraction.md),
[lighting modes](PBR.LightingModes.md) and the intended quantities in
[photometric lighting](PBR.PhotometricLighting.Proposal.md).

## Qualification and adoption decision

Compare against independently sorted CPU alpha/transmittance references: zero through capacity
samples, unequal/equal depths, shuffled submission, zero/full opacity, colored transmission,
emission and additive materials. Check exact ordering within capacity and quantify overflow
approximation beyond it. Validate insertions into an already merged interval rather than claiming
that all overflow cases are order independent. Test real concurrent overlapping draws to establish
synchronization, not just sequential shader invocations.

Qualify packed storage error, finite HDR output, frozen-scale handling, startup, resize, shader
reload, failure fallback and world retirement. Cover actual production contributors, underwater
transitions, water/refraction, glare and both lighting modes. User-run visual checks establish
appearance; headless fixtures establish only the tested numerical and lifecycle contracts.

Measure matched GPU time, bandwidth and allocated memory against the current RGBA32F buckets
at native resolutions and representative overdraw. Per-pixel insertion and serialization may
cost more despite bounded storage. Do not infer a performance improvement from fewer buckets
or a smaller logical representation.

Revisit this proposal after the photometric migration is qualified. Resolve capability coverage,
sample packing, overflow behavior, transport integration and measured costs before approving
an implementation plan. Keep the current weighted path until an explicit adoption decision.
