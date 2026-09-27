# ConfigLib saved setting paths

ConfigLib 1.10.12 reads the file declared by the asset definition's `file` field:
`VanillaGraphicsExpanded.json`. Its setting `name` is a slash-separated JSON path, such as
`LumOn/Enabled`. VGE's additional `code` field is a separate dot-separated mapping used when
applying ConfigLib event keys to the in-memory configuration.

The former `name: "LumOn.Enabled"` did not resolve the nested JSON property. ConfigLib selected
the definition's default `true` and emitted an `ENABLED=true` setting-loaded event, replacing VGE's
correctly deserialized `false`. The archived 2026-09-26 21:51 startup log records that event.

All 59 setting names now use slash-separated paths. Their event codes and mapping keys are unchanged.
This corrects loading of saved values, not just the LumOn toggle. No user configuration file is
rewritten by this fix.

The installed ConfigLib assembly reproduced the mismatch against the user's read-only JSON file:
the dotted path returned no match, while `LumOn/Enabled` returned `false`. Evidence is recorded in
`artifacts/PbrColor/ConfigLibFalsePathReproduction.txt`, `ConfigLibRead.il.txt`, and
`ConfigLibSettingFromJson.il.txt`. VGE deserialization and sanitization both preserved `false`.
