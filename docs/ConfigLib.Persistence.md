# ConfigLib setting persistence

The 2026-09-27 automation client log records ConfigLib 1.10.12 failing to save:
`Can not add property MaterialAtlas/TerrainSubdivision/MaximumLevel ... Property
with the same name already exists on object.`

The saved document lacked the corresponding nested property. Installed
`JsonObjectPath.Set` replaces existing tokens only; it cannot create missing
parents or leaves. ConfigLib falls back to adding a literal slash-delimited root
key, which fails when a previous save already added that key. The exception
aborts saving other settings, including atmospheric quality.

VGE now fills absent current properties before ConfigLib loads the document.
Existing values and unknown keys are preserved. Misplaced legacy values are not
interpreted or migrated. ConfigLib remains the sole writer during GUI save events.
VGE resets its loaded flag at world shutdown so the next world reads the file.

The three controls now use plain integer settings with ConfigLib min/max ranges,
not named dropdown mappings:

- Atmospheric quality: 0-3, default 0.
- Maximum terrain subdivision: 1-8, default 5.
- Surface detail mode: 0 disabled, 1 relief, 2 tessellation; default 1.

Both VGE and ConfigLib write numerical values. Existing hand-edited files must use
these numbers rather than enum names; no legacy conversion is installed.

The installed ConfigLib 1.10.12 JSON-file constructor, setting-loaded event
publication, file save and reload were exercised with 8/3/2 for subdivision,
atmospheric quality and surface detail. All preserved those values with the plain
integer definitions. Evidence: `artifacts/ConfigPersistence/numeric-constructor-results.txt`.

## Previous mapped-setting diagnosis

ConfigLib named mappings save their names, such as `High`. Atmospheric quality
uses the `AtmosphereQuality` enum (`Low` through `Ultra`). The tessellation maximum
uses `TerrainSubdivisionLevel` (`Level1` through `Level8`), with matching ConfigLib
choices. VGE accepts enum names and numeric values through standard JSON enum
handling, but writes names for atmospheric quality, subdivision level and surface
detail mode. ConfigLib's mapped-setting loader requires those names: numeric
values such as `3` and `8` leave its `Low` and `Level5` defaults selected.
Previously, a whole-config save from the debug view controller or LumOn debug
toggles could replace ConfigLib's named selections with numbers, causing the next
load to reset them. Property-level `StringEnumConverter` attributes preserve the
same representation through both writers. No legacy-value migration is added.

All VGE ConfigLib settings explicitly declare `clientSide: true`. Without it,
ConfigLib excludes their changes from multiplayer-client saves. This is separate
from the reproduced single-player enum serialization failure.

No user config files were modified during implementation. Live save/reopen
verification remains with the user.
