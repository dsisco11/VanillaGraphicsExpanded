# PBR Material Definitions Schema (VGE)

This document describes the expected structure of `assets/<modid>/materials/pbr_material_definitions.json`.

The registry loads all mods that provide this file, merges materials deterministically, and builds:

- `MaterialId -> PBRMaterialDefinition`
- `Texture AssetLocation -> MaterialId`

## Canonical Concepts

### MaterialId

- Canonical format: `<modid>:<name>`
- For author convenience:
  - Material keys in `materials` MAY omit the prefix (e.g. `stone`). They are treated as `<thisFileModid>:stone`.
  - `values.material` in mapping rules MAY omit the prefix as well, using the same rule.

### Texture match key

Mapping rules are written against a filesystem-like path. The matcher evaluates globs against this canonical string:

`assets/<domain>/<path>`

Where:

- `<domain>` is the asset domain (mod id)
- `<path>` is the asset path within that domain (uses `/` separators)

Example: an `AssetLocation` of domain `game` and path `textures/block/stone/granite.png` is matched as:

`assets/game/textures/block/stone/granite.png`

### Glob syntax

Use standard **globstar** syntax:

- `*` matches within a path segment (does not cross `/`)
- `?` matches a single character within a segment (does not match `/`)
- `**` matches across path segments (may cross `/`)

## Top-level JSON

```json
{
  "$schema": "(optional schema url)",
  "version": 1,
  "notes": ["optional informational strings"],
  "defaults": { ... },
  "materials": { ... },
  "mapping": [ ... ]
}
```

### version

- Required.
- Integer.
- Current: `1`.

### notes

- Optional.
- Array of strings for documentation; ignored by runtime.

## defaults

```json
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
}
```

All numeric values are floats in `[0, 1]` unless otherwise stated.

## materials

`materials` is a dictionary of `materialKey -> materialDefinition`.

```json
"materials": {
  "stone": {
    "roughness": 0.92,
    "metallic": 0.0,
    "priority": 0,
    "noise": {
      "roughness": 0.02,
      "normals": 0.05
    }
  },
  "mymod:polishedStone": {
    "roughness": 0.35,
    "metallic": 0.0,
    "emissive": 0.0,
    "priority": 10
  }
}
```

### materialDefinition fields

- `roughness` (optional): float
- `metallic` (optional): float
- `emissive` (optional): float
- `priority` (optional): integer, higher wins when merging
- `noise` (optional): per-channel delta ranges

`reflectivity` is NOT stored here for GPU transport; it is computed in the patched base-game shader before writing `gMaterial`.

### noise

Noise is deterministic and evaluated in shaders using Squirrel3 noise.

The `noise` object defines a **delta-range** per channel:

- shader generates a deterministic random scalar per channel
- multiplies by the corresponding delta-range
- applies the resulting delta to the channel value (or to the normal angle)

Supported keys (all optional):

- `roughness`: float delta-range
- `metallic`: float delta-range
- `emissive`: float delta-range
- `reflectivity`: float delta-range (allowed, but reflectivity may still be computed)
- `normals`: float delta-range (interpreted as normal-angle variation)

## mapping

`mapping` is an ordered array of mapping rules.

```json
"mapping": [
  {
    "id": "metal-blocks",
    "description": "Mark metal texture families as metallic",
    "priority": 0,
    "match": { "glob": "assets/**/textures/block/**/metal/**/*.png" },
    "values": { "material": "metal_generic" }
  }
]
```

### Rule ordering / priority

- Higher `priority` overrides lower `priority` when multiple rules match the same texture.
- If priorities tie, tie-break uses a deterministic merge order (domain/modid order, then file order, then mapping order).

### match

Current v1 match supports:

- `glob` (string): globstar match against the canonical key `assets/<domain>/<path>`.

### values

- `material`: required; material key or fully-qualified MaterialId.
