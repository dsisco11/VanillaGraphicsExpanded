using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn;
using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>
/// Checks probe-atlas projection, including constant-radiance DC coefficients and vanishing directional terms.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnProbeAtlasProjectSHFunctionalTests : LumOnShaderFunctionalTestBase
{


    private const float SH_C0 = 0.282095f;

    #region Public API
    /// <summary>Creates the fixture-backed spherical-harmonics projection suite.</summary>
    public LumOnProbeAtlasProjectSHFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    /// <summary>Constant atlas radiance projects to the expected low-order coefficients with negligible directional terms.</summary>
    [Fact]
    public void AtlasToSH_ProjectsKnownAtlasCorrectly()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Flags: hit (matches LUMON_META_HIT = 1u << 0)
        const uint HitFlag = 1u;

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();

        var anchors = CreateProbeAnchors(worldZ: -1.0f, validity: 1.0f);
        var atlas = CreateUniformAtlas(1.0f, 0.5f, 0.25f, hitDist: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchors);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, meta);

        using var outSh = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f,
            TextureFormat.Rgba16f);

        var programId = CompileProjectShader();
        SetupProjectUniforms(programId, identity);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ScreenProbeAtlasMeta = metaTex;
        using var anchorInputs101 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, null);
        programId.ProbeAnchors = anchorInputs101;

        // Act
        TestFramework.RenderQuadTo(programId, outSh);

        var tex0 = outSh[0].ReadPixelsRegion(0,0,1,1);
        var tex1 = outSh[1].ReadPixelsRegion(0,0,1,1);

        // For constant radiance, directional terms should cancel out, leaving DC only.
        // Packed layout: tex0.rgb = (shR.x, shG.x, shB.x)
        var (r0, g0, b0, a0) = ReadProbeTexel(tex0, ProbeGridWidth, 0, 0);
        // Assert
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


    #endregion
    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
    }
    #endregion

    #region Private


    /// <summary>Creates the production atlas-to-SH projection program.</summary>
    private LumOnScreenProbeAtlasProjectSHShaderProgram CompileProjectShader() => Programs.Create<LumOnScreenProbeAtlasProjectSHShaderProgram>();
    /// <summary>Creates the production atlas gather program used to compare projected output.</summary>
    private LumOnScreenProbeAtlasGatherShaderProgram CompileAtlasGatherShader() => Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>();

    /// <summary>Creates probe anchors with a shared world-space depth and authored validity.</summary>
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

    /// <summary>Creates a grid of normals encoded into the atlas input representation.</summary>
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

    /// <summary>Creates a probe atlas with constant radiance and encoded hit distance.</summary>
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

    /// <summary>Creates uniform confidence and packed flags for every atlas texel.</summary>
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
        UpdateAndBindLumOnFrameUbo(programId, viewMatrix: viewMatrix);
    }

    /// <summary>Configures the production shader for controlled coefficient inputs.</summary>
    private void SetupAtlasGatherUniforms(LumOnScreenProbeAtlasGatherShaderProgram programId, float[] invProjection, float[] view)
    {
        UpdateAndBindLumOnFrameUbo(programId, invProjectionMatrix: invProjection, viewMatrix: view);
        programId.Intensity = 1; programId.IndirectTint = [1,1,1]; programId.LeakThreshold = .5f; programId.SampleStride = 1;
    }

    /// <summary>Reads one RGBA texel from a packed probe-output image.</summary>
    private static (float r, float g, float b, float a) ReadProbeTexel(float[] rgba, int w, int x, int y)
    {
        int idx = (y * w + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }

    /// <summary>Reads one texel using the test's half-resolution output width.</summary>
    private static (float r, float g, float b, float a) ReadHalfResTexel(float[] rgba, int x, int y)
    {
        int idx = (y * HalfResWidth + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }
    #endregion
}
