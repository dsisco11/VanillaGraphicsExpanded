# VGE PBR Material Registry (Modder Guide)

This feature lets you define PBR-like material parameters (roughness/metallic/emissive + optional noise) and map them to textures using globs.

## Where to put the file

Create this file in your mod assets:

- `assets/<yourmodid>/config/vge/material_definitions.json`

VGE discovers this file in all loaded mods and merges them deterministically.

## Quick start example

```json
{
  "$schema": "../../../../schemas/pbr_material_definitions.schema.json",
  "version": 1,
  "defaults": {
    "roughness": 0.85,
    "metallic": 0.0,
    "emissive": 0.0,
    "noise": {
      "roughness": 0.02,
      "metallic": 0.0,
      "emissive": 0.0,
      "normals": 0.05
    }
  },
  "materials": {
    "stone": { "roughness": 0.92 },
    "metal_generic": { "roughness": 0.03, "metallic": 1.0 },
    "mymod:glowing_crystal": {
      "roughness": 0.15,
      "emissive": 0.9,
      "priority": 10
    }
  },
  "mapping": [
    {
      "id": "mymod-metals",
      "priority": 0,
      "match": { "glob": "assets/mymod/textures/block/**/metal/**/*.png" },
      "values": { "material": "metal_generic" }
    },
    {
      "id": "mymod-crystals",
      "priority": 10,
      "match": { "glob": "assets/mymod/textures/block/crystal/**/*.png" },
      "values": { "material": "mymod:glowing_crystal" }
    }
  ]
}
```

Notes:

- Material keys in `materials` can be **local** (`"stone"`) or **fully qualified** (`"mymod:stone"`).
- Local keys are treated as `"<thisfilemodid>:<key>"`.

## Mapping textures

Mapping rules match against this canonical string:

- `assets/<domain>/<path>`

Example: an `AssetLocation("game", "textures/block/stone/granite.png")` is matched as:

- `assets/game/textures/block/stone/granite.png`

### Glob syntax

VGE uses **globstar** matching:

- `*` matches within a single path segment (does not cross `/`)
- `?` matches one character within a segment (does not match `/`)
- `**` matches across path segments (may cross `/`)

## Explicit texture overrides (per mapping rule)

Mapping rules can optionally specify **explicit texture maps** to use instead of VGE-generated/baked maps.

Fields:

- `values.overrides.materialParams`: explicit packed _material params_ map
- `values.overrides.normalHeight`: explicit packed _normal+height_ map

Example:

```json
{
  "id": "mymod-granite-overrides",
  "priority": 10,
  "match": { "glob": "assets/mymod/textures/block/stone/granite.png" },
  "values": {
    "material": "stone",
    "overrides": {
      "materialParams": "mymod:textures/vge/params/stone/granite_pbr.png",
      "normalHeight": "mymod:textures/vge/normalheight/stone/granite_nh.dds"
    }
  }
}
```

### Supported file formats

- `.png` via the Vintage Story asset system
- `.dds` decoded via BCnEncoder.NET

For `.dds`, VGE expects standard DDS files using common block-compressed BCn formats.
BCnEncoder.NET supports BC1–BC7 (DXT1/3/5, RGTC/BC4/BC5, BC6H, BC7).

### Required dimensions

Overrides must match the exact atlas-rect dimensions VGE is filling (typically tile-sized, e.g. 32×32).
VGE currently does **not** resample; if dimensions mismatch, VGE logs a warning and falls back.

### Packing / channel semantics

**Material params override (`materialParams`)**

- Used to fill the material-params atlas (RGB16F).
- Channels are interpreted as:
  - `R` = roughness (0..1)
  - `G` = metallic (0..1)
  - `B` = emissive (0..1)
  - `A` is ignored

**Normal+height override (`normalHeight`)**

- Used to fill the normal+height atlas (RGBA16F).
- Channels are interpreted as:
  - `RGB` = normal packed in 0..1 (consumer converts to signed via `*2-1`)
  - `A` = height01 (0..1), where `0.5` is neutral

Note: VGE’s procedural normal+height bake applies an alpha-cutoff mask derived from the albedo.
Overrides are currently copied as-authored; if you need cutout behavior, bake it into your override texture.

## Conflict resolution (deterministic)

### MaterialId collisions

If two mods define the same `MaterialId`:

- Higher `material.priority` wins.
- On ties, later in deterministic load order wins (stable by domain/modid then asset path).

### Texture mapping collisions

If multiple mapping rules match the same texture:

- Higher `mapping.priority` wins.
- On ties, the first matching rule wins (deterministic domain/modid order, then file order, then rule order).
  Put broad catch-all rules last.

## Best practices

- Prefer **narrow globs** over broad ones (avoid `assets/**/textures/**/*.png` unless you truly mean it).
- Use `priority` sparingly; reserve high priority for deliberate overrides.
- Keep `materials` IDs lowercase (VGE normalizes ids, but lowercase avoids confusion).
- Start with a small set of materials (stone/wood/metal/liquid) and refine.

## Material noise (blue-noise)

The `noise` fields in material definitions are applied when VGE builds the **material params atlas** (the packed roughness/metallic/emissive sidecar).

### Why blue-noise?

For per-texel variation, VGE uses a **tileable blue-noise map** (Void-and-Cluster) instead of hash noise.

This is intended to:

- Avoid obvious low-frequency banding and long straight features.
- Keep results deterministic and stable across loads.

### Defaults (current)

The bake-time noise source is a cached, tileable rank map with these defaults:

- Tile size: 64×64
- Sigma: 1.5
- Seed: `0xC0FFEE`
- Max iterations: 2048

These values are currently defined in code in:

- `VanillaGraphicsExpanded/PBR/Materials/MaterialAtlasParamsBuilder.cs`

### Mapping strategy

Noise sampling is done in **local texel coordinates inside each atlas rect** (tile-sized).

- The blue-noise tile is wrapped in X/Y (periodic).
- A deterministic per-texture/per-channel tile offset is derived from a stable hash of the texture asset location.
  This reduces repeated phase alignment between different textures and between channels.

The final per-channel value is:

`value = clamp01(base + (noiseSigned * amplitude))`

where `noiseSigned` is in approximately `[-1, 1]`.

### How to toggle / disable

You can disable noise without changing code:

- Per material: set `noise.roughness`, `noise.metallic`, and `noise.emissive` to `0`.
- Globally: set `defaults.noise.*` to `0` (and avoid adding non-zero `noise` in materials).

If you need full authored control instead of procedural noise:

- Use `values.overrides.materialParams` to provide an explicit per-texel material params texture.

## Troubleshooting

- If a mapping rule references a material that doesn’t exist, VGE logs a warning and skips that mapping.
- If your material params look unchanged in-game, check:
  - Your glob matches the canonical `assets/<domain>/<path>` string.
  - The texture you’re targeting is part of the block atlas (terrain/chunk rendering).

## References

- Schema reference: [docs/PBRMaterialDefinitions.schema.md](PBRMaterialDefinitions.schema.md)
- Canonical example shipped with VGE: [VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/config/vge/material_definitions.json](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/config/vge/material_definitions.json)
