# Engine framebuffer tracking and OIT

The installed Vintage Story 1.22.7 engine binds framebuffers directly in
`ClientPlatformWindows.CurrentFrameBuffer` and `CurrentFrameBufferKeepVw`.
Previously, those binds did not update `GlStateCache`. A cached primary binding
could therefore survive a switch to the transparent framebuffer.

`GBufferManager.ReapplyGBufferBlendState`, called after engine blend changes,
uses that binding to decide whether primary-only attachment policies apply.
With the stale binding, it disabled blending at OIT accumulation attachments
4 and 5. The engine's OIT layer renderer uses six attachments, including additive
accumulation at 3–5; those attachment numbers overlap VGE's opaque material
outputs but have different meanings.

`FramebufferBindingHook` now observes both completed engine setters and updates
the existing binding cache. This requires no additional driver query, framebuffer
bind, or per-draw allocation. VGE-owned binds continue through the existing cache.

## Evidence and limits

- A headless GPU regression reproduced the incorrect blend disable before the fix:
  `artifacts/Aerial/oit-state.trx`.
- Nine installed standard/entity fragment shader cases preserved authored alpha
  and OIT revealage: `artifacts/Aerial/coverage.trx`.
- After the fix, all 14 focused Release GPU cases passed:
  `artifacts/Aerial/oit-fixed.trx`. Both actual Harmony-patched engine setters
  preserve OIT additive blending, retain primary-only repair, and track default
  framebuffer unbinding. Existing read/draw binding tests also passed.
- Release build passed: `artifacts/oit-fixed-build.log`.
- These results establish a framebuffer-state defect, not a complete explanation
  of the reported live transparency. In particular, first-person rendering can
  use the primary framebuffer rather than OIT. Live confirmation remains open.

## Follow-up: sampler ownership

The user confirmed that the framebuffer fix did not resolve the visible issue,
including with LumOn disabled.

The subsequent client log (2026-09-27 05:49) repeatedly reports undefined sampling:
comparison-enabled sampler 7 on ordinary color textures, and non-comparison
sampler 5 on shadow depth textures. These numbers identify sampler objects, not
texture units.

The installed engine's `ShaderProgramBase.Stop` clears only the initial units
covered by its `customSamplers.Count`. VGE's explicit contract bindings are not
in that dictionary. Consequently, its sampler objects can remain bound when an
engine shader binds another texture at those units. The sampler object overrides
the new texture's filtering and comparison settings.

`GpuProgramStopHook` releases the stopped VGE program's declared sampler slots
through `GlStateCache`. The engine's sealed Stop method requires a hook so calls
through its interfaces and current-program reference also perform cleanup.
This uses the existing contracts without source scanning or driver queries.
The connection to all reported live transparency still requires confirmation.

The complete installed standard vertex/fragment pair passed four depth and
coverage cases (opaque/forward routes, with/without first-person depth offset).
A later background draw remained occluded. A separate actual
`IShaderProgram.Stop` regression reproduced surviving shadow samplers at units
4 and 5 without the hook; with the hook those bindings are zero while unrelated
unit 9 remains unchanged. Initial receipt: `artifacts/Oit/sampler-retirement.trx`.
Installed engine IL and distinct live warnings are retained in
`artifacts/Oit/engine-sampler-lifetime.il` and
`artifacts/Oit/sampler-runtime-warnings.txt`.

Final Release build and all 20 focused GPU cases passed:
`artifacts/Oit/transparency-final.trx` and `artifacts/oit-transparency-final.log`.
This includes an ordinary texture draw after VGE stops, with exact expected RGBA,
plus the previous OIT coverage and framebuffer-state regressions. These tests
verify the sampler fix; they do not substitute for the user's live visual check.
