using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.RHI;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Freezes production direct-lighting inputs and owns independent reference and candidate targets.</summary>
internal sealed class DirectLightingComparisonFixture : IDisposable
{
    private readonly EngineShaderPlatformScope platform = new();
    private readonly VanillaGraphicsExpanded.ModSystems.AtmosphereModSystem atmosphere = new();
    private readonly BinaryShaderApiFixture assets = new();
    private readonly RuntimeLightingPrograms programs = new();
    private readonly TestFrameLightingPublication frameLighting = new();
    private readonly VgeFrameUniformBuffer frameCamera;
    private readonly VgeLightsUniformBuffer frameLights = new();
    private readonly EngineTerrainBuffers terrain;
    private readonly DepthTexture shadow, shadowFar;
    private readonly DirectLightingBufferManager buffers;
    private readonly DirectLightingReferenceRenderer reference;
    private readonly GBufferManager gbuffer;
    private readonly PBRDirectLightingShaderProgram shader;
    internal readonly DirectLightingRenderer Candidate;
    internal DirectLightingTargets Expected { get; private set; }
    internal DirectLightingTargets Actual { get; private set; }
    internal readonly DefaultShaderUniforms Uniforms = new() { ZNear = .1f, ZFar = 100 };
    internal readonly int Width, Height;
    [ThreadStatic] private static DirectLightingComparisonFixture? activeDrawCapture;
    private readonly Harmony drawCapture = new("Tests.DirectLighting.NativeDraw");
    internal int Draws;
    internal bool FailDraw;

    #region Public API
    #region Construction
    /// <summary>Constructs ordinary engine services around built production shaders without starting the game.</summary>
    internal DirectLightingComparisonFixture(int width, int height)
    {
        Width = width; Height = height;
        atmosphere.Publish(new VanillaGraphicsExpanded.PBR.Atmosphere.AtmosphereLighting(System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.One, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero, System.Collections.Immutable.ImmutableArray.Create(0f, 0f, 0f, 1f)) { Width = 1, Height = 1 });
        terrain = new(width, height);
        shadow = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f); shadow.UploadDataImmediate([1f]);
        shadowFar = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f); shadowFar.UploadDataImmediate([1f]);
        var frames = Enumerable.Repeat<FrameBufferRef>(null!, 25).ToList(); frames[0] = terrain.Primary;
        frames[(int)EnumFrameBuffer.ShadowmapNear] = new() { DepthTextureId = shadow.TextureId };
        frames[(int)EnumFrameBuffer.ShadowmapFar] = new() { DepthTextureId = shadowFar.TextureId };
        float[] identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        float[] shadowMatrix = [0,0,0,0,0,0,0,0,0,0,0,0,.5f,.5f,.5f,1];
        Uniforms.ToShadowMapSpaceMatrixNear = shadowMatrix; Uniforms.ToShadowMapSpaceMatrixFar = shadowMatrix;
        Uniforms.ShadowRangeNear = 10; Uniforms.ShadowRangeFar = 100;
        Uniforms.ShadowZExtendNear = Uniforms.ShadowZExtendFar = 1;
        var render = RuntimeEngineServices.Render(width, frames, () => identity, () => identity, () =>
        {
            if (FailDraw) throw new ArithmeticException("Controlled native draw failure.");
            Draws++;
        }, Uniforms, nativeDraw: true);
        Mock.Get(render).SetupGet(value => value.FrameHeight).Returns(height);
        var events = new RuntimeRenderEvents();
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render, "get_Event" => events.Api, "get_Shader" => programs.Api,
            _ => method.Invoke(assets.Api, args)
        });
        programs.Initialize(api);
        gbuffer = new(api); Assert.True(gbuffer.EnsureBuffers(width, height));
        buffers = new(api); reference = new(api, gbuffer, buffers); Candidate = new(api, gbuffer, buffers);
        Expected = new(width, height); Actual = new(width, height);
        activeDrawCapture=this;
        drawCapture.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL),nameof(OpenTK.Graphics.OpenGL4.GL.DrawElementsInstancedBaseVertexBaseInstance),
            [typeof(OpenTK.Graphics.OpenGL4.PrimitiveType),typeof(int),typeof(OpenTK.Graphics.OpenGL4.DrawElementsType),typeof(IntPtr),typeof(int),typeof(int),typeof(uint)]),
            prefix:new HarmonyMethod(typeof(DirectLightingComparisonFixture),nameof(ObserveNativeDraw)));
        shader = reference.PrepareBoundaryProgram()!;
        frameCamera = TestFrameCamera.CreateFromInverseView(identity, identity, width, height, Uniforms.ZNear, Uniforms.ZFar);
        shader.FrameInputs = frameCamera;
        shader.LightsInputs = frameLights;
        frameLighting.Attach(shader);
    }

    #endregion

    #region Inputs and rendering
    /// <summary>Replaces both immutable output arrays together while retaining the authored scene inputs.</summary>
    internal void ResizeOutputs(int width, int height)
    {
        var expected = new DirectLightingTargets(width, height);
        var actual = new DirectLightingTargets(width, height);
        Expected.Dispose(); Actual.Dispose();
        Expected = expected; Actual = actual;
    }

    /// <summary>Publishes a deterministic receiver family, including explicit first-person coordinates.</summary>
    internal void Inputs(int kind, float intensity = 1)
    {
        Uniforms.PointLightsCount = kind is 3 or 6 ? 1 : 0;
        Uniforms.PointLights3 = [0, 0, 2]; Uniforms.PointLightColors3 = [intensity * 2, intensity, intensity * .5f];
        Uniforms.DropShadowIntensity = kind is 3 or 6 ? 1 : 0;
        frameLights.Capture(Uniforms.PointLightsCount, Uniforms.PointLights3, Uniforms.PointLightColors3);
        Uniforms.ShadowRangeNear = kind == 6 ? 0 : 100;
        shadow.UploadDataImmediate([kind == 3 ? .25f : 1f]);
        shadowFar.UploadDataImmediate([kind == 6 ? .25f : 1f]);
        // With the fixture's identity projection, depth zero places the receiver at
        // z=-1. Depth .5 placed it in the camera plane, giving no valid facing view.
        terrain.UploadTerrain(gbuffer, Enumerable.Repeat(kind == 0 ? 1f : 0f, Width * Height).ToArray(),
            Pixels([.5f, .5f, 1f, kind == 4 ? -1f : 0f]),
            Pixels([kind == 2 ? 0 : kind == 5 ? 1 : .5f, kind == 2 ? 1f : 0f, kind == 2 ? intensity : 0f, 0]),
            Pixels([intensity * .7f, intensity * .3f, intensity * .2f, 1]));
        Upload(gbuffer.PositionTextureId, Pixels([0, 0, -2, 1]));
        gbuffer.SurfaceTexture!.UploadDataImmediate(Pixels([1, 1, 1, 1]), 0, 0, 2, Width, Height, 1);
    }

    /// <summary>Runs the retained renderer, optionally establishing the neutral baseline before a measurement batch.</summary>
    internal void DrawReference(bool establishNeutral = true)
    {
        if (establishNeutral) Neutral();
        try { Assert.True(reference.RenderLighting(Expected)); }
        finally
        {
            // The reference uses temporary view/light slices; restore the persistent
            // fixture publication before the production renderer consumes this shader.
            shader.FrameInputs = frameCamera;
            shader.LightsInputs = frameLights;
        }
    }

    /// <summary>Establishes a complete baseline independently of the old renderer's partial descriptor.</summary>
    internal void Neutral()
    {
        var shader = reference.PrepareBoundaryProgram()!;
        var desc = new RHIGraphicsPipelineDescription(shader.Shader, EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.Rgba16f), new(TextureFormat.Rgba16f), new(TextureFormat.Rgba16f)]), DynamicPipelineState.Viewport);
        StateCache.Current.ApplyGraphicsState(desc, new() { Viewport = new() { Width = Width, Height = Height } });
    }

    #endregion

    #region Lifetime
    /// <summary>Retires renderers before the shared engine programs and images they borrow.</summary>
    public void Dispose()
    {
        drawCapture.UnpatchAll(drawCapture.Id); activeDrawCapture=null;
        Neutral(); Candidate.Dispose(); reference.Dispose(); Actual.Dispose(); Expected.Dispose();
        buffers.Dispose(); gbuffer.Dispose(); shadowFar.Dispose(); shadow.Dispose(); terrain.Dispose(); programs.Dispose();
        frameCamera.Dispose(); frameLights.Dispose(); frameLighting.Dispose();
        assets.Dispose(); atmosphere.Dispose(); platform.Dispose();
    }
    /// <summary>Exposes the production manager target after a normal frame draw.</summary>
    internal RHIRenderPassDescription Normal => buffers.LightingPass!;

    /// <summary>Makes the next reload encounter an invalid production binary, then permits explicit recovery.</summary>
    internal void FailPreparation(bool fail)
    {
        const string binary = "shaders/pbr_direct_lighting.fsh.spv";
        if (fail) assets.Overrides[binary] = new byte[20]; else assets.Overrides.Remove(binary);
        shader.InvalidateAssets();
    }

    #endregion

    #endregion

    #region Private
    /// <summary>Observes actual RHI native submissions and injects a driver-boundary failure before drawing.</summary>
    private static void ObserveNativeDraw()
    {
        if(activeDrawCapture is not { } owner)return;
        if(owner.FailDraw)throw new ArithmeticException("Controlled native draw failure.");
        owner.Draws++;
    }
    /// <summary>Replicates one frozen four-channel value over the complete image.</summary>
    private float[] Pixels(float[] value) => Enumerable.Repeat(value, Width * Height).SelectMany(pixel => pixel).ToArray();
    /// <summary>Updates production G-buffer storage through the existing unpack and texture-binding scopes.</summary>
    private static void Upload(int texture, float[] data)
    {
        using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.Float, data);
    }
    #endregion
}
