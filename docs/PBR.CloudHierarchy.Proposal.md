# Conservative min/max cloud hierarchy

Status: approved design. This document describes the design; implementation planning is separate.

Related architecture: [PBR.Clouds.md](PBR.Clouds.md).
Implementation checklist: [PBR.CloudHierarchy.todo](PBR.CloudHierarchy.todo).

## Purpose

Accelerate volumetric view and secondary-light rays by skipping groups of native cloud columns
which cannot intersect the ray. Preserve native weather, cubic cloud bounds, procedural fluff,
PMJ sampling, lighting and existing OIT composition.

The hierarchy summarizes cloud geometry in world space. The shared scene-depth HZB continues
to provide foreground clipping; it cannot describe empty cloud regions or off-camera light rays.

## Representation

Use the currently certified native cloud map as the logical leaf level. Build only the
coarser summaries in a VGE-owned RG32F mip hierarchy:

- Read leaf extinction and lower/upper bounds directly from the native texture; do not copy
  leaves into another texture. Convert native heights from tile units to world units.
- The first owned level reduces 2-by-2 native columns. Subsequent levels reduce 2-by-2 summaries.
- Invalid or empty columns contribute an empty interval, represented by positive/negative infinity.
- Each parent stores the minimum occupied lower height and maximum occupied upper height.
- Node position and level supply its horizontal bounds; the stored interval completes a
  conservative bounding box around all descendant cloud density.

Use the same column validity rules as the density shader. Logically pad the native grid to
the next power of two with empty leaves; never fetch out-of-map native texels or rely on
texture clamping for padding. A maximum 200-by-200 map has a logical 256-by-256 leaf level,
but owned storage starts at 128-by-128 and ends at 1-by-1: eight mip levels and approximately
0.17 MiB of texture payload, excluding allocator overhead. Owned texture mip zero therefore
represents logical hierarchy level one, with a horizontal footprint of two native cells per axis.

The installed native producer uses 50-by-50-world-unit columns, each containing one vertical
interval. Its map width is `clamp((8 * desiredViewDistance) / 50, 20, 200)` with integer division;
odd widths and non-power-of-two dimensions are valid. Horizontal bounds are implicit in the
regular grid. Neither map placement nor vertical bounds provide power-of-two alignment.

Native normalized controls produce column thicknesses from 25 to 450.25 world units, while
animated undulation translates both bounds together. These thickness limits do not establish
occupancy, equal neighboring heights or fixed vertical cells. Use the actual generated bounds
and extinction rather than reconstructing geometry from CPU weather controls.

Native bounds are conservative because current noise and edge erosion never extend density
outside a column. Combining separated child intervals may include empty space, which reduces
pruning efficiency but preserves correctness. Do not classify a region as empty from a few
procedural-noise samples or from a nonzero extinction threshold.

## Traversal

Intersect the root bounds to reject rays outside the cloud layer. For each candidate node,
test the remaining ray segment through its horizontal footprint against its height interval.
Skip the entire region only when that segment cannot intersect any descendant cloud; otherwise
descend to actual columns. At the finest owned level, test its native children directly using
the existing leaf validity and intersection rules. Keep logical hierarchy levels distinct from
owned texture mip indices when deriving footprints and traversal boundaries.

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
Reuse storage across ordinary frames. Rebuild for each newly published map generation: native
GPU undulation can change bounds even when CPU weather controls and their uploaded textures
are unchanged. CPU upload or weather-update events alone are not sufficient invalidation signals.

Certify the result against its source owner, world, allocation, map generation and placement;
publish only after GPU writes are visible to sampling. Missing or mismatched hierarchy data
uses the existing column traversal. Retire resources when their owning cloud lifetime ends or
their source/executable generation is invalidated.

## Acceptance

Compare accelerated and existing traversal using identical PMJ frames and settings. Preserve
occupied intervals, optical depth, lighting, all OIT payload channels and foreground clipping.
Exercise empty/sparse/dense maps, separated heights, odd and non-power-of-two dimensions,
padded edges, the owned-mip/native-leaf transition, boundary and parallel rays,
inside-cloud cameras, source replacement and in-place map updates. Include independent bounds
and traversal checks so both paths cannot share the same skipping error.

Measure hierarchy construction plus view/light traversal cost against the existing path. Dense
overlapping columns may offer little pruning; performance benefit must be demonstrated rather
than inferred from hierarchy size. This proposal adds no 3D density volume or shared scene-HZB
format change.
