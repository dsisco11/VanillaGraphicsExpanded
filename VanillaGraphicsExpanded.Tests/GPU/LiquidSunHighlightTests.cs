using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Isolates camera orientation from fixed-world liquid direct sunlight.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidSunHighlightTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The same receiver retains its solar highlight when the sun crosses the top viewport edge.</summary>
    [Fact]
    public void FixedReceiverRetainsSunlightAcrossViewportEdge()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();

        const int size = 512;
        using var target = CreateMRTRenderTarget(size, size,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f);
        using var aerial = DynamicTexture3D.Create(1, 2, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        terrain.UploadDataImmediate(new float[] {1,1,1,1});
        material.UploadDataImmediate([.25f,0,0,1]);
        depth.UploadDataImmediate(new float[] {1});
        aerial.UploadDataImmediate(new float[8], 0, 0, 0, 1, 2, 1, 0);
        program.TerrainTexture = terrain.TextureId;
        program.MaterialParamsTexture = material;
        program.DepthTexture = depth.TextureId;
        program.ShadowMapNear = depth.TextureId;
        program.ShadowMapFar = depth.TextureId;
        program.AerialRadianceTexture = aerial;
        program.AerialAttenuationTexture = aerial;
        program.ShadowRanges = Vector4.Zero;
        program.SunDirection = new(Vector3.Normalize(new(0,1,-5)), 0);
        program.SolarIrradiance = new(.1f,.1f,.1f,0);
        program.EnvironmentIrradiance = Vector4.Zero;
        program.AtlasMetrics = new(1,1,1,1);
        program.DepthRangeAndFrameSize = new(.1f,100,size,size);
        program.SetCounts(0,0);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,.1f,100);
        // Numerics uses zero-to-one depth; explicitly select OpenGL's minus-one-to-one depth.
        projection.M33 = -(100+.1f)/(100-.1f);
        projection.M43 = -2*100*.1f/(100-.1f);
        program.ProjectionMatrix = Flatten(projection);
        var state = StateCache.Current;
        using var fixedFunction = state.CaptureLegacyFixedFunctionState();
        using var framebufferBinding = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        using var vao = GpuVao.Create("Test.LiquidSun.Vao");
        using var vertices = GpuVbo.Create(debugName: "Test.LiquidSun.Vertices");
        using var flags = GpuVbo.Create(debugName: "Test.LiquidSun.Flags");
        using var indices = GpuEbo.Create(debugName: "Test.LiquidSun.Indices");
        using var vertexBinding = vao.BindScope();
        // Supply every input through vertex buffers instead of mutable global constant attributes.
        using (vertices.BindScope())
        {
            vertices.UploadData(new float[] {
                -10,-1,-1, .5f,.5f, 0,0,0,1, 0,0,
                 10,-1,-1, .5f,.5f, 0,0,0,1, 0,0,
                 10,-1,-20,.5f,.5f, 0,0,0,1, 0,0,
                -10,-1,-20,.5f,.5f, 0,0,0,1, 0,0 });
            vao.AttribPointer(0, 3, VertexAttribPointerType.Float, false, 44, 0);
            vao.AttribPointer(1, 2, VertexAttribPointerType.Float, false, 44, 12);
            vao.AttribPointer(2, 4, VertexAttribPointerType.Float, false, 44, 20);
            vao.AttribPointer(4, 2, VertexAttribPointerType.Float, false, 44, 36);
        }
        using (flags.BindScope())
        {
            flags.UploadData(new int[12]);
            vao.AttribIPointer(3, 1, VertexAttribIntegerType.Int, 12, 0);
            vao.AttribIPointer(5, 1, VertexAttribIntegerType.Int, 12, 4);
            vao.AttribIPointer(6, 1, VertexAttribIntegerType.Int, 12, 8);
        }
        indices.UploadIndices(new uint[] { 0, 1, 2, 0, 2, 3 });
        state.Apply(new GlPipelineDesc(
            defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
                .With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.BlendEnable)
                .With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask),
            nonDefaultMask: default,
            name: "Test.LiquidSun"));
        Assert.True(target.CheckStatus(out var error), error);
        float a = Draw(-18.5f, out float sunA), b = Draw(-19f, out float sunB);
        TestContext.Current.TestOutputHelper!.WriteLine($"Sun NDC Y {sunA} -> {sunB}; receiver RGB {a} -> {b}; ratio {b/a}");
        Assert.True(sunA < 1 && sunB > 1,$"Sun did not cross top edge: {sunA}, {sunB}");
        Assert.True(a > .01f && b > .01f,$"Expected lit receiver: {a}, {b}");
        Assert.InRange(b/a,.9f,1.1f);
        Assert.Equal(ErrorCode.NoError,GL.GetError());

        /// <summary>Projects and reads the same receiver while only camera pitch changes.</summary>
        float Draw(float pitch, out float sunY)
        {
            var view = Matrix4x4.CreateRotationX(-pitch*MathF.PI/180);
            program.ModelViewMatrix = Flatten(view);
            using var active = program.UseScope();
            var sun = Vector4.Transform(new Vector4(0,1,-5,0), view*projection); sunY=sun.Y/sun.W;
            var receiver = Vector4.Transform(new Vector4(0,-1,-5,1),view*projection);
            int x=(int)((receiver.X/receiver.W*.5f+.5f)*size), y=(int)((receiver.Y/receiver.W*.5f+.5f)*size);
            // Status checks and texture readback release their framebuffer binding.
            target.BindWithViewport();
            target.Clear(0, 0, 0, 0);
            vao.DrawElements(PrimitiveType.Triangles, indices);
            // Attachment 3 is the first OIT radiance/weight output.
            float[] pixel = target[3].ReadPixelsRegion(x, y, 1, 1);
            return pixel[0]/pixel[3];
        }
    }
    #endregion

    #region Private
    /// <summary>Converts row-vector Numerics storage into the matching column-vector shader representation.</summary>
    private static float[] Flatten(Matrix4x4 value) => MemoryMarshal.Cast<Matrix4x4,float>(MemoryMarshal.CreateReadOnlySpan(ref value,1)).ToArray();
    #endregion
}
