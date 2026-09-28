# Terrain atlas binding

The renderer owns atlas-page selection. VGE injects its companion resource binding
immediately after the engine's primary atlas setter call, retaining the same shader
instance and atlas texture ID. It does not patch `BindTexture2D` or depend on a
property-setter postfix executing.

The installed 1.22.7 renderer contains these call sites:

| Method | Primary atlas assignments |
| --- | --- |
| `RenderOpaque` | Four opaque, one topsoil |
| `RenderShadow` | Four shadow |
| `RenderOIT` | One liquid, one transparent |
| `RenderAfterOIT` | One opaque |

Exact setter method identities and expected counts are validated when applying the
transpiler. The original engine assignment executes first; the injected call then
binds material, normal/depth and relief resources for that page, followed by the
existing Surface Cache mapping refresh. The mapping refresh on program use remains
available for frame-level changes. Old atlas-setter postfixes are removed.

## Evidence and validation boundary

The September 28 automation log showed opaque program 43 reaching both atlas-setter
callbacks and issuing material/normal bindings. Topsoil program 67 was used, but
neither callback recorded an arrival despite successful patch registration. The
user's RenderDoc capture independently showed missing VGE resources on topsoil
draws. These observations identify a missed binding callback; they do not establish
whether JIT inlining caused the bypass.

Temporary binding diagnostics were removed at the user's request. Live acceptance
requires a new user-run capture showing the VGE atlases on topsoil draws and correct
nearby normals/shadow reception. Shader-math tests alone do not establish that result.

Focused validation passed 10/10 tests: eight renderer call-site tests and two existing
atlas plumbing tests. Coverage includes all four installed renderer methods, preserved
engine calls and evaluated operands across page changes, branch/exception metadata,
and rejection of missing or prefixed call layouts. Receipt:
`artifacts/test-results/terrain-atlas-binding/terrain-atlas-binding.trx`.
