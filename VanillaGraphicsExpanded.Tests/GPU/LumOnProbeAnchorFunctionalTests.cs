using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Probe Anchor shader pass.
///
/// These tests verify that the probe anchor shader correctly:
/// - Reconstructs world-space positions from depth buffer
/// - Extracts world-space normals from G-buffer
/// - Sets validity flags based on depth/normal criteria
/// - Rejects sky pixels (depth >= 0.9999)
///
/// Test configuration:
/// - Screen buffer: 4×4 pixels
/// - Probe grid: 2×2 probes (probeSpacing = 2 pixels)
/// - Each probe samples the center of its 2×2 cell
/// </summary>
/// <remarks>
/// Expected value derivation:
/// <code>
/// // For depth=0.5, zNear=0.1, zFar=100:
/// // z_ndc = depth * 2.0 - 1.0 = 0.0
/// // linearDepth = (2 * zNear * zFar) / (zFar + zNear - z_ndc * (zFar - zNear))
/// //             = (2 * 0.1 * 100) / (100 + 0.1 - 0 * 99.9)
/// //             = 20 / 100.1 ≈ 0.1998
/// //
/// // With identity projection, NDC (x,y) maps directly to clip space
/// // View-space position = invProj * clip_pos
/// // World-space position = invView * view_pos (identity = view_pos)
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnProbeAnchorFunctionalTests : LumOnShaderFunctionalTestBase
{


    // Additional test-specific constants (those not in base class)
    private new const float TestEpsilon = 1e-3f;  // Tighter tolerance for this test
    private const float DepthDiscontinuityThreshold = 0.1f;

    public LumOnProbeAnchorFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the probe anchor shader.
    /// </summary>
    private LumOnProbeAnchorShaderProgram CompileProbeAnchorShader() => Programs.Create<LumOnProbeAnchorShaderProgram>();

    /// <summary>
    /// Sets up common uniforms for the probe anchor shader.
    /// </summary>
    private void SetupProbeAnchorUniforms(
        LumOnProbeAnchorShaderProgram programId,
        float[] invProjection,
        float[] invView,
        int frameIndex = 0,
        bool anchorJitterEnabled = false,
        float anchorJitterScale = 0.0f,
        int depthUnit = 0,
        int normalUnit = 1,
        int pmjUnit = 2,
        int pmjCycleLength = DefaultPmjCycleLength)
    {
        programId.DepthDiscontinuityThreshold = DepthDiscontinuityThreshold;
        programId.PmjJitter = GetOrCreatePmjJitterTexture(pmjCycleLength);
        var projection = LumOnTestInputFactory.CreateInverseMatrix(invProjection);
        var view = LumOnTestInputFactory.CreateInverseMatrix(invView);
        UpdateAndBindLumOnFrameUbo(
            programId,
            invProjectionMatrix: invProjection,
            projectionMatrix: projection,
            viewMatrix: view,
            invViewMatrix: invView,
            frameIndex: frameIndex,
            anchorJitterEnabled: anchorJitterEnabled ? 1 : 0,
            pmjCycleLength: pmjCycleLength,
            anchorJitterScale: anchorJitterScale);

    }

    [Fact]
    public void AnchorJitter_ChangesSampleLocation_Deterministically()
    {
        EnsureShaderTestAvailable();

        // Depth buffer with a simple spatial gradient so different sample locations produce different outputs.
        var depthData = new float[ScreenWidth * ScreenHeight];
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                depthData[y * ScreenWidth + x] = 0.2f + 0.01f * (y * ScreenWidth + x);
            }
        }

        // Valid normals everywhere
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();

        // Enable jitter with a scale large enough to move sampling between neighboring pixels.
        const float jitterScale = 0.49f;

        // Render twice with same frameIndex: results must match (deterministic jitter).
        SetupProbeAnchorUniforms(programId, invProjection, invView, frameIndex: 0, anchorJitterEnabled: true, anchorJitterScale: jitterScale);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput1 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput1;
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var pos0 = outputGBuffer[0].ReadPixelsRegion(0,0,1,1);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var pos0b = outputGBuffer[0].ReadPixelsRegion(0,0,1,1);

        // Compare probe (0,0) output
        for (int i = 0; i < 4; i++)
        {
            Assert.True(MathF.Abs(pos0[i] - pos0b[i]) < TestEpsilon,
                $"Determinism mismatch at component {i}: {pos0[i]} vs {pos0b[i]}");
        }

        // Render with a different frameIndex: result should change for at least one component.
        SetupProbeAnchorUniforms(programId, invProjection, invView, frameIndex: 1, anchorJitterEnabled: true, anchorJitterScale: jitterScale);
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var pos1 = outputGBuffer[0].ReadPixelsRegion(0,0,1,1);

        bool changed =
            MathF.Abs(pos0[0] - pos1[0]) > 1e-4f ||
            MathF.Abs(pos0[1] - pos1[1]) > 1e-4f ||
            MathF.Abs(pos0[2] - pos1[2]) > 1e-4f;

        Assert.True(changed, "Expected probe (0,0) position to change when frameIndex changes with jitter enabled.");
    }

    /// <summary>
    /// Linearizes depth from the depth buffer using the same formula as the shader.
    /// </summary>
    /// <remarks>
    /// linearDepth = (2 * zNear * zFar) / (zFar + zNear - z_ndc * (zFar - zNear))
    /// where z_ndc = depth * 2.0 - 1.0
    /// </remarks>
    private static float LinearizeDepth(float depth, float zNear, float zFar)
    {
        float z = depth * 2.0f - 1.0f;
        return (2.0f * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
    }

    /// <summary>
    /// Reconstructs view-space position from screen UV and depth.
    /// Uses System.Numerics.Matrix4x4 and MatrixHelper for clean matrix math.
    /// This mirrors the lumonReconstructViewPos function in the shader.
    /// </summary>
    /// <remarks>
    /// GLSL does: viewPos = invProjectionMatrix * ndc (column vector convention)
    /// C# Vector4.Transform does: v * M (row vector convention)
    ///
    /// Since the input is column-major (GLSL format), and FromColumnMajor transposes
    /// it for System.Numerics row-major storage, we need to use the transposed matrix
    /// with Vector4.Transform to match GLSL's post-multiplication behavior.
    ///
    /// Effectively: GLSL (M * v) == C# (v * M^T)
    /// Since FromColumnMajor gives us M (the GLSL matrix transposed), we transpose again.
    /// </remarks>
    private static Vector3 ReconstructViewPos(float u, float v, float depth, float[] invProjection)
    {
        // Convert column-major float[] to Matrix4x4 using MatrixHelper
        // This transposes the data for System.Numerics' row-major storage
        var invProjMatrix = MatrixHelper.FromColumnMajor(invProjection);

        // Transpose to match GLSL's post-multiplication convention
        // GLSL: M * v == C#: v * M^T, and invProjMatrix is already transposed from column-major
        // So we need to transpose again to get the correct result
        var invProjForGlsl = Matrix4x4.Transpose(invProjMatrix);

        // NDC coordinates (homogeneous)
        var ndc = new Vector4(
            u * 2.0f - 1.0f,
            v * 2.0f - 1.0f,
            depth * 2.0f - 1.0f,
            1.0f);

        // Transform by inverse projection (now matches GLSL behavior)
        var viewPos = Vector4.Transform(ndc, invProjForGlsl);

        // Perspective divide
        if (MathF.Abs(viewPos.W) > 1e-6f)
        {
            return new Vector3(viewPos.X, viewPos.Y, viewPos.Z) / viewPos.W;
        }

        return new Vector3(viewPos.X, viewPos.Y, viewPos.Z);
    }

    /// <summary>
    /// Transforms a view-space position to world-space using the inverse view matrix.
    /// Uses System.Numerics.Matrix4x4 and MatrixHelper for clean matrix math.
    /// </summary>
    /// <remarks>
    /// Uses same GLSL-compatible convention as ReconstructViewPos.
    /// </remarks>
    private static Vector3 TransformToWorld(Vector3 viewPos, float[] invView)
    {
        var invViewMatrix = MatrixHelper.FromColumnMajor(invView);
        var invViewForGlsl = Matrix4x4.Transpose(invViewMatrix);
        return Vector3.Transform(viewPos, invViewForGlsl);
    }

    /// <summary>
    /// Calculates the screen UV that a probe at the given grid coordinate samples.
    /// This mirrors lumonProbeToScreenUV from the shader.
    /// </summary>
    private static (float u, float v) ProbeToScreenUV(int probeX, int probeY)
    {
        float screenX = (probeX + 0.5f) * ProbeSpacing;
        float screenY = (probeY + 0.5f) * ProbeSpacing;
        return (screenX / ScreenWidth, screenY / ScreenHeight);
    }

    /// <summary>Checks every decoded normal with the original float normalization and strict per-component tolerance.</summary>
    private void AssertDecodedNormals(GpuTexture image,float[] expected)
    {
        using var oracle=TestFramework.CreateTexture(ProbeGridWidth,ProbeGridHeight,TextureFormat.Rgba32f,expected);
        ImageAssertions.Predicates.Predicate(image, SurfacePredicates.LumOnProbeAnchorFunctionalTests_AssertDecodedNormals,oracle,parameters:new(TestEpsilon,0,0,0)).AssertEqual();
    }
    /// <summary>
    /// Encodes a normal to G-buffer format [-1,1] to [0,1].
    /// </summary>
    private static (float r, float g, float b) EncodeNormal(float x, float y, float z)
    {
        return (x * 0.5f + 0.5f, y * 0.5f + 0.5f, z * 0.5f + 0.5f);
    }

    /// <summary>
    /// Creates a normal buffer with encoded normals (G-buffer format).
    /// The shader expects normals encoded in [0,1] range where 0.5 = 0, 0 = -1, 1 = +1.
    /// </summary>
    /// <param name="nx">Normal X component [-1,1]</param>
    /// <param name="ny">Normal Y component [-1,1]</param>
    /// <param name="nz">Normal Z component [-1,1]</param>
    /// <returns>Encoded normal buffer for shader input.</returns>
    private static float[] CreateEncodedNormalBufferUniform(float nx, float ny, float nz)
    {
        var (encR, encG, encB) = EncodeNormal(nx, ny, nz);
        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = encR;
            data[idx + 1] = encG;
            data[idx + 2] = encB;
            data[idx + 3] = 1.0f; // Alpha
        }

        return data;
    }

    /// <summary>
    /// Creates a normal buffer with axis-aligned encoded normals per quadrant.
    /// </summary>
    private static float[] CreateEncodedNormalBufferAxisAligned()
    {
        // Define normals for each quadrant (world-space)
        (float x, float y, float z)[] quadrantNormals =
        [
            (1f, 0f, 0f),   // Top-left: +X
            (0f, 1f, 0f),   // Top-right: +Y
            (0f, 0f, 1f),   // Bottom-left: +Z
            (0f, -1f, 0f)   // Bottom-right: -Y
        ];

        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                // Determine quadrant (0-3)
                int qx = px < ScreenWidth / 2 ? 0 : 1;
                int qy = py < ScreenHeight / 2 ? 0 : 1;
                int quadrant = qy * 2 + qx;

                var (nx, ny, nz) = quadrantNormals[quadrant];
                var (encR, encG, encB) = EncodeNormal(nx, ny, nz);

                int idx = (py * ScreenWidth + px) * 4;
                data[idx + 0] = encR;
                data[idx + 1] = encG;
                data[idx + 2] = encB;
                data[idx + 3] = 1.0f;
            }
        }

        return data;
    }

    #endregion

    #region Test: UniformDepth_ProducesCorrectWorldPositions

    /// <summary>
    /// Tests that uniform depth=0.5 with identity matrices produces correct world positions.
    ///
    /// Setup:
    /// - Depth buffer: all pixels at depth=0.5
    /// - Normals: all upward (0, 1, 0) encoded as (0.5, 1.0, 0.5)
    /// - Matrices: identity projection and view
    ///
    /// Expected:
    /// - Each probe's outPosition.xyz should match hand-calculated world coordinates
    /// - outPosition.w should be 1.0 (valid)
    /// </summary>
    [Fact]
    public void UniformDepth_ProducesCorrectWorldPositions()
    {
        EnsureShaderTestAvailable();

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals for G-buffer format
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output MRT GBuffer (position + normal)
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile shader and set uniforms - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Bind inputs and render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput2 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput2;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var expected=new float[ProbeGridWidth*ProbeGridHeight*4];
        for(int py=0;py<ProbeGridHeight;py++)for(int px=0;px<ProbeGridWidth;px++)
        {
            var(u,v)=ProbeToScreenUV(px,py);
            var position=TransformToWorld(ReconstructViewPos(u,v,testDepth,invProjection),invView);
            int i=(py*ProbeGridWidth+px)*4;
            expected[i]=position.X;expected[i+1]=position.Y;expected[i+2]=position.Z;
        }
        using var oracle=TestFramework.CreateTexture(ProbeGridWidth,ProbeGridHeight,TextureFormat.Rgba32f,expected);
        ImageAssertions.Predicates.Predicate(outputGBuffer[0], SurfacePredicates.LumOnProbeAnchorFunctionalTests_UniformDepth_ProducesCorrectWorldPositions,
            oracle,parameters:new(TestEpsilon,0,0,0)).AssertEqual();
    }

    #endregion

    #region Test: UniformDepth_ProducesCorrectNormals

    /// <summary>
    /// Tests that G-buffer normals are correctly passed through to output.
    ///
    /// Setup:
    /// - Depth buffer: uniform depth=0.5
    /// - Normals: all upward (0, 1, 0) encoded as (0.5, 1.0, 0.5)
    ///
    /// Expected:
    /// - outNormal.xyz should decode to the upward normal (0, 1, 0)
    /// </summary>
    [Fact]
    public void UniformDepth_ProducesCorrectNormals()
    {
        EnsureShaderTestAvailable();

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output MRT GBuffer
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile and setup - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput3 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput3;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        ImageAssertions.Predicates.Predicate(outputGBuffer[1],SurfacePredicates.ConstantDecodedNormal,
            parameters:new(TestEpsilon,0,1,0)).AssertEqual();
    }

    #endregion

    #region Test: ValidProbes_HaveValidityFlagSet

    /// <summary>
    /// Tests that probes with valid depth and normals have validity flag set to 1.0.
    ///
    /// Setup:
    /// - Depth buffer: uniform depth=0.5 (valid, not sky)
    /// - Normals: all upward (0, 1, 0) encoded - valid normal
    ///
    /// Expected:
    /// - outPosition.w = 1.0 for all probes (or 0.5 for edge probes)
    /// </summary>
    [Fact]
    public void ValidProbes_HaveValidityFlagSet()
    {
        EnsureShaderTestAvailable();

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile and setup - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput4 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput4;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var valid=ImageAssertions.Predicates.Predicate(outputGBuffer[0], SurfacePredicates.LumOnProbeAnchorFunctionalTests_ValidProbes_HaveValidityFlagSet);
        valid.AssertEqual();Assert.True(valid.MatchingTexels>0);
    }

    #endregion

    #region Test: SkyPixels_ProduceInvalidProbes

    /// <summary>
    /// Tests that probes sampling sky (depth >= 0.9999) are marked as invalid.
    ///
    /// Setup:
    /// - Depth buffer: all pixels at depth=1.0 (sky/far plane)
    /// - Normals: any valid encoded normal
    ///
    /// Expected:
    /// - outPosition.w = 0.0 for all probes (invalid)
    /// - outPosition.xyz should be (0, 0, 0)
    /// </summary>
    [Fact]
    public void SkyPixels_ProduceInvalidProbes()
    {
        EnsureShaderTestAvailable();

        const float skyDepth = 1.0f;

        // Create input textures - sky depth everywhere, use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(skyDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile and setup - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput5 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput5;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        ImageAssertions.Predicates.Predicate(outputGBuffer[0], SurfacePredicates.LumOnProbeAnchorFunctionalTests_SkyPixels_ProduceInvalidProbes,
            parameters:new(TestEpsilon,0,0,0)).AssertEqual();
    }

    #endregion

    #region Test: DepthDiscontinuity_ProducesPartialValidity

    /// <summary>
    /// Tests that probes at depth discontinuities are marked with partial validity (0.5).
    ///
    /// DESIRED BEHAVIOR:
    /// - When a probe's sampling neighborhood contains significant depth discontinuities,
    ///   the probe should be marked with partial validity (0.5) to indicate it's at an edge
    /// - This prevents light leaking across object boundaries
    ///
    /// Setup:
    /// - Depth buffer: checkerboard pattern (alternating 0.3 and 0.7 depth)
    /// - Screen: 4×4 pixels, Probe grid: 2×2 probes (probeSpacing=2)
    /// - Each probe samples center of its 2×2 cell, which straddles the checkerboard edge
    /// - Normals: valid upward normals (encoded)
    ///
    /// Expected:
    /// - ALL probes should have validity = 0.5 (partial) because each probe's 2×2 cell
    ///   contains both near and far depth values in the checkerboard pattern
    /// </summary>
    [Fact]
    public void DepthDiscontinuity_ProducesPartialValidity()
    {
        EnsureShaderTestAvailable();

        // Create input textures - checkerboard creates depth edges at every probe
        var depthData = LumOnTestInputFactory.CreateDepthBufferCheckerboard(channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile and setup - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput6 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput6;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        ImageAssertions.Predicates.Predicate(outputGBuffer[0], SurfacePredicates.LumOnProbeAnchorFunctionalTests_DepthDiscontinuity_ProducesPartialValidity).AssertEqual();
    }

    #endregion

    #region Test: InvalidNormals_ProduceInvalidProbes

    /// <summary>
    /// Tests that probes with invalid normals (zero-length after decoding) are marked as invalid.
    ///
    /// DESIRED BEHAVIOR:
    /// - Probes with degenerate/zero-length normals should be marked invalid (validity = 0.0)
    /// - The shader should gracefully handle edge cases without producing NaN or undefined output
    ///
    /// Setup:
    /// - Depth buffer: valid uniform depth
    /// - Normals: encoded (0.5, 0.5, 0.5) which decodes to zero vector (0, 0, 0)
    ///
    /// Expected:
    /// - outPosition.w = 0.0 (invalid due to degenerate normal)
    /// - outPosition.xyz = (0, 0, 0) for invalid probes
    /// </summary>
    /// <remarks>
    /// The shader should check for degenerate normals BEFORE calling normalize() to avoid
    /// undefined behavior. The check `length(rawNormal) &lt; epsilon` should occur on the
    /// decoded but un-normalized value.
    /// </remarks>
    [Fact]
    public void InvalidNormals_ProduceInvalidProbes()
    {
        EnsureShaderTestAvailable();

        const float testDepth = 0.5f;

        // Create input textures
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);

        // Encoded (0.5, 0.5, 0.5) decodes to (0, 0, 0) - a zero-length normal
        var normalData = new float[ScreenWidth * ScreenHeight * 4];
        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * 4;
            normalData[idx + 0] = 0.5f;  // Decodes to 0
            normalData[idx + 1] = 0.5f;  // Decodes to 0
            normalData[idx + 2] = 0.5f;  // Decodes to 0
            normalData[idx + 3] = 1.0f;
        }

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile and setup - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput7 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput7;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        ImageAssertions.Predicates.Predicate(outputGBuffer[0], SurfacePredicates.LumOnProbeAnchorFunctionalTests_InvalidNormals_ProduceInvalidProbes).AssertEqual();
    }

    #endregion

    #region Test: AxisAlignedNormals_ProducesCorrectOutput

    /// <summary>
    /// Tests that different axis-aligned normals per quadrant are correctly output.
    ///
    /// Setup:
    /// - Depth buffer: uniform valid depth
    /// - Normals: axis-aligned per quadrant (+X, +Y, +Z, -Y), encoded for G-buffer
    ///
    /// Expected:
    /// - Each probe's normal output should match its quadrant's axis-aligned normal
    /// </summary>
    [Fact]
    public void AxisAlignedNormals_ProducesCorrectOutput()
    {
        EnsureShaderTestAvailable();

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferAxisAligned();

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            TextureFormat.Rgba16f, TextureFormat.Rgba16f);

        // Compile and setup - use realistic matrices for proper depth reconstruction
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Camera at origin, looking -Z
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        programId.PrimaryDepth = depthTex;
        using var surfaceInput8 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput8;
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read back normal output
        float[] expected=[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,-1,0,0];
        AssertDecodedNormals(outputGBuffer[1],expected);
    }

    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
