# VGE PBR Material Registry (Modder Guide)

This feature lets you define PBR-like material parameters (roughness/metallic/emissive + optional noise) and map them to textures using globs.

## Where to put the file

Create this file in your mod assets:

- `assets/<yourmodid>/materials/pbr_material_definitions.json`

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

## Conflict resolution (deterministic)

### MaterialId collisions

If two mods define the same `MaterialId`:

- Higher `material.priority` wins.
- On ties, later in deterministic load order wins (stable by domain/modid then asset path).

### Texture mapping collisions

If multiple mapping rules match the same texture:

- Higher `mapping.priority` wins.
- On ties, later rule wins (deterministic domain/modid order, then file order, then rule order).

## Best practices

- Prefer **narrow globs** over broad ones (avoid `assets/**/textures/**/*.png` unless you truly mean it).
- Use `priority` sparingly; reserve high priority for deliberate overrides.
- Keep `materials` IDs lowercase (VGE normalizes ids, but lowercase avoids confusion).
- Start with a small set of materials (stone/wood/metal/liquid) and refine.

## Troubleshooting

- If a mapping rule references a material that doesn’t exist, VGE logs a warning and skips that mapping.
- If your material params look unchanged in-game, check:
  - Your glob matches the canonical `assets/<domain>/<path>` string.
  - The texture you’re targeting is part of the block atlas (terrain/chunk rendering).

## References

- Schema reference: [docs/PBRMaterialDefinitions.schema.md](PBRMaterialDefinitions.schema.md)
- Canonical example shipped with VGE: [VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/materials/pbr_material_definitions.json](../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/materials/pbr_material_definitions.json)
