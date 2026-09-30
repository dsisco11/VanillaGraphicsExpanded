# Engine render scopes

The installed Vintage Story 1.22.7 renderer was inspected through `ClientEventManager.TriggerRenderStage`, `ScreenManager.Render`, and `ClientPlatformWindows` IL. Previously VGE inserted renderer callbacks at orders -999 and 999 to label thirteen whole stages. Individual callbacks and composition outside those stages had no VGE scope. Program object labels are separate and do not delimit render work.

| Previously uncovered boundary | Scope coverage |
| --- | --- |
| Every registered world renderer, including shadow, opaque, OIT, first-person and orthographic callbacks | `<registration>` inside `VS.<stage>` |
| Action registrations represented by `DummyRenderer` | Registration name; unnamed callbacks use short type and method names |
| Transparent accumulation composition | `VS.MergeTransparentRenderPass`, with the transparent-compose fullscreen shader inside |
| Bloom extraction | Postprocessing parent, then `game:findbright` |
| Bloom horizontal/vertical blur at both resolutions | Four separate fullscreen blur scopes in submission order |
| God rays | Fullscreen god-rays scope |
| SSAO and its horizontal/vertical bilateral filter | Three separate fullscreen scopes |
| FXAA luminance preparation or ordinary copy | Fullscreen luma or blit scope |
| Final composition and primary-to-default copy | Named engine method scopes, each containing its fullscreen shader scope |
| Other screen rendering to primary, and screen rendering after postprocessing, after final composition, after blit and to the default framebuffer | `<screen>.<boundary>`, e.g. `RunningGame.AfterPostProcessing` |
| Shared menu background drawn outside screen callbacks | `VS.GuiCompositeMainMenuLeft.RenderBg` |

`EngineRenderStageScopeHook` replaces both installed engine callback invocation sites (normal and extended-debug dispatch) with a balanced callback wrapper. The original engine profiling and GL error checks remain in place. Stage scopes now surround the actual dispatcher, so render order and thrown callbacks cannot strand a group.

Dedicated composition hooks use `HarmonyPatch` attributes with `typeof(ClientPlatformWindows)`, `nameof` method references and exact argument types. `EngineFullscreenScopeHook` covers `RenderFullscreenTriangle`. The installed postprocessing implementation has eleven fullscreen callsites, including the mutually exclusive luma/blit paths. Active shader names identify these procedurally; repeated blur submissions retain separate scopes without relying on an instruction offset or manually maintained shader allowlist.

`EngineScreenScopeHook` discovers concrete implementations of the screen render boundaries using `typeof(GuiScreen)` and its `nameof` method references rather than enumerating individual menu/loading/game screen types. It excludes `GuiScreenRunningGame.RenderToPrimary`, so the `RunningGame.ToPrimary` scope is absent while other screen boundaries remain. The separate `ScreenManager.Render(float)` scope has also been removed. Menu-background rendering retains its own hook. Names are cached. Handler and shader name caches use weak keys so world shutdown and shader reload do not retain old objects.

All instrumentation uses `GlDebug.Group`. Harmony prepares these hooks only in Debug builds, matching the previous instrumentation policy. Unsupported debug-group implementations remain no-ops through the existing abstraction. Every scope uses either `using` or a Harmony finalizer, preserving exception propagation and balanced nesting. This labels engine pass boundaries rather than creating a scope for every terrain mesh submission. Individual objects within a renderer are still draws within their owning callback scope.

Validation: Debug build succeeded and 16 focused tests passed (including existing engine shader-object labeling tests). Installed-engine tests cover both dispatcher invocation sites, all eleven postprocessing fullscreen callsites, screen discovery, and actual Harmony installation of every selected hook. Headless GPU tests verify nested stage/callback group depth and balance after success and exceptions. Results: `artifacts/EngineScopes/results/engine-scopes.trx`; build/test log: `artifacts/engine-scopes-test.log`.

A user-run RenderDoc capture remains the visual confirmation; no game session was launched. Independent source review found no additional standalone draw boundary in the inspected main-frame dispatch path.

## Engine resource labels

Debug builds also label borrowed engine resources through `GlDebug.TryLabel`. Labeling changes object metadata only; it does not bind, resize, dispose, or take ownership of engine resources. Unsupported KHR_debug contexts retain the existing no-op behavior.

| Resource | Label examples | Allocation boundary |
| --- | --- | --- |
| Default framebuffers and textures | `VS.Primary.Framebuffer`, `VS.Primary.Color`, `VS.Primary.Depth`, `VS.ShadowmapNear.Depth` | Completed `SetupDefaultFrameBuffers` return; current table also named once at client startup |
| Custom framebuffers | `VS.<FramebufferAttrs.Name>.Framebuffer`, `.Color0`, `.Depth` | `CreateFramebuffer` return |
| OIT bucket extensions | `VS.OIT.BucketRevealage`, `VS.OIT.AccumulationBuckets`, `VS.OIT.Revealage`, `VS.OIT.Glow` | `SystemRenderOITLayers.BeforeOIT.rebuild` return |
| Uploaded or reserved meshes | `VS.Mesh<vaoId>.VAO`, `.Positions`, `.Indices`, `.Flags`, `.CustomInt` | `UploadMesh`, `AllocateEmptyMesh`, `AllocateEmptySSBOMesh` returns |
| Classified mesh pools | `VS.MeshPool.Liquid.Atlas0.Pool0.VAO` and the same prefix for each stream | Manager-table construction/growth supplies context; `AddModel` labels only newly appended pools |
| Shared engine index buffer | `VS.Mesh.SharedIndices` | Mesh allocation, without naming shared storage after an individual pool |

The OIT hook targets the later bucket allocation because the original `FrameBufferRef.ColorTextureIds[0]` retains the replaced initial texture ID. Bucket IDs come from cached accessors to the engine system's static fields; the hook does not inspect GL attachments per frame. The accumulation texture array has one object label shared by its three layers.

Default framebuffer labeling deduplicates texture IDs within the completed table, preserving the first owner's label for shared primary depth. Existing G-buffer integration uses the same naming helper, replacing the older `VS_Primary` / `VS_Depth` naming path. Pool context is weakly keyed by manager and adds no resource ownership. Ordinary uploads into existing pools perform no label calls; there is no draw-time pool scan. Meshes outside classified tables retain their generic allocation labels. Resources allocated before hook installation are not exhaustively backfilled; the current default framebuffer table is explicitly covered at startup.

Resource-label validation: the Debug build and 11 focused tests passed, covering driver label readback, replacement resources, shared depth and indices, binding-state preservation, pool growth, installation of all seven allocation targets, existing shader labels and framebuffer rebuild behavior. No game was launched; RenderDoc presentation remains user validation.

After converting known targets to typed Harmony attributes, the Debug build and all 14 focused scope tests passed again, including actual installation of the frame and menu hooks. Evidence: `artifacts/typed-scopes-test.log` and `artifacts/EngineScopes/results/typed-scopes.trx`.
