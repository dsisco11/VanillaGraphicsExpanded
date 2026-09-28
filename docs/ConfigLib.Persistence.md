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

ConfigLib named mappings save their names, such as `High`. Atmospheric quality
uses the `AtmosphereQuality` enum (`Low` through `Ultra`). The tessellation maximum
uses `TerrainSubdivisionLevel` (`Level1` through `Level8`), with matching ConfigLib
choices. Both accept their enum names and numeric values through standard JSON
enum handling, retain numeric VGE serialization, and sanitize out-of-range values
before allocating resources or publishing GPU parameters.

No user config files were modified during implementation. Live save/reopen
verification remains with the user.
