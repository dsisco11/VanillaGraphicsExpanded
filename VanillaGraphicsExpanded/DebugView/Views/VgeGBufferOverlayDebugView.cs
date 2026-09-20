using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateGBufferOverlayView(GBufferManager gBufferManager)
        => new(
            id: GBufferOverlayViewId,
            name: "Overlay",
            category: CategoryGBuffer,
            description: "Fullscreen overlay that blits a selected GBuffer/Primary attachment (AfterBlit).",
            registerRenderer: ctx =>
            {
                var renderer = new VgeGBufferOverlayRenderer(ctx.Capi, gBufferManager);
                return renderer;
            },
            activationMode: DebugViewActivationMode.Exclusive,
            createPanel: ctx => new GBufferOverlayPanel(viewId: GBufferOverlayViewId, ctx.Capi));

    private static class GBufferOverlayViewState
    {
        public static GBufferOverlayMode Mode { get; set; } = GBufferOverlayMode.Normals;
    }

    private sealed class GBufferOverlayPanel : DebugViewPanelBase
    {
        private readonly string viewId;
        private readonly ICoreClientAPI capi;

        private readonly string[] values = Enum.GetNames(typeof(GBufferOverlayMode));
        private readonly string[] names = Enum.GetNames(typeof(GBufferOverlayMode));

        public GBufferOverlayPanel(string viewId, ICoreClientAPI capi)
        {
            this.viewId = viewId;
            this.capi = capi;
        }

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            const double labelW = 140;
            const double rowH = 30;
            const double gap = 10;

            double boundsW = bounds.fixedWidth > 0 ? bounds.fixedWidth : bounds.OuterWidth;
            double controlW = Math.Min(280, Math.Max(200, boundsW - labelW - gap));

            ElementBounds labelBounds = ElementBounds.Fixed(0, 0, labelW, rowH).WithParent(bounds);
            ElementBounds dropBounds = ElementBounds.Fixed(labelW + gap, 0, controlW, rowH).WithParent(bounds);

            var fontLabel = CairoFont.WhiteDetailText();
            var fontSmall = CairoFont.WhiteSmallText();

            int selectedIndex = Array.IndexOf(values, GBufferOverlayViewState.Mode.ToString());
            if (selectedIndex < 0) selectedIndex = 0;

            composer
                .AddStaticText("Attachment", fontLabel, labelBounds)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        values,
                        names,
                        selectedIndex,
                        OnModeChanged,
                        dropBounds,
                        fontSmall),
                    $"{keyPrefix}-mode");
        }

        private void OnModeChanged(string code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            if (!Enum.TryParse(code, out GBufferOverlayMode mode))
            {
                return;
            }

            GBufferOverlayViewState.Mode = mode;
        }
    }

    public enum GBufferOverlayMode
    {
        Normals = 0,
        Material = 1,
        Depth = 2,
        PrimaryColor = 3
    }

    private sealed class VgeGBufferOverlayRenderer : IRenderer, IDisposable
    {
        private static readonly GlPipelineDesc OverlayPso = new(
            defaultMask: default(GlPipelineStateMask)
                .With(GlPipelineStateId.DepthTestEnable)
                .With(GlPipelineStateId.BlendEnable)
                .With(GlPipelineStateId.CullFaceEnable)
                .With(GlPipelineStateId.ScissorTestEnable)
                .With(GlPipelineStateId.ColorMask),
            nonDefaultMask: default(GlPipelineStateMask)
                .With(GlPipelineStateId.DepthWriteMask),
            depthWriteMask: false);

        private const double RenderOrderValue = 1.0;
        private const int RenderRangeValue = 1;

        private readonly ICoreClientAPI capi;
        private readonly GBufferManager gBufferManager;

        private MeshRef? quadMeshRef;

        public double RenderOrder => RenderOrderValue;
        public int RenderRange => RenderRangeValue;

        public VgeGBufferOverlayRenderer(ICoreClientAPI capi, GBufferManager gBufferManager)
        {
            this.capi = capi;
            this.gBufferManager = gBufferManager;

            var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
            quadMesh.Rgba = null;
            quadMeshRef = capi.Render.UploadMesh(quadMesh);

            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "vge_gbuffer_overlay");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.AfterBlit)
            {
                return;
            }

            if (quadMeshRef is null)
            {
                return;
            }

            int textureId = GetTextureId();
            if (textureId == 0)
            {
                return;
            }

            int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
            using var fixedFunctionState = GlStateCache.Current.CaptureLegacyFixedFunctionState();

            var blitShader = capi.Render.GetEngineShader(EnumShaderProgram.Blit);
            blitShader.Use();

            try
            {
                GlStateCache.Current.InvalidateAll();
                GlStateCache.Current.Apply(OverlayPso);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, textureId);
                blitShader.BindTexture2D("scene", textureId, 0);
                GpuSamplers.NearestClamp.Bind(0);

                capi.Render.RenderMesh(quadMeshRef);
            }
            finally
            {
                blitShader.Stop();

                GL.ActiveTexture((TextureUnit)prevActiveTexture);
                GlStateCache.Current.InvalidateAll();
            }
        }

        private int GetTextureId()
        {
            return GBufferOverlayViewState.Mode switch
            {
                GBufferOverlayMode.Normals => gBufferManager.NormalTextureId,
                GBufferOverlayMode.Material => gBufferManager.MaterialTextureId,
                GBufferOverlayMode.Depth => capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId,
                GBufferOverlayMode.PrimaryColor => capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0],
                _ => 0
            };
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);

            if (quadMeshRef is not null)
            {
                capi.Render.DeleteMesh(quadMeshRef);
                quadMeshRef = null;
            }
        }
    }
}
