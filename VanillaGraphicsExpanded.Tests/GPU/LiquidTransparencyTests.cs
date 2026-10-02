using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks production liquid outputs against the captured six-target bucket OIT blend contract.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidTransparencyTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Bucket accumulation preserves transmission and overlapping layer order independence.</summary>
    [Theory]
    [InlineData(0f, false)]
    [InlineData(.1f, false)]
    [InlineData(.5f, false)]
    [InlineData(1f, false)]
    [InlineData(1f, true)]
    public void CapturedBucketBlendPreservesTransmission(float opacity, bool water)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();

        const int size = 32;
        using var target = CreateMRTRenderTarget(size, size,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f);
        using var aerial = DynamicTexture3D.Create(1, 2, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        terrain.UploadDataImmediate(new float[] {1,1,1,1});
        material.UploadDataImmediate([.25f,0,0,water ? 1 : 0]);
        depth.UploadDataImmediate(new float[] {.98f});
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
        var state = GlStateCache.Current;
        using var fixedFunction = state.CaptureLegacyFixedFunctionState();
        using var framebufferBinding = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        using var vao = GpuVao.Create("Test.LiquidBuckets.Vao");
        using var vertices = GpuVbo.Create(debugName: "Test.LiquidBuckets.Vertices");
        using var flags = GpuVbo.Create(debugName: "Test.LiquidBuckets.Flags");
        using var indices = GpuEbo.Create(debugName: "Test.LiquidBuckets.Indices");
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
            name: "Test.LiquidBuckets"));
        Assert.True(target.CheckStatus(out var error), error);
        var view = Matrix4x4.CreateRotationX(19f*MathF.PI/180);
        program.ModelViewMatrix = Flatten(view);

        var receiver = Vector4.Transform(new Vector4(0,-1,-5,1), view*projection);
        int x=(int)((receiver.X/receiver.W*.5f+.5f)*size), y=(int)((receiver.Y/receiver.W*.5f+.5f)*size);
        try
        {
            float[][] reference = Draw(false, opacity);
            float alpha = 1-reference[1][0];
            if (water) Assert.InRange(alpha, .001f, .99f);
            else Assert.InRange(MathF.Abs(alpha-opacity), 0, .00001f);
            float[][] single = Draw(true, opacity);
            AssertTargets(reference, single);
            float[][] second = Draw(false, .35f);
            float[][] forward = Draw(true, opacity, .35f);
            float[][] reverse = Draw(true, .35f, opacity);
            AssertTargets(forward, reverse);
            for (int targetIndex=0; targetIndex<6; targetIndex++)
            {
                if (targetIndex==2) continue; // Glow uses order-dependent alpha blending, independently of OIT color.
                for (int channel=0; channel<4; channel++)
                {
                    float expected = targetIndex<2
                        ? reference[targetIndex][channel]*second[targetIndex][channel]
                        : reference[targetIndex][channel]+second[targetIndex][channel];
                    Assert.InRange(MathF.Abs(forward[targetIndex][channel]-expected), 0, .00002f);
                }
            }
            // Model transparentcompose.fsh's three-bin resolve on the CPU; this does not execute that shader.
            Vector3 background = new(.2f,.4f,.6f);
            Vector3 resolved = Resolve(single, background);
            if (alpha==0) Assert.Equal(background, resolved);
            else
            {
                Vector3 straight = new(reference[3][0],reference[3][1],reference[3][2]);
                straight /= reference[3][3];
                Assert.InRange(Vector3.Distance(straight*alpha+background*(1-alpha), resolved),0,.0001f);
            }
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally
        {
            // Reset every indexed factor before the legacy scope restores global state.
            state.Apply(new GlPipelineDesc(
                defaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.BlendFuncIndexed),
                nonDefaultMask: default,
                blendFuncIndexed: Enumerable.Range(0,6).Select(i=>new GlBlendFuncIndexed((byte)i,GlBlendFunc.Default)).ToArray(),
                name: "Test.LiquidBuckets.Reset"));
        }

        /// <summary>Clears revealage to one and accumulation to zero, then renders layers using captured blending.</summary>
        float[][] Draw(bool blend, params float[] opacities)
        {
            for (int i=0;i<6;i++)
                target[i].UploadDataImmediate(Enumerable.Repeat(i<2 ? 1f : 0f,size*size*4).ToArray());
            var additive = new GlBlendFunc(BlendingFactorSrc.One,BlendingFactorDest.One,BlendingFactorSrc.One,BlendingFactorDest.One);
            // DstColor in the captured alpha equation selects destination alpha as well.
            var multiply = new GlBlendFunc(BlendingFactorSrc.DstColor,BlendingFactorDest.Zero,BlendingFactorSrc.DstAlpha,BlendingFactorDest.Zero);
            var glow = new GlBlendFunc(BlendingFactorSrc.SrcAlpha,BlendingFactorDest.OneMinusSrcAlpha,BlendingFactorSrc.SrcAlpha,BlendingFactorDest.OneMinusSrcAlpha);
            state.Apply(new GlPipelineDesc(
                defaultMask: blend ? default : GlPipelineStateMask.From(GlPipelineStateId.BlendEnable),
                nonDefaultMask: blend ? GlPipelineStateMask.From(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.BlendFuncIndexed) : default,
                blendFuncIndexed: blend ? [new(0,multiply),new(1,multiply),new(2,glow),new(3,additive),new(4,additive),new(5,additive)] : null,
                name: "Test.LiquidBuckets"));
            target.BindWithViewport();
            foreach (float layer in opacities)
            {
                // Distinct tints make the order test exercise color mixing rather than duplicate layers.
                terrain.UploadDataImmediate(layer==.35f ? new float[] {.3f,.6f,.9f,1} : new float[] {1,1,1,1});
                program.ForcedTransparency = 1-layer;
                using var active = program.UseScope();
                vao.DrawElements(PrimitiveType.Triangles, indices);
            }
            return Enumerable.Range(0,6).Select(i=>target[i].ReadPixelsRegion(x,y,1,1)).ToArray();
        }
    }
    #endregion

    #region Private
    /// <summary>Compares every non-glow channel from two renders.</summary>
    private static void AssertTargets(float[][] expected, float[][] actual)
    {
        for(int i=0;i<6;i++)
            if(i!=2)
                for(int j=0;j<4;j++) Assert.InRange(MathF.Abs(expected[i][j]-actual[i][j]),0,.00002f);
    }

    /// <summary>Models installed three-bin compositing and its final straight-alpha blend over an opaque background.</summary>
    private static Vector3 Resolve(float[][] targets, Vector3 background)
    {
        Vector4 combined=Vector4.Zero;
        float remaining=1;
        for(int i=0;i<3;i++)
        {
            float[] bin=targets[3+i];
            Vector3 color=bin[3]<.0001f ? Vector3.Zero : new Vector3(bin[0],bin[1],bin[2])/bin[3];
            float alpha=1-targets[0][i];
            combined+=new Vector4(color*alpha,alpha)*remaining;
            remaining*=1-alpha;
        }
        Vector3 straight=combined.W<.0001f ? Vector3.Zero : new Vector3(combined.X,combined.Y,combined.Z)/combined.W;
        float opacity=1-targets[1][0];
        return straight*opacity+background*(1-opacity);
    }

    /// <summary>Converts row-vector Numerics storage into the matching column-vector shader representation.</summary>
    private static float[] Flatten(Matrix4x4 value) => MemoryMarshal.Cast<Matrix4x4,float>(MemoryMarshal.CreateReadOnlySpan(ref value,1)).ToArray();
    #endregion
}
