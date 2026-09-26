using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for probe-atlas to SH9 projection and gathering.
///
/// Verifies that:
/// - constant atlas projects to DC-only SH9 (directional terms ~ 0)
/// - SH9 gather using projected coefficients matches atlas gather in a simple constant case
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public partial class LumOnProbeAtlasProjectSh9FunctionalTests : LumOnShaderFunctionalTestBase
{
    private const float SH9_C0 = 0.282095f;
    private const float Pi = 3.141592654f;

    public LumOnProbeAtlasProjectSh9FunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private LumOnScreenProbeAtlasProjectSh9ShaderProgram CompileProjectShader() => Programs.Create<LumOnScreenProbeAtlasProjectSh9ShaderProgram>();
    private LumOnProbeSh9GatherShaderProgram CompileSh9GatherShader() => Programs.Create<LumOnProbeSh9GatherShaderProgram>();
    private LumOnScreenProbeAtlasGatherShaderProgram CompileAtlasGatherShader() => Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>();

    private static float[] CreateProbeAnchors(float worldZ, float validity = 1.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                data[idx + 0] = 0f;
                data[idx + 1] = 0f;
                data[idx + 2] = worldZ;
                data[idx + 3] = validity;
            }
        }
        return data;
    }

    private static float[] CreateProbeNormalsEncoded(float nx, float ny, float nz)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        float encX = nx * 0.5f + 0.5f;
        float encY = ny * 0.5f + 0.5f;
        float encZ = nz * 0.5f + 0.5f;

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = encX;
            data[idx + 1] = encY;
            data[idx + 2] = encZ;
            data[idx + 3] = 0f;
        }

        return data;
    }

    private static float[] CreateUniformAtlas(float r, float g, float b, float hitDist = 10f)
    {
        var data = new float[AtlasWidth * AtlasHeight * 4];
        float encodedDist = System.MathF.Log(hitDist + 1.0f);

        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = encodedDist;
        }

        return data;
    }

    private static float[] CreateUniformMetaAtlas(float confidence, uint flags)
    {
        var data = new float[AtlasWidth * AtlasHeight * 2];
        float flagsF = System.BitConverter.UInt32BitsToSingle(flags);
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 2;
            data[idx + 0] = confidence;
            data[idx + 1] = flagsF;
        }
        return data;
    }

    /// <summary>Initializes the production Project parameter and frame bindings.</summary>
    private void SetupProjectUniforms(LumOnScreenProbeAtlasProjectSh9ShaderProgram programId)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId);
    }

    /// <summary>Initializes the production Sh9Gather parameter and frame bindings.</summary>
    private void SetupSh9GatherUniforms(LumOnProbeSh9GatherShaderProgram programId, float[] invProjection, float[] view)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId, invProjectionMatrix: invProjection, viewMatrix: view);
        programId.Intensity = 1; programId.IndirectTint = [1,1,1];
    }

    /// <summary>Initializes the production AtlasGather parameter and frame bindings.</summary>
    private void SetupAtlasGatherUniforms(LumOnScreenProbeAtlasGatherShaderProgram programId, float[] invProjection, float[] view)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId, invProjectionMatrix: invProjection, viewMatrix: view);
        programId.Intensity = 1; programId.IndirectTint = [1,1,1];
        programId.LeakThreshold = .5f; programId.SampleStride = 1;
    }

    private static (float r, float g, float b, float a) ReadProbeTexel(float[] rgba, int w, int x, int y)
    {
        int idx = (y * w + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }

    private static (float r, float g, float b, float a) ReadHalfResTexel(float[] rgba, int x, int y)
    {
        int idx = (y * HalfResWidth + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }

    private static void UnpackSh9(
        (float r, float g, float b, float a) t0,
        (float r, float g, float b, float a) t1,
        (float r, float g, float b, float a) t2,
        (float r, float g, float b, float a) t3,
        (float r, float g, float b, float a) t4,
        (float r, float g, float b, float a) t5,
        (float r, float g, float b, float a) t6,
        out (float r, float g, float b) c0,
        out (float r, float g, float b) c1,
        out (float r, float g, float b) c2,
        out (float r, float g, float b) c3,
        out (float r, float g, float b) c4,
        out (float r, float g, float b) c5,
        out (float r, float g, float b) c6,
        out (float r, float g, float b) c7,
        out (float r, float g, float b) c8)
    {
        // Must match lumon_sh9.glsl lumonSH9Pack layout.
        c0 = (t0.r, t0.g, t0.b);
        c1 = (t0.a, t1.r, t1.g);
        c2 = (t1.b, t1.a, t2.r);
        c3 = (t2.g, t2.b, t2.a);

        c4 = (t3.r, t3.g, t3.b);
        c5 = (t3.a, t4.r, t4.g);
        c6 = (t4.b, t4.a, t5.r);
        c7 = (t5.g, t5.b, t5.a);

        c8 = (t6.r, t6.g, t6.b);
    }

    [Fact]
    public void AtlasToSH_ProjectsKnownAtlasCorrectly()
    {
        EnsureShaderTestAvailable();

        const uint HitFlag = 1u;

        var anchors = CreateProbeAnchors(worldZ: -1.0f, validity: 1.0f);
        var atlas = CreateUniformAtlas(1.0f, 0.5f, 0.25f, hitDist: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchors);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

        using var outSh9 = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileProjectShader();
        using var programUse = programId.UseScope();
        SetupProjectUniforms(programId);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ScreenProbeAtlasMeta = metaTex;
        programId.ProbeAnchorPosition = anchorPosTex;

        TestFramework.RenderQuadTo(programId, outSh9);

        var t0 = ReadProbeTexel(outSh9[0].ReadPixels(), ProbeGridWidth, 0, 0);
        var t1 = ReadProbeTexel(outSh9[1].ReadPixels(), ProbeGridWidth, 0, 0);
        var t2 = ReadProbeTexel(outSh9[2].ReadPixels(), ProbeGridWidth, 0, 0);
        var t3 = ReadProbeTexel(outSh9[3].ReadPixels(), ProbeGridWidth, 0, 0);
        var t4 = ReadProbeTexel(outSh9[4].ReadPixels(), ProbeGridWidth, 0, 0);
        var t5 = ReadProbeTexel(outSh9[5].ReadPixels(), ProbeGridWidth, 0, 0);
        var t6 = ReadProbeTexel(outSh9[6].ReadPixels(), ProbeGridWidth, 0, 0);

        UnpackSh9(t0, t1, t2, t3, t4, t5, t6,
            out var c0, out var c1, out var c2, out var c3, out var c4, out var c5, out var c6, out var c7, out var c8);

        // For constant radiance L over the sphere:
        // c0 = L * ∫ Y00 dΩ = L * (SH9_C0 * 4π)
        float expectedR0 = 1.0f * SH9_C0 * (4f * Pi);
        float expectedG0 = 0.5f * SH9_C0 * (4f * Pi);
        float expectedB0 = 0.25f * SH9_C0 * (4f * Pi);

        Assert.InRange(c0.r, expectedR0 - 0.05f, expectedR0 + 0.05f);
        Assert.InRange(c0.g, expectedG0 - 0.05f, expectedG0 + 0.05f);
        Assert.InRange(c0.b, expectedB0 - 0.05f, expectedB0 + 0.05f);

        // Directional terms should cancel out (approximately 0).
        static void AssertNearZero((float r, float g, float b) v)
        {
            Assert.InRange(System.Math.Abs(v.r), 0f, 0.1f);
            Assert.InRange(System.Math.Abs(v.g), 0f, 0.1f);
            Assert.InRange(System.Math.Abs(v.b), 0f, 0.1f);
        }

        AssertNearZero(c1);
        AssertNearZero(c2);
        AssertNearZero(c3);
        AssertNearZero(c4);
        AssertNearZero(c5);
        AssertNearZero(c6);
        AssertNearZero(c7);
        AssertNearZero(c8);
    }

    [Fact]
    public void Gather_SHFromProjected_StableVsIntegration()
    {
        EnsureShaderTestAvailable();

        const uint HitFlag = 1u;

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();

        var anchors = CreateProbeAnchors(worldZ: -1.0f, validity: 1.0f);
        var normals = CreateProbeNormalsEncoded(0, 1, 0);

        var atlas = CreateUniformAtlas(0.8f, 0.8f, 0.8f, hitDist: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        var depth = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);
        var gbufNormal = CreateUniformNormalData(ScreenWidth, ScreenHeight, 0, 1, 0);

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchors);
        using var anchorNormTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, normals);

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depth);
        using var gbufNormTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gbufNormal);

        // 1) Project atlas → SH9
        using var outSh9 = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var projectId = CompileProjectShader();
        using var projectIdUse = projectId.UseScope();
        SetupProjectUniforms(projectId);

        projectId.ScreenProbeAtlas = atlasTex;
        projectId.ScreenProbeAtlasMeta = metaTex;
        projectId.ProbeAnchorPosition = anchorPosTex;
        TestFramework.RenderQuadTo(projectId, outSh9);

        // 2) SH9 gather using projected coefficients
        using var outIndirectSh9 = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);

        var sh9GatherId = CompileSh9GatherShader();
        using var sh9GatherIdUse = sh9GatherId.UseScope();
        SetupSh9GatherUniforms(sh9GatherId, identity, identity);

        sh9GatherId.ProbeSh0 = outSh9[0];
        sh9GatherId.ProbeSh1 = outSh9[1];
        sh9GatherId.ProbeSh2 = outSh9[2];
        sh9GatherId.ProbeSh3 = outSh9[3];
        sh9GatherId.ProbeSh4 = outSh9[4];
        sh9GatherId.ProbeSh5 = outSh9[5];
        sh9GatherId.ProbeSh6 = outSh9[6];

        sh9GatherId.ProbeAnchorPosition = anchorPosTex;
        sh9GatherId.ProbeAnchorNormal = anchorNormTex;
        sh9GatherId.PrimaryDepth = depthTex.TextureId;
        sh9GatherId.GBufferNormal = gbufNormTex.TextureId;

        TestFramework.RenderQuadTo(sh9GatherId, outIndirectSh9);
        var sh9Out = outIndirectSh9[0].ReadPixels();

        // 3) Atlas gather integration
        using var outIndirectAtlas = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);

        var atlasGatherId = CompileAtlasGatherShader();
        using var atlasGatherIdUse = atlasGatherId.UseScope();
        SetupAtlasGatherUniforms(atlasGatherId, identity, identity);

        atlasGatherId.ScreenProbeAtlas = atlasTex;
        atlasGatherId.ProbeAnchorPosition = anchorPosTex;
        atlasGatherId.ProbeAnchorNormal = anchorNormTex;
        atlasGatherId.PrimaryDepth = depthTex.TextureId;
        atlasGatherId.GBufferNormal = gbufNormTex.TextureId;

        TestFramework.RenderQuadTo(atlasGatherId, outIndirectAtlas);
        var atlasOut = outIndirectAtlas[0].ReadPixels();

        // Compare one representative half-res pixel.
        var (sr, sg, sb, _) = ReadHalfResTexel(sh9Out, 0, 0);
        var (ar, ag, ab, _) = ReadHalfResTexel(atlasOut, 0, 0);

        Assert.InRange(System.Math.Abs(sr - ar), 0f, 0.1f);
        Assert.InRange(System.Math.Abs(sg - ag), 0f, 0.1f);
        Assert.InRange(System.Math.Abs(sb - ab), 0f, 0.1f);
    }
}
