using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for Phase 12 Option B: probe-atlas → SH projection.
///
/// Verifies that:
/// - constant atlas projects to DC-only SH (directional terms ~ 0)
/// - SH gather using projected coefficients matches atlas gather in a simple constant case
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnProbeAtlasProjectSHFunctionalTests : LumOnShaderFunctionalTestBase
{
    private const float SH_C0 = 0.282095f;

    public LumOnProbeAtlasProjectSHFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private LumOnScreenProbeAtlasProjectSHShaderProgram CompileProjectShader() => Programs.Create<LumOnScreenProbeAtlasProjectSHShaderProgram>();
    /// <summary>Retains the explicitly skipped retired L1 comparison; no production L1 gather owner exists.</summary>
    private int CompileShGatherShader()
    {
        var result = ShaderHelper.CompileAndLink("lumon_gather.vsh", "lumon_gather.fsh");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.ProgramId;
    }
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

    /// <summary>Configures the production shader for controlled coefficient inputs.</summary>
    private void SetupProjectUniforms(LumOnScreenProbeAtlasProjectSHShaderProgram programId, float[] viewMatrix)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId, viewMatrix: viewMatrix);
    }

    /// <summary>Retains the retired L1 binding contract solely for the explicitly skipped historical comparison.</summary>
    private void SetupShGatherUniforms(int programId, float[] invProjection, float[] view)
    {
        GL.UseProgram(programId);

        GL.UniformMatrix4(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "invProjectionMatrix"), 1, false, invProjection);
        GL.UniformMatrix4(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "viewMatrix"), 1, false, view);

        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "probeSpacing"), ProbeSpacing);
        GL.Uniform2(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "probeGridSize"), (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform2(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "screenSize"), (float)ScreenWidth, (float)ScreenHeight);
        GL.Uniform2(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "halfResSize"), (float)HalfResWidth, (float)HalfResHeight);

        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "zNear"), ZNear);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "zFar"), ZFar);

        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "depthDiscontinuityThreshold"), 0.05f);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "intensity"), 1.0f);
        GL.Uniform3(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "indirectTint"), 1f, 1f, 1f);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "depthSigma"), 0.5f);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "normalSigma"), 8.0f);

        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "radianceTexture0"), 0);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "radianceTexture1"), 1);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "probeAnchorPosition"), 2);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "probeAnchorNormal"), 3);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "primaryDepth"), 4);
        GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "gBufferNormal"), 5);

        // The retired gather borrows the existing frame-buffer contract without a production shader owner.
        UpdateAndBindLumOnFrameUbo(
            null,
            invProjectionMatrix: invProjection,
            viewMatrix: view,
            probeSpacing: ProbeSpacing);

        GL.UseProgram(0);
    }

    /// <summary>Configures the production shader for controlled coefficient inputs.</summary>
    private void SetupAtlasGatherUniforms(LumOnScreenProbeAtlasGatherShaderProgram programId, float[] invProjection, float[] view)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId, invProjectionMatrix: invProjection, viewMatrix: view);
        programId.Intensity = 1; programId.IndirectTint = [1,1,1]; programId.LeakThreshold = .5f; programId.SampleStride = 1;
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

    [Fact]
    public void AtlasToSH_ProjectsKnownAtlasCorrectly()
    {
        EnsureShaderTestAvailable();

        // Flags: hit (matches LUMON_META_HIT = 1u << 0)
        const uint HitFlag = 1u;

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();

        var anchors = CreateProbeAnchors(worldZ: -1.0f, validity: 1.0f);
        var atlas = CreateUniformAtlas(1.0f, 0.5f, 0.25f, hitDist: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchors);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

        using var outSh = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileProjectShader();
        using var programIdUse = programId.UseScope();
        SetupProjectUniforms(programId, identity);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ScreenProbeAtlasMeta = metaTex;
        programId.ProbeAnchorPosition = anchorPosTex;

        TestFramework.RenderQuadTo(programId, outSh);

        var tex0 = outSh[0].ReadPixels();
        var tex1 = outSh[1].ReadPixels();

        // For constant radiance, directional terms should cancel out, leaving DC only.
        // Packed layout: tex0.rgb = (shR.x, shG.x, shB.x)
        var (r0, g0, b0, a0) = ReadProbeTexel(tex0, ProbeGridWidth, 0, 0);
        Assert.True(System.Math.Abs(r0 - SH_C0 * 1.0f) < 0.05f);
        Assert.True(System.Math.Abs(g0 - SH_C0 * 0.5f) < 0.05f);
        Assert.True(System.Math.Abs(b0 - SH_C0 * 0.25f) < 0.05f);
        Assert.True(System.Math.Abs(a0) < 0.05f);

        var (r1, g1, b1, a1) = ReadProbeTexel(tex1, ProbeGridWidth, 0, 0);
        Assert.True(System.Math.Abs(r1) < 0.05f);
        Assert.True(System.Math.Abs(g1) < 0.05f);
        Assert.True(System.Math.Abs(b1) < 0.05f);
        Assert.True(System.Math.Abs(a1) < 0.05f);
    }

    [Fact(Skip = "Option B now uses SH9 projection/gather; this L1 gather comparison is obsolete.")]
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

        // 1) Project atlas → SH
        using var outSh = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var projectId = CompileProjectShader();
        using var projectIdUse = projectId.UseScope();
        SetupProjectUniforms(projectId, identity);

        projectId.ScreenProbeAtlas = atlasTex;
        projectId.ScreenProbeAtlasMeta = metaTex;
        projectId.ProbeAnchorPosition = anchorPosTex;
        TestFramework.RenderQuadTo(projectId, outSh);

        // 2) SH gather using projected coefficients
        using var outIndirectSh = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);

        int shGatherId = CompileShGatherShader();
        SetupShGatherUniforms(shGatherId, identity, identity);

        outSh[0].Bind(0);
        outSh[1].Bind(1);
        anchorPosTex.Bind(2);
        anchorNormTex.Bind(3);
        depthTex.Bind(4);
        gbufNormTex.Bind(5);

        TestFramework.RenderQuadTo(shGatherId, outIndirectSh);
        var shOut = outIndirectSh[0].ReadPixels();

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
        var (sr, sg, sb, _) = ReadHalfResTexel(shOut, 0, 0);
        var (ar, ag, ab, _) = ReadHalfResTexel(atlasOut, 0, 0);

        Assert.True(System.Math.Abs(sr - ar) < 0.1f);
        Assert.True(System.Math.Abs(sg - ag) < 0.1f);
        Assert.True(System.Math.Abs(sb - ab) < 0.1f);
        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(shGatherId);
    }
}
