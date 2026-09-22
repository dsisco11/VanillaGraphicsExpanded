using System;
using System.Numerics;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateWorldCellBoundsView()
        => new(
            id: WorldCellBoundsViewId,
            name: "World Cell Bounds",
            category: CategoryGeometry,
            description: "Draws registered partition cells and source envelopes alongside legacy scene cells. Green: ready; orange: required/unpublished; violet: prefetch/unpublished; magenta: unsupported. White: source requirement; cyan: selected cell envelope.",
            registerRenderer: ctx => new VgeWorldCellBoundsWireframeRenderer(ctx.Capi),
            activationMode: DebugViewActivationMode.Exclusive,
            createPanel: ctx => new WorldCellBoundsPanel(ctx.Capi));

    private static class WorldCellBoundsViewState
    {
        public static int RadiusChunks { get; set; } = 8;
        public static bool DepthTest { get; set; } = true;
        public static bool ShowUnloaded { get; set; } = true;
        public static bool ColorByDesiredState { get; set; }
        public static string DiagnosticsText { get; set; } = "Waiting for partition diagnostics.";
        public static int SelectedPartition { get; set; }
    }

    private sealed class WorldCellBoundsPanel : DebugViewPanelBase
    {
        private readonly ICoreClientAPI capi;
        private GuiComposer? activeComposer;
        private string textKey = "";
        public override bool WantsGameTick => true;

        public WorldCellBoundsPanel(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            activeComposer = composer;
            textKey = $"{keyPrefix}-partitions";
            const double rowH = 30;
            const double gapY = 6;

            var font = CairoFont.WhiteSmallText();
            double y = 0;
            int toggleIndex = 0;

            void AddToggle(string label, Func<bool> get, Action<bool> set, string suffix)
            {
                const double labelW = 180;
                const double gap = 10;

                double x = (toggleIndex % 2) * 260;
                ElementBounds labelBounds = ElementBounds.Fixed(x, y, labelW, rowH).WithParent(bounds);
                ElementBounds toggleBounds = ElementBounds.Fixed(x + labelW + gap, y, 30, rowH).WithParent(bounds);

                var sw = new GuiElementSwitch(capi, val =>
                {
                    set(val);
                }, toggleBounds, size: 26, padding: 4);
                sw.SetValue(get());

                composer
                    .AddStaticText(label, CairoFont.WhiteDetailText(), labelBounds)
                    .AddInteractiveElement(sw, $"{keyPrefix}-{suffix}");

                if (++toggleIndex % 2 == 0) y += rowH + gapY;
            }

            AddToggle("Depth Test", () => WorldCellBoundsViewState.DepthTest, v => WorldCellBoundsViewState.DepthTest = v, "depth");
            AddToggle("Show Unloaded", () => WorldCellBoundsViewState.ShowUnloaded, v => WorldCellBoundsViewState.ShowUnloaded = v, "unloaded");
            AddToggle("Color By Desired", () => WorldCellBoundsViewState.ColorByDesiredState, v => WorldCellBoundsViewState.ColorByDesiredState = v, "colorDesired");
            AddToggle("Log measurements", () => capi.ModLoader.GetModSystem<WorldPartitionModSystem>().RecordDiagnostics,
                v => capi.ModLoader.GetModSystem<WorldPartitionModSystem>().RecordDiagnostics = v, "record");

            // Radius dropdown (small discrete choices to keep UI simple)
            string[] values = ["4", "6", "8", "10", "12", "16", "24"];
            string[] names = values;
            int selectedIndex = Array.IndexOf(values, WorldCellBoundsViewState.RadiusChunks.ToString());
            if (selectedIndex < 0) selectedIndex = 2;

            ElementBounds labelBounds = ElementBounds.Fixed(0, y, 120, rowH).WithParent(bounds);
            ElementBounds dropBounds = ElementBounds.Fixed(130, y, 90, rowH).WithParent(bounds);

            composer
                .AddStaticText("Radius (chunks)", CairoFont.WhiteDetailText(), labelBounds)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        values,
                        names,
                        selectedIndex,
                        (code, selected) =>
                        {
                            if (!selected) return;
                            if (int.TryParse(code, out int r)) WorldCellBoundsViewState.RadiusChunks = Math.Clamp(r, 1, 64);
                        },
                        dropBounds,
                        font),
                    $"{keyPrefix}-radius");
            composer.AddSmallButton("Next partition", () => { WorldCellBoundsViewState.SelectedPartition++; return true; },
                ElementBounds.Fixed(260, y, 220, rowH).WithParent(bounds), EnumButtonStyle.Normal, $"{keyPrefix}-next");
            y += rowH + gapY;
            composer.AddDynamicText(WorldCellBoundsViewState.DiagnosticsText, CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(0, y, bounds.fixedWidth, Math.Max(60, bounds.fixedHeight - y)).WithParent(bounds), textKey);
        }

        /// <summary>Displays detached render-thread observations without querying coordinator state on a GUI tick.</summary>
        public override void OnGameTick(float dt) =>
            activeComposer?.GetDynamicText(textKey)?.SetNewText(WorldCellBoundsViewState.DiagnosticsText);
    }

    private sealed partial class VgeWorldCellBoundsWireframeRenderer : IRenderer, IDisposable
    {
        private const double RenderOrderValue = 12.0;
        private const int RenderRangeValue = 1;

        private const int MaxCellsDrawn = 4096;
        private const int MaxLineVertices = MaxCellsDrawn * 24 * 2; // 12 edges, 2 vertices per edge.

        private static readonly GlPipelineDesc BoundsLinesPso = new(
            defaultMask: default(GlPipelineStateMask)
                .With(GlPipelineStateId.BlendEnable)
                .With(GlPipelineStateId.CullFaceEnable)
                .With(GlPipelineStateId.ScissorTestEnable)
                .With(GlPipelineStateId.ColorMask),
            nonDefaultMask: default(GlPipelineStateMask)
                .With(GlPipelineStateId.DepthTestEnable)
                .With(GlPipelineStateId.DepthFunc)
                .With(GlPipelineStateId.DepthWriteMask)
                .With(GlPipelineStateId.LineWidth),
            depthFunc: DepthFunction.Lequal,
            depthWriteMask: false,
            lineWidth: 2f);

        private static readonly GlPipelineDesc BoundsLinesNoDepthPso = new(
            defaultMask: default(GlPipelineStateMask)
                .With(GlPipelineStateId.DepthTestEnable)
                .With(GlPipelineStateId.BlendEnable)
                .With(GlPipelineStateId.CullFaceEnable)
                .With(GlPipelineStateId.ScissorTestEnable)
                .With(GlPipelineStateId.ColorMask),
            nonDefaultMask: default(GlPipelineStateMask)
                .With(GlPipelineStateId.DepthWriteMask)
                .With(GlPipelineStateId.LineWidth),
            depthWriteMask: false,
            lineWidth: 2f);

        private readonly ICoreClientAPI capi;
        private readonly LumOnDiagnosticsModSystem? lumOnDiagnostics;

        private GpuVao? vao;
        private GpuVbo? vbo;

        private readonly LineVertex[] vertices = new LineVertex[MaxLineVertices];
        private LumonSceneRegionCellDebugSnapshot[] snapshots = new LumonSceneRegionCellDebugSnapshot[MaxCellsDrawn];

        private readonly float[] currentViewProjMatrix = new float[16];
        private readonly float[] tempProjectionMatrix = new float[16];
        private readonly float[] tempModelViewMatrix = new float[16];

        public double RenderOrder => RenderOrderValue;
        public int RenderRange => RenderRangeValue;

        public VgeWorldCellBoundsWireframeRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
            lumOnDiagnostics = capi.ModLoader.GetModSystem<LumOnDiagnosticsModSystem>();

            // World-space lines must use the scene camera state, as the probe wireframes do.
            // AfterBlit is for screen overlays and can observe post-pass camera sway instead.
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "vge_world_cell_bounds");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.OIT)
            {
                return;
            }

            if (lumOnDiagnostics is null)
            {
                return;
            }

            var shader = capi.Shader.GetProgramByName("vge_debug_lines") as VgeDebugLinesShaderProgram;
            if (shader is null || shader.LoadError)
            {
                return;
            }

            var player = capi.World?.Player;
            if (player?.Entity is null)
            {
                return;
            }

            EnsureGlObjects();
            if (vao is null || !vao.IsValid || vbo is null || !vbo.IsValid)
            {
                return;
            }

            int got = lumOnDiagnostics.CopyLumonSceneNearRegionDebugSnapshots(snapshots);

            Vec3d camPosWorld = player.Entity.CameraPos;
            int cellSize = 32;

            int camChunkX = FloorDiv(camPosWorld.X, cellSize);
            int camChunkY = FloorDiv(camPosWorld.Y, cellSize);
            int camChunkZ = FloorDiv(camPosWorld.Z, cellSize);

            int radius = Math.Clamp(WorldCellBoundsViewState.RadiusChunks, 1, 64);
            int radius2 = radius * radius;

            int written = 0;
            AddPartitionDiagnostics(ref written, camPosWorld);
            for (int i = 0; i < got && written + 24 * 2 <= vertices.Length; i++)
            {
                var snap = snapshots[i];

                if (!WorldCellBoundsViewState.ShowUnloaded)
                {
                    WorldCellDesiredState desired = snap.DesiredState;
                    if (desired == WorldCellDesiredState.Unloaded)
                    {
                        continue;
                    }
                }

                int dx = snap.ChunkCoord.X - camChunkX;
                int dy = snap.ChunkCoord.Y - camChunkY;
                int dz = snap.ChunkCoord.Z - camChunkZ;

                long d2l = (long)dx * dx + (long)dy * dy + (long)dz * dz;
                if (d2l > radius2)
                {
                    continue;
                }

                Vector4 color = WorldCellBoundsViewState.ColorByDesiredState
                    ? GetColorForDesired(snap.DesiredState)
                    : GetColorForActual(snap.ActualState);

                double minXAbs = (double)snap.ChunkCoord.X * cellSize;
                double minYAbs = (double)snap.ChunkCoord.Y * cellSize;
                double minZAbs = (double)snap.ChunkCoord.Z * cellSize;

                float x0 = (float)(minXAbs - camPosWorld.X);
                float y0 = (float)(minYAbs - camPosWorld.Y);
                float z0 = (float)(minZAbs - camPosWorld.Z);

                float x1 = x0 + cellSize;
                float y1 = y0 + cellSize;
                float z1 = z0 + cellSize;

                AddBoxLines(ref written, x0, y0, z0, x1, y1, z1, color.X, color.Y, color.Z, color.W);
            }

            if (written <= 0)
            {
                return;
            }

            UpdateCurrentViewProjMatrix();

            int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
            using var fixedFunctionState = GlStateCache.Current.CaptureLegacyFixedFunctionState();

            bool shaderUsed = false;
            try
            {
                GlStateCache.Current.InvalidateAll();
                GlStateCache.Current.Apply(WorldCellBoundsViewState.DepthTest ? BoundsLinesPso : BoundsLinesNoDepthPso);

                shader.Use();
                shaderUsed = true;

                shader.ModelViewProjectionMatrix = currentViewProjMatrix;
                shader.WorldOffset = new Vec3f(0, 0, 0);

                int stride = Marshal.SizeOf<LineVertex>();
                vbo!.UploadData(vertices, written * stride);

                vao!.Bind();
                GL.DrawArrays(PrimitiveType.Lines, 0, written);
                GlStateCache.Current.SetLineWidth(1f);

                GL.BindVertexArray(0);
            }
            finally
            {
                if (shaderUsed)
                {
                    shader.Stop();
                }

                GL.ActiveTexture((TextureUnit)prevActiveTexture);

                GlStateCache.Current.InvalidateAll();
            }
        }

        /// <summary>Preserves the engine camera adjustment when projecting camera-relative world bounds.</summary>
        private void UpdateCurrentViewProjMatrix()
        {
            Array.Copy(capi.Render.CurrentProjectionMatrix, tempProjectionMatrix, 16);
            Array.Copy(capi.Render.CameraMatrixOriginf, tempModelViewMatrix, 16);

            // Vertices already subtract CameraPos. Keep the full view transform so bounds share terrain's camera bob.
            MatrixHelper.Multiply(tempProjectionMatrix, tempModelViewMatrix, currentViewProjMatrix);
        }

        private void EnsureGlObjects()
        {
            if (vao is not null && vao.IsValid && vbo is not null && vbo.IsValid)
            {
                return;
            }

            vao?.Dispose();
            vbo?.Dispose();
            vao = null;
            vbo = null;

            try
            {
                vao = GpuVao.Create("VGE_WorldCellBoundsLines_VAO");
                vbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_WorldCellBoundsLines_VBO");

                using var vaoScope = vao.BindScope();
                using var vboScope = vbo.BindScope();

                int stride = Marshal.SizeOf<LineVertex>();

                // vec3 position
                vao.AttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, stride, 0);

                // vec4 color
                vao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 12);
            }
            catch
            {
                vao?.Dispose();
                vbo?.Dispose();
                vao = null;
                vbo = null;
            }
        }

        private static int FloorDiv(double value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            return (int)Math.Floor(value / divisor);
        }

        private void AddBoxLines(ref int written, float x0, float y0, float z0, float x1, float y1, float z1, float r, float g, float b, float a)
        {
            int w = written;

            void AddLine(float ax, float ay, float az, float bx, float by, float bz)
            {
                vertices[w++] = new LineVertex { X = ax, Y = ay, Z = az, R = r, G = g, B = b, A = a };
                vertices[w++] = new LineVertex { X = bx, Y = by, Z = bz, R = r, G = g, B = b, A = a };
            }

            // bottom (y0)
            AddLine(x0, y0, z0, x1, y0, z0);
            AddLine(x1, y0, z0, x1, y0, z1);
            AddLine(x1, y0, z1, x0, y0, z1);
            AddLine(x0, y0, z1, x0, y0, z0);

            // top (y1)
            AddLine(x0, y1, z0, x1, y1, z0);
            AddLine(x1, y1, z0, x1, y1, z1);
            AddLine(x1, y1, z1, x0, y1, z1);
            AddLine(x0, y1, z1, x0, y1, z0);

            // verticals
            AddLine(x0, y0, z0, x0, y1, z0);
            AddLine(x1, y0, z0, x1, y1, z0);
            AddLine(x1, y0, z1, x1, y1, z1);
            AddLine(x0, y0, z1, x0, y1, z1);

            written = w;
        }

        private static Vector4 GetColorForActual(WorldCellActualState state)
        {
            return state switch
            {
                WorldCellActualState.Unloaded => new Vector4(1f, 0.15f, 0.15f, 1f),
                WorldCellActualState.Loading => new Vector4(1f, 0.55f, 0.10f, 1f),
                WorldCellActualState.Loaded => new Vector4(1f, 0.95f, 0.20f, 1f),
                WorldCellActualState.Activating => new Vector4(0.20f, 0.90f, 1f, 1f),
                WorldCellActualState.Active => new Vector4(0.20f, 1f, 0.20f, 1f),
                WorldCellActualState.Unloading => new Vector4(1f, 0.20f, 1f, 1f),
                _ => new Vector4(1f, 1f, 1f, 1f)
            };
        }

        private static Vector4 GetColorForDesired(WorldCellDesiredState state)
        {
            return state switch
            {
                WorldCellDesiredState.Unloaded => new Vector4(1f, 0.15f, 0.15f, 1f),
                WorldCellDesiredState.Loaded => new Vector4(1f, 0.95f, 0.20f, 1f),
                WorldCellDesiredState.Active => new Vector4(0.20f, 1f, 0.20f, 1f),
                _ => new Vector4(1f, 1f, 1f, 1f)
            };
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);

            vao?.Dispose();
            vbo?.Dispose();
            vao = null;
            vbo = null;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct LineVertex
        {
            public float X;
            public float Y;
            public float Z;
            public float R;
            public float G;
            public float B;
            public float A;
        }
    }
}
