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
