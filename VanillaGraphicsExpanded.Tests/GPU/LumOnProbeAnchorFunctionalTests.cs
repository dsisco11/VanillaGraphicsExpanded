using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
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
[Trait("Category", "GPU")]
public class LumOnProbeAnchorFunctionalTests : RenderTestBase, IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;
    private readonly ShaderTestFramework _framework;
    
    // Test configuration constants
    private const int ScreenWidth = LumOnTestInputFactory.ScreenWidth;   // 4
    private const int ScreenHeight = LumOnTestInputFactory.ScreenHeight; // 4
    private const int ProbeGridWidth = LumOnTestInputFactory.ProbeGridWidth;   // 2
    private const int ProbeGridHeight = LumOnTestInputFactory.ProbeGridHeight; // 2
    private const int ProbeSpacing = 2;  // Pixels per probe cell (4÷2 = 2)
    
    private const float ZNear = LumOnTestInputFactory.DefaultZNear;  // 0.1
    private const float ZFar = LumOnTestInputFactory.DefaultZFar;    // 100
    private const float TestEpsilon = 1e-3f;  // Tolerance for float comparisons
    
    // Depth discontinuity threshold for edge detection
    private const float DepthDiscontinuityThreshold = 0.1f;

    public LumOnProbeAnchorFunctionalTests(HeadlessGLFixture fixture) : base(fixture)
    {
        _fixture = fixture;
        _framework = new ShaderTestFramework();

        if (_fixture.IsContextValid)
        {
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
            var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaderincludes");

            if (Directory.Exists(shaderPath) && Directory.Exists(includePath))
            {
                _helper = new ShaderTestHelper(shaderPath, includePath);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _helper?.Dispose();
            _framework.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the probe anchor shader.
    /// </summary>
    private int CompileProbeAnchorShader()
    {
        var result = _helper!.CompileAndLink("lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh");
        Assert.True(result.IsSuccess, $"Shader compilation failed: {result.ErrorMessage}");
        return result.ProgramId;
    }

    /// <summary>
    /// Sets up common uniforms for the probe anchor shader.
    /// </summary>
    private void SetupProbeAnchorUniforms(
        int programId, 
        float[] invProjection, 
        float[] invView,
        int depthUnit = 0,
        int normalUnit = 1)
    {
        GL.UseProgram(programId);

        // Matrix uniforms
        var invProjLoc = GL.GetUniformLocation(programId, "invProjectionMatrix");
        var invViewLoc = GL.GetUniformLocation(programId, "invViewMatrix");
        GL.UniformMatrix4(invProjLoc, 1, false, invProjection);
        GL.UniformMatrix4(invViewLoc, 1, false, invView);

        // Probe grid uniforms
        var spacingLoc = GL.GetUniformLocation(programId, "probeSpacing");
        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        var screenSizeLoc = GL.GetUniformLocation(programId, "screenSize");
        GL.Uniform1(spacingLoc, ProbeSpacing);
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform2(screenSizeLoc, (float)ScreenWidth, (float)ScreenHeight);

        // Z-plane uniforms
        var zNearLoc = GL.GetUniformLocation(programId, "zNear");
        var zFarLoc = GL.GetUniformLocation(programId, "zFar");
        GL.Uniform1(zNearLoc, ZNear);
        GL.Uniform1(zFarLoc, ZFar);

        // Edge detection threshold
        var thresholdLoc = GL.GetUniformLocation(programId, "depthDiscontinuityThreshold");
        GL.Uniform1(thresholdLoc, DepthDiscontinuityThreshold);

        // Texture sampler uniforms
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        var normalLoc = GL.GetUniformLocation(programId, "gBufferNormal");
        GL.Uniform1(depthLoc, depthUnit);
        GL.Uniform1(normalLoc, normalUnit);

        GL.UseProgram(0);
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
    private static Vector3 ReconstructViewPos(float u, float v, float depth, float[] invProjection)
    {
        // Convert column-major float[] to Matrix4x4 using MatrixHelper
        var invProjMatrix = MatrixHelper.FromColumnMajor(invProjection);
        
        // NDC coordinates (homogeneous)
        var ndc = new Vector4(
            u * 2.0f - 1.0f,
            v * 2.0f - 1.0f,
            depth * 2.0f - 1.0f,
            1.0f);

        // Transform by inverse projection
        var viewPos = Vector4.Transform(ndc, invProjMatrix);

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
    private static Vector3 TransformToWorld(Vector3 viewPos, float[] invView)
    {
        var invViewMatrix = MatrixHelper.FromColumnMajor(invView);
        return Vector3.Transform(viewPos, invViewMatrix);
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

    /// <summary>
    /// Decodes a normal from G-buffer format [0,1] to [-1,1].
    /// </summary>
    private static (float x, float y, float z) DecodeNormal(float r, float g, float b)
    {
        float x = r * 2.0f - 1.0f;
        float y = g * 2.0f - 1.0f;
        float z = b * 2.0f - 1.0f;
        float len = MathF.Sqrt(x * x + y * y + z * z);
        if (len > 0.001f)
        {
            x /= len;
            y /= len;
            z /= len;
        }
        return (x, y, z);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals for G-buffer format
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output MRT GBuffer (position + normal)
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile shader and set uniforms
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Bind inputs and render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var positionData = outputGBuffer[0].ReadPixels();

        // Verify each probe's world position
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;

                // Calculate expected world position using MatrixHelper-based methods
                var (u, v) = ProbeToScreenUV(px, py);
                var viewPos = ReconstructViewPos(u, v, testDepth, invProjection);
                var expectedPos = TransformToWorld(viewPos, invView);

                float actualX = positionData[idx + 0];
                float actualY = positionData[idx + 1];
                float actualZ = positionData[idx + 2];
                float validity = positionData[idx + 3];

                // Assert position within tolerance
                Assert.True(MathF.Abs(actualX - expectedPos.X) < TestEpsilon,
                    $"Probe ({px},{py}) X mismatch: expected {expectedPos.X}, got {actualX}");
                Assert.True(MathF.Abs(actualY - expectedPos.Y) < TestEpsilon,
                    $"Probe ({px},{py}) Y mismatch: expected {expectedPos.Y}, got {actualY}");
                Assert.True(MathF.Abs(actualZ - expectedPos.Z) < TestEpsilon,
                    $"Probe ({px},{py}) Z mismatch: expected {expectedPos.Z}, got {actualZ}");

                // Validity should be 1.0 for uniform depth (no edges)
                Assert.True(validity >= 0.9f,
                    $"Probe ({px},{py}) should be valid, got validity={validity}");
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output MRT GBuffer
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile and setup
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back normal output (second attachment)
        var normalOutput = outputGBuffer[1].ReadPixels();

        // Expected: upward normal (0, 1, 0) encoded as (0.5, 1.0, 0.5)
        var (expectedR, expectedG, expectedB) = EncodeNormal(0f, 1f, 0f);

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;

                float actualR = normalOutput[idx + 0];
                float actualG = normalOutput[idx + 1];
                float actualB = normalOutput[idx + 2];

                // Decode and compare
                var (actualNx, actualNy, actualNz) = DecodeNormal(actualR, actualG, actualB);

                Assert.True(MathF.Abs(actualNx - 0f) < TestEpsilon,
                    $"Probe ({px},{py}) normal X mismatch: expected 0, got {actualNx}");
                Assert.True(MathF.Abs(actualNy - 1f) < TestEpsilon,
                    $"Probe ({px},{py}) normal Y mismatch: expected 1, got {actualNy}");
                Assert.True(MathF.Abs(actualNz - 0f) < TestEpsilon,
                    $"Probe ({px},{py}) normal Z mismatch: expected 0, got {actualNz}");
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile and setup
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var positionData = outputGBuffer[0].ReadPixels();

        // Verify all probes are valid
        int validCount = 0;
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                float validity = positionData[idx + 3];

                // Valid probes should have validity >= 0.5 (could be 0.5 for edges, 1.0 for interior)
                Assert.True(validity >= 0.5f,
                    $"Probe ({px},{py}) should be valid (validity >= 0.5), got {validity}");

                if (validity > 0.5f)
                    validCount++;
            }
        }

        // With uniform depth, we expect all probes to be fully valid (no edges detected)
        Assert.True(validCount > 0, "At least some probes should be fully valid (validity = 1.0)");

        GL.DeleteProgram(programId);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float skyDepth = 1.0f;

        // Create input textures - sky depth everywhere, use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(skyDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile and setup
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var positionData = outputGBuffer[0].ReadPixels();

        // Verify all probes are invalid
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;

                float posX = positionData[idx + 0];
                float posY = positionData[idx + 1];
                float posZ = positionData[idx + 2];
                float validity = positionData[idx + 3];

                // Sky pixels should produce invalid probes
                Assert.True(validity < 0.1f,
                    $"Probe ({px},{py}) should be invalid for sky depth, got validity={validity}");

                // Position should be zeroed for invalid probes
                Assert.True(MathF.Abs(posX) < TestEpsilon,
                    $"Probe ({px},{py}) invalid probe X should be 0, got {posX}");
                Assert.True(MathF.Abs(posY) < TestEpsilon,
                    $"Probe ({px},{py}) invalid probe Y should be 0, got {posY}");
                Assert.True(MathF.Abs(posZ) < TestEpsilon,
                    $"Probe ({px},{py}) invalid probe Z should be 0, got {posZ}");
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        // Create input textures - checkerboard creates depth edges at every probe
        var depthData = LumOnTestInputFactory.CreateDepthBufferCheckerboard(channels: 1);
        var normalData = CreateEncodedNormalBufferUniform(0f, 1f, 0f); // Upward normal encoded

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile and setup
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var positionData = outputGBuffer[0].ReadPixels();

        // DESIRED: With checkerboard pattern, ALL probes should detect depth discontinuities
        // Each probe's 2×2 cell contains alternating near/far depths
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                float validity = positionData[idx + 3];

                // DESIRED: Probes at depth discontinuities should have partial validity (0.5)
                Assert.True(validity > 0.4f && validity < 0.6f,
                    $"Probe ({px},{py}) should have partial validity (≈0.5) at depth edge, got {validity}");
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

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

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile and setup
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var positionData = outputGBuffer[0].ReadPixels();

        // DESIRED: All probes should be marked invalid due to zero-length normals
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                float posX = positionData[idx + 0];
                float posY = positionData[idx + 1];
                float posZ = positionData[idx + 2];
                float validity = positionData[idx + 3];
                
                // DESIRED: No NaN values in output
                Assert.False(float.IsNaN(validity), 
                    $"Probe ({px},{py}) validity should not be NaN");
                Assert.False(float.IsNaN(posX) || float.IsNaN(posY) || float.IsNaN(posZ),
                    $"Probe ({px},{py}) position should not contain NaN");
                
                // DESIRED: Invalid probes due to degenerate normal
                Assert.True(validity < 0.5f,
                    $"Probe ({px},{py}) should be invalid (validity < 0.5) due to zero-length normal, got {validity}");
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float testDepth = 0.5f;

        // Create input textures - use encoded normals
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(testDepth, channels: 1);
        var normalData = CreateEncodedNormalBufferAxisAligned();

        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create output GBuffer
        using var outputGBuffer = _framework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Compile and setup
        var programId = CompileProbeAnchorShader();
        var invProjection = LumOnTestInputFactory.CreateIdentityProjection();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupProbeAnchorUniforms(programId, invProjection, invView);

        // Render
        depthTex.Bind(0);
        normalTex.Bind(1);
        _framework.RenderQuadTo(programId, outputGBuffer);

        // Read back normal output
        var normalOutput = outputGBuffer[1].ReadPixels();

        // Expected normals per quadrant (from CreateNormalBufferAxisAligned)
        (float x, float y, float z)[] expectedNormals =
        [
            (1f, 0f, 0f),   // Probe (0,0) - Top-left quadrant: +X
            (0f, 1f, 0f),   // Probe (1,0) - Top-right quadrant: +Y
            (0f, 0f, 1f),   // Probe (0,1) - Bottom-left quadrant: +Z
            (0f, -1f, 0f)   // Probe (1,1) - Bottom-right quadrant: -Y
        ];

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int probeIdx = py * ProbeGridWidth + px;
                int idx = probeIdx * 4;

                var (expectedNx, expectedNy, expectedNz) = expectedNormals[probeIdx];
                var (actualNx, actualNy, actualNz) = DecodeNormal(
                    normalOutput[idx + 0],
                    normalOutput[idx + 1],
                    normalOutput[idx + 2]);

                Assert.True(MathF.Abs(actualNx - expectedNx) < TestEpsilon,
                    $"Probe ({px},{py}) normal X mismatch: expected {expectedNx}, got {actualNx}");
                Assert.True(MathF.Abs(actualNy - expectedNy) < TestEpsilon,
                    $"Probe ({px},{py}) normal Y mismatch: expected {expectedNy}, got {actualNy}");
                Assert.True(MathF.Abs(actualNz - expectedNz) < TestEpsilon,
                    $"Probe ({px},{py}) normal Z mismatch: expected {expectedNz}, got {actualNz}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion
}
