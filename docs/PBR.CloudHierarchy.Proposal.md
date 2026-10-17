# Conservative min/max cloud hierarchy

Status: proposed. This document describes the design; implementation planning follows approval.

Related architecture: [PBR.Clouds.md](PBR.Clouds.md).

## Purpose

Accelerate volumetric view and secondary-light rays by skipping groups of native cloud columns
which cannot intersect the ray. Preserve native weather, cubic cloud bounds, procedural fluff,
PMJ sampling, lighting and existing OIT composition.

The hierarchy summarizes cloud geometry in world space. The shared scene-depth HZB continues
to provide foreground clipping; it cannot describe empty cloud regions or off-camera light rays.

## Representation

Build a VGE-owned RG32F mip hierarchy from the currently certified native cloud map:

- An occupied leaf stores the column's lower and upper heights in world units.
- Invalid or empty columns use an empty interval, represented by positive/negative infinity.
- Each parent stores the minimum occupied lower height and maximum occupied upper height.
- Node position and level supply its horizontal bounds; the stored interval completes a
  conservative bounding box around all descendant cloud density.

Use the same column validity rules as the density shader. Pad the native grid to the next
power of two with empty leaves. A maximum 200-by-200 map therefore uses a 256-by-256 base and
nine mip levels: approximately 0.67 MiB of texture payload, excluding allocator overhead.

Native bounds are conservative because current noise and edge erosion never extend density
outside a column. Combining separated child intervals may include empty space, which reduces
pruning efficiency but preserves correctness. Do not classify a region as empty from a few
procedural-noise samples or from a nonzero extinction threshold.

## Traversal

Intersect the root bounds to reject rays outside the cloud layer. For each candidate node,
test the remaining ray segment through its horizontal footprint against its height interval.
Skip the entire region only when that segment cannot intersect any descendant cloud; otherwise
descend to actual columns.

At occupied leaves, retain the current column intersection, integration intervals, sample counts,
PMJ indices and cell/light decorrelation. Hierarchical traversal changes which empty regions are
visited, rather than the integration of occupied regions. Boundary ties, parallel rays and
cameras inside clouds require conservative handling and guaranteed forward progress.

Use the same hierarchy for view rays and existing secondary self-lighting marches. Full-resolution
resolve remains texture reconstruction. Cloud shadows cast onto the scene remain deferred to
virtual shadow maps.

## Ownership and publication

Generate the hierarchy on the GPU after native map generation and before VGE cloud tracing,
using the existing map publication boundary without additional native-method hooks or CPU readback.
Reuse storage across ordinary frames. Rebuild for each newly published map generation unless
unchanged bounds can be established through an authoritative producer contract.

Certify the result against its source owner, world, allocation, map generation and placement;
publish only after GPU writes are visible to sampling. Missing or mismatched hierarchy data
uses the existing column traversal. Retire resources when their owning cloud lifetime ends or
their source/executable generation is invalidated.

## Acceptance

Compare accelerated and existing traversal using identical PMJ frames and settings. Preserve
occupied intervals, optical depth, lighting, all OIT payload channels and foreground clipping.
Exercise empty/sparse/dense maps, separated heights, padded edges, boundary and parallel rays,
inside-cloud cameras, source replacement and in-place map updates. Include independent bounds
and traversal checks so both paths cannot share the same skipping error.

Measure hierarchy construction plus view/light traversal cost against the existing path. Dense
overlapping columns may offer little pruning; performance benefit must be demonstrated rather
than inferred from hierarchy size. This proposal adds no 3D density volume or shared scene-HZB
format change.
