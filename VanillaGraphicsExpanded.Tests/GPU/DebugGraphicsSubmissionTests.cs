using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises retained debug submission with actual primitive and texture programs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DebugGraphicsSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Alternating borrowed destinations retain metadata until an explicit rebuild or window resize retires every wrapper.</summary>
    [Fact]
    public void AlternatingTargetsRetainPublicationsUntilLifecycleChange()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        int windowSize = 8;
        var baselineApi = Api(assets);
        var render = RuntimeRenderEvents.Adapt<IRenderAPI>((method, args) => method.Name switch
        {
            "get_FrameWidth" or "get_FrameHeight" => windowSize,
            _ => method.Invoke(baselineApi.Render, args)
        });
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name == "get_Render"
            ? render : method.Invoke(baselineApi, args));
        using var first = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
        using var second = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
        using var owner = new DebugRenderTarget(api);
        first.Bind(); var firstPublication = owner.GetPass().Target;
        ulong revision = firstPublication.AttachmentRevision;
        second.Bind(); var secondPublication = owner.GetPass().Target;
        first.Bind(); Assert.Same(firstPublication, owner.GetPass().Target);
        Assert.Equal(revision, firstPublication.AttachmentRevision);

        // Engine framebuffer rebuild invalidation retires publications, never their borrowed storage.
        ScreenResourceManager.HandleScreenResize();
        var rebuilt = owner.GetPass().Target;
        Assert.NotSame(firstPublication, rebuilt);
        Assert.True(firstPublication.IsDisposed); Assert.True(secondPublication.IsDisposed);
        second.Bind(); var rebuiltSecond = owner.GetPass().Target;
        windowSize = 6;
        var resized = owner.GetPass().Target;
        Assert.NotSame(rebuiltSecond, resized);
        Assert.True(rebuilt.IsDisposed); Assert.True(rebuiltSecond.IsDisposed);
        owner.Dispose(); Assert.True(resized.IsDisposed);
        Assert.True(first.IsValid); Assert.True(second.IsValid);
        Assert.True(GL.IsTexture(first[0].TextureId)); Assert.True(GL.IsTexture(second[0].TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Line and point policies override hostile state while preserving borrowed targets through resize and teardown.</summary>
    [Theory]
    [InlineData(PrimitiveType.Lines)]
    [InlineData(PrimitiveType.Points)]
    public void PrimitiveSubmissionRestoresStateAndRefreshesTargets(PrimitiveType topology)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var api = Api(assets);
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<VgeDebugLinesShaderProgram>();
        shader.ModelViewProjectionMatrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        shader.WorldOffset = new(0, 0, 0);
        var layout = new VertexLayoutDesc([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 28),
            new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 12, 28)]);
        using var vertices = GpuVbo.Create();
        vertices.UploadData(new float[] { -.75f, 0, 0, 1, 0, 0, 1, .75f, 0, 0, 1, 0, 0, 1 });
        using var geometry = new ArrayGraphicsGeometry(layout, topology, new Dictionary<int, GpuVbo> { [0] = vertices });
        using var target = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
        using var submission = new DebugGraphicsSubmission(api);
        foreach (int size in new[] { 8, 6 })
        {
            target.Resize(size, size); target[0].UploadDataImmediate(new float[size * size * 4]);
            target.Bind(); ScreenResourceManager.HandleScreenResize();
            using var hostile = new HostileFullscreenState();
            Assert.True(submission.Draw("primitive", shader, geometry, layout, new(0, 2), topology, pointSize: 3));
            hostile.AssertRestored();
            Assert.Equal(target.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Contains(target[0].ReadPixels(), value => value > .9f);
        }
        shader.InvalidateAssets(); target.Bind();
        Assert.True(submission.Draw("primitive", shader, geometry, layout, new(0, 2), topology));
        submission.Dispose();
        Assert.True(target.IsValid); Assert.True(vertices.IsValid); Assert.True(shader.IsLinked);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>The debug texture owner draws to a borrowed image and the real window, preserving native destination bindings.</summary>
    [Fact]
    public void TextureSubmissionSupportsImageAndSurfaceTargets()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<DebugTextureShaderProgram>();
        var layout = new VertexLayoutDesc([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 20),
            new(1, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 12, 20)]);
        using var vertices = GpuVbo.Create();
        vertices.UploadData(new float[] { -1, -1, 0, 0, 0, 3, -1, 0, 2, 0, -1, 3, 0, 0, 2 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo> { [0] = vertices });
        using var source = DynamicTexture2D.CreateWithData(1, 1, PixelInternalFormat.Rgba32f, [.25f, .5f, .75f, 1]);
        using var target = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
        using var submission = new DebugGraphicsSubmission(Api(assets));
        shader.Scene = source.TextureId;
        foreach (int id in new[] { target.FboId, 0 })
        {
            StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, id);
            using var hostile = new HostileFullscreenState();
            Assert.True(submission.Draw("texture", shader, geometry, layout, new(0, 3), PrimitiveType.Triangles));
            hostile.AssertRestored(); Assert.Equal(id, GL.GetInteger(GetPName.DrawFramebufferBinding));
            if (id != 0) Assert.Equal(.25f, target[0].ReadPixels()[0]);
            else
            {
                GL.ReadBuffer(GL.GetInteger(GetPName.Doublebuffer) != 0 ? ReadBufferMode.Back : ReadBufferMode.Front);
                using var pack = StateCache.Current.SetPixelPackScope(new(1));
                byte[] pixel = new byte[4]; GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                Assert.InRange(pixel[0], (byte)63, (byte)65);
            }
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>World-probe point sprites use the production six-output blend policy and write their covered depth.</summary>
    [Fact]
    public void OrbSubmissionUsesSixRoutesProgramPointSizeAndDepthWrites()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var probes = new SurfaceLightingWorldProbeFixture();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<VgeWorldProbeOrbsPointsShaderProgram>();
        shader.EnsureWorldProbeClipmapDefines(true, 1, 1, 1, 8, 64, 2);
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        shader.ModelViewProjectionMatrix = identity; shader.WorldOffset = new(0, 0, 0); shader.CameraPos = new(0, 0, 0);
        shader.PointSize = 6; shader.FadeNear = 10; shader.FadeFar = 20; shader.ImportanceColorMode = true;
        using var frame = GpuUniformBuffer.Create(); using var world = GpuUniformBuffer.Create();
        float[] frameValues = new float[136];
        for (int matrix = 0; matrix < 6; matrix++) identity.CopyTo(frameValues, matrix * 16);
        frame.UploadData(frameValues); world.UploadData(new float[72]);
        shader.FrameUniformBuffer = frame; shader.WorldProbeUniformBuffer = world;
        shader.WorldProbeRadianceAtlas = probes.Resources.ProbeRadianceAtlas;
        shader.WorldProbeVis0 = probes.Resources.ProbeVis0; shader.WorldProbeDebugState0 = probes.Resources.ProbeDebugState0;
        probes.Resources.ProbeDebugState0.UploadDataImmediate(new float[4]);
        var layout = new VertexLayoutDesc([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 36),
            new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 12, 36),
            new(2, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 28, 36)]);
        using var vertices = GpuVbo.Create(); vertices.UploadData(new float[] { 0, 0, 0, 1, 1, 1, 1, 0, 0 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveType.Points, new Dictionary<int, GpuVbo> { [0] = vertices });
        using var target = CreateMRTRenderTarget(8, 8, PixelInternalFormat.Rgb8, PixelInternalFormat.R16f,
            PixelInternalFormat.Rgba8, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);
        using var depth = new DepthTexture(8, 8, PixelInternalFormat.DepthComponent24);
        using var depthImage = GpuFramebufferAttachment.FromTexture(depth);
        target.SetAttachment(FramebufferAttachment.DepthAttachment, depthImage);
        for (int i = 0; i < 6; i++)
            target[i].UploadDataImmediate(Enumerable.Repeat(i < 2 ? 1f : 0f, 64 * (i == 0 ? 3 : i == 1 ? 1 : 4)).ToArray());
        target.Bind(); GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ClearDepth(1); GL.Clear(ClearBufferMask.DepthBufferBit);
        StateCache.Current.InvalidateAll();
        var blending = (ColorBlendDesc[])typeof(LumOnDebugRenderer).GetField("OrbBlending", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        using var submission = new DebugGraphicsSubmission(Api(assets));
        using (var hostile = new HostileFullscreenState())
        {
            Assert.True(submission.Draw("orbs", shader, geometry, layout, new(0, 1), PrimitiveType.Points,
                depthTest: true, pointSize: 6, programPointSize: true, depthWrite: true, blending: blending));
            hostile.AssertRestored();
        }
        float[] accumulation = target[3].ReadPixels();
        Assert.True(Enumerable.Range(0, 64).Count(pixel => accumulation[pixel * 4 + 2] > .1f) > 4);
        int center = (4 * 8 + 4) * 4;
        Assert.InRange(target[1].ReadPixels()[4 * 8 + 4], 0, .001f);
        Assert.InRange(target[2].ReadPixels()[center + 3], .999f, 1.001f);
        Assert.True(target[4].ReadPixels()[center + 2] > 0);
        Assert.True(target[5].ReadPixels()[center + 2] > 0);
        Assert.InRange(depth.ReadPixels()[4 * 8 + 4], .499f, .501f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Provides window dimensions and real lifecycle events while shader assets retain their existing fixture owner.</summary>
    private static ICoreClientAPI Api(BinaryShaderApiFixture assets)
    {
        var events = new RuntimeRenderEvents();
        var render = RuntimeEngineServices.Render(8, [], () => new float[16], () => new float[16], () => { });
        return RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render,
            "get_Event" => events.Api,
            _ => method.Invoke(assets.Api, args)
        });
    }
    #endregion
}
