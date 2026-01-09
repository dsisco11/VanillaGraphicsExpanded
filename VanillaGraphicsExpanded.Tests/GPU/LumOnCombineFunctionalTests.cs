using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Combine/Integrate shader pass.
/// 
/// These tests verify that the combine shader correctly:
/// - Adds indirect diffuse to direct lighting
/// - Modulates indirect contribution by surface albedo
/// - Reduces indirect for metallic surfaces (metals don't receive diffuse)
/// - Applies indirectIntensity and indirectTint to the indirect contribution
/// - Passes through direct lighting when lumOnEnabled=0
/// - Skips indirect for sky pixels
/// 
/// Test configuration:
/// - Full-res: 4×4 pixels
/// </summary>
/// <remarks>
/// The combine formula from lumon_material.fsh:
/// <code>
/// diffuseWeight = 1.0 - metallic
/// indirectContrib = indirect * albedo * diffuseWeight * intensity * tint
/// output = directLight + indirectContrib
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnCombineFunctionalTests : LumOnShaderFunctionalTestBase
{
    public LumOnCombineFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the combine shader.
    /// </summary>
    private int CompileCombineShader() => CompileShader("lumon_combine.vsh", "lumon_combine.fsh");

    /// <summary>
    /// Sets up common uniforms for the combine shader.
    /// </summary>
    private void SetupCombineUniforms(
        int programId,
        float indirectIntensity = 1.0f,
        (float r, float g, float b) indirectTint = default,
        int lumOnEnabled = 1)
    {
        GL.UseProgram(programId);

        // Intensity and tint
        var intensityLoc = GL.GetUniformLocation(programId, "indirectIntensity");
        var tintLoc = GL.GetUniformLocation(programId, "indirectTint");
        var enabledLoc = GL.GetUniformLocation(programId, "lumOnEnabled");
        
        GL.Uniform1(intensityLoc, indirectIntensity);
        var tint = indirectTint == default ? (1.0f, 1.0f, 1.0f) : indirectTint;
        GL.Uniform3(tintLoc, tint.Item1, tint.Item2, tint.Item3);
        GL.Uniform1(enabledLoc, lumOnEnabled);

        // Texture sampler uniforms
        var sceneDirectLoc = GL.GetUniformLocation(programId, "sceneDirect");
        var indirectLoc = GL.GetUniformLocation(programId, "indirectDiffuse");
        var albedoLoc = GL.GetUniformLocation(programId, "gBufferAlbedo");
        var materialLoc = GL.GetUniformLocation(programId, "gBufferMaterial");
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        GL.Uniform1(sceneDirectLoc, 0);
        GL.Uniform1(indirectLoc, 1);
        GL.Uniform1(albedoLoc, 2);
        GL.Uniform1(materialLoc, 3);
        GL.Uniform1(depthLoc, 4);

        GL.UseProgram(0);
    }

    /// <summary>
    /// Reads a pixel from output (using ScreenWidth for stride).
    /// </summary>
    private static (float r, float g, float b, float a) ReadPixelScreen(float[] data, int x, int y)
    {
        int idx = (y * ScreenWidth + x) * 4;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    #endregion

    #region Test: BasicComposite_AddsIndirect

    /// <summary>
    /// Tests that direct and indirect lighting are correctly combined.
    /// 
    /// DESIRED BEHAVIOR:
    /// - output = direct + (indirect * albedo * diffuseWeight * intensity * tint)
    /// - With white albedo, full diffuse weight (metallic=0), intensity=1, tint=white:
    ///   output = direct + indirect
    /// 
    /// Setup:
    /// - Direct: (1, 0, 0) = red
    /// - Indirect: (0, 1, 0) = green
    /// - Albedo: (1, 1, 1) = white
    /// - Metallic: 0 (dielectric, full diffuse)
    /// 
    /// Expected:
    /// - Output = (1, 1, 0) = yellow (red + green)
    /// </summary>
    [Fact]
    public void BasicComposite_AddsIndirect()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 1.0f, g: 0.0f, b: 0.0f);   // Red
        var indirect = (r: 0.0f, g: 1.0f, b: 0.0f); // Green
        var albedo = (r: 1.0f, g: 1.0f, b: 1.0f);   // White

        // Create input textures
        var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f);  // Dielectric
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);  // Valid geometry

        using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileCombineShader();
        SetupCombineUniforms(programId, indirectIntensity: 1.0f, indirectTint: (1f, 1f, 1f), lumOnEnabled: 1);

        // Bind inputs
        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: output = direct + indirect = (1,0,0) + (0,1,0) = (1,1,0)
        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                var (r, g, b, _) = ReadPixelScreen(outputData, px, py);

                Assert.True(MathF.Abs(r - 1.0f) < TestEpsilon,
                    $"Pixel ({px},{py}) R should be 1.0 (direct), got {r:F3}");
                Assert.True(MathF.Abs(g - 1.0f) < TestEpsilon,
                    $"Pixel ({px},{py}) G should be 1.0 (indirect), got {g:F3}");
                Assert.True(MathF.Abs(b - 0.0f) < TestEpsilon,
                    $"Pixel ({px},{py}) B should be 0.0, got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: AlbedoModulation_AppliedToIndirect

    /// <summary>
    /// Tests that indirect lighting is modulated by surface albedo.
    /// 
    /// DESIRED BEHAVIOR:
    /// - indirectContrib = indirect * albedo * diffuseWeight
    /// - With red albedo: only red channel of indirect passes through
    /// 
    /// Setup:
    /// - Direct: (0, 0, 0) = black (to isolate indirect)
    /// - Indirect: (1, 1, 1) = white
    /// - Albedo: (1, 0, 0) = red
    /// - Metallic: 0
    /// 
    /// Expected:
    /// - Output = (1, 0, 0) = red (white indirect filtered by red albedo)
    /// </summary>
    [Fact]
    public void AlbedoModulation_AppliedToIndirect()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.0f, g: 0.0f, b: 0.0f);   // Black
        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f); // White
        var albedo = (r: 1.0f, g: 0.0f, b: 0.0f);   // Red

        var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f);
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);

        using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileCombineShader();
        SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 1);

        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: output = indirect * albedo = (1,1,1) * (1,0,0) = (1,0,0)
        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                var (r, g, b, _) = ReadPixelScreen(outputData, px, py);

                Assert.True(MathF.Abs(r - 1.0f) < TestEpsilon,
                    $"Pixel ({px},{py}) R should be 1.0 (albedo filtered), got {r:F3}");
                Assert.True(MathF.Abs(g - 0.0f) < TestEpsilon,
                    $"Pixel ({px},{py}) G should be 0.0 (filtered by red albedo), got {g:F3}");
                Assert.True(MathF.Abs(b - 0.0f) < TestEpsilon,
                    $"Pixel ({px},{py}) B should be 0.0 (filtered by red albedo), got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: IndirectIntensity_ScalesContribution

    /// <summary>
    /// Tests that indirectIntensity scales the indirect contribution.
    /// 
    /// DESIRED BEHAVIOR:
    /// - indirectContrib = indirect * albedo * diffuseWeight * intensity
    /// - With intensity=2.0: indirect contribution is doubled
    /// 
    /// Setup:
    /// - Direct: (0, 0, 0) = black
    /// - Indirect: (0.25, 0.25, 0.25) = gray
    /// - Albedo: (1, 1, 1) = white
    /// - Metallic: 0
    /// - indirectIntensity: 2.0
    /// 
    /// Expected:
    /// - Output = (0.5, 0.5, 0.5) = doubled indirect
    /// </summary>
    [Fact]
    public void IndirectIntensity_ScalesContribution()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.0f, g: 0.0f, b: 0.0f);
        var indirect = (r: 0.25f, g: 0.25f, b: 0.25f);
        var albedo = (r: 1.0f, g: 1.0f, b: 1.0f);
        const float intensity = 2.0f;

        var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f);
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);

        using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileCombineShader();
        SetupCombineUniforms(programId, indirectIntensity: intensity, lumOnEnabled: 1);

        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: output = indirect * intensity = 0.25 * 2.0 = 0.5
        float expectedValue = indirect.r * intensity;  // 0.5

        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                var (r, g, b, _) = ReadPixelScreen(outputData, px, py);

                Assert.True(MathF.Abs(r - expectedValue) < TestEpsilon,
                    $"Pixel ({px},{py}) R should be {expectedValue:F2} (intensity scaled), got {r:F3}");
                Assert.True(MathF.Abs(g - expectedValue) < TestEpsilon,
                    $"Pixel ({px},{py}) G should be {expectedValue:F2} (intensity scaled), got {g:F3}");
                Assert.True(MathF.Abs(b - expectedValue) < TestEpsilon,
                    $"Pixel ({px},{py}) B should be {expectedValue:F2} (intensity scaled), got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: LumOnDisabled_PassthroughDirect

    /// <summary>
    /// Tests that when lumOnEnabled=0, direct lighting passes through unchanged.
    /// 
    /// DESIRED BEHAVIOR:
    /// - With lumOnEnabled=0: output = direct (no indirect added)
    /// - Feature toggle for performance or debugging
    /// 
    /// Setup:
    /// - Direct: (0.5, 0.3, 0.1)
    /// - Indirect: (1, 1, 1) = bright (should be ignored)
    /// - lumOnEnabled: 0
    /// 
    /// Expected:
    /// - Output = (0.5, 0.3, 0.1) = direct only
    /// </summary>
    [Fact]
    public void LumOnDisabled_PassthroughDirect()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.5f, g: 0.3f, b: 0.1f);
        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f);  // Should be ignored

        var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, 1f, 1f, 1f);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f);
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);

        using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileCombineShader();
        // DISABLE LumOn
        SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 0);

        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: output = direct (passthrough, no indirect)
        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                var (r, g, b, _) = ReadPixelScreen(outputData, px, py);

                Assert.True(MathF.Abs(r - direct.r) < TestEpsilon,
                    $"Pixel ({px},{py}) R should be {direct.r:F2} (passthrough), got {r:F3}");
                Assert.True(MathF.Abs(g - direct.g) < TestEpsilon,
                    $"Pixel ({px},{py}) G should be {direct.g:F2} (passthrough), got {g:F3}");
                Assert.True(MathF.Abs(b - direct.b) < TestEpsilon,
                    $"Pixel ({px},{py}) B should be {direct.b:F2} (passthrough), got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: SkyPixels_NoIndirect

    /// <summary>
    /// Tests that sky pixels pass through direct lighting without indirect contribution.
    /// 
    /// DESIRED BEHAVIOR:
    /// - For sky (depth=1.0): output = direct (no indirect added)
    /// - Sky doesn't receive bounced light from surfaces
    /// 
    /// Setup:
    /// - Direct: (0.3, 0.5, 0.8) = sky blue
    /// - Indirect: (1, 0, 0) = red (should be ignored for sky)
    /// - Depth: 1.0 (sky)
    /// 
    /// Expected:
    /// - Output = (0.3, 0.5, 0.8) = direct only
    /// </summary>
    [Fact]
    public void SkyPixels_NoIndirect()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.3f, g: 0.5f, b: 0.8f);   // Sky blue
        var indirect = (r: 1.0f, g: 0.0f, b: 0.0f); // Red (should be ignored)

        var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, 1f, 1f, 1f);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f);
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 1.0f);  // SKY

        using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileCombineShader();
        SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 1);

        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Sky pixels output direct only (no indirect for sky)
        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                var (r, g, b, _) = ReadPixelScreen(outputData, px, py);

                Assert.True(MathF.Abs(r - direct.r) < TestEpsilon,
                    $"Sky pixel ({px},{py}) R should be {direct.r:F2} (direct only), got {r:F3}");
                Assert.True(MathF.Abs(g - direct.g) < TestEpsilon,
                    $"Sky pixel ({px},{py}) G should be {direct.g:F2} (direct only), got {g:F3}");
                Assert.True(MathF.Abs(b - direct.b) < TestEpsilon,
                    $"Sky pixel ({px},{py}) B should be {direct.b:F2} (direct only), got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: MetallicSurface_ReducesIndirect

    /// <summary>
    /// Tests that metallic surfaces receive reduced indirect diffuse.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Metals don't receive diffuse lighting (they only reflect specularly)
    /// - diffuseWeight = 1.0 - metallic
    /// - With metallic=1.0: no indirect diffuse contribution
    /// 
    /// Setup:
    /// - Direct: (0.2, 0.2, 0.2) = dark gray
    /// - Indirect: (1, 1, 1) = bright white (should be blocked for metal)
    /// - Metallic: 1.0 (full metal)
    /// 
    /// Expected:
    /// - Output ≈ direct (indirect blocked by metallic)
    /// </summary>
    [Fact]
    public void MetallicSurface_ReducesIndirect()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.2f, g: 0.2f, b: 0.2f);
        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f);  // Should be blocked
        var albedo = (r: 1.0f, g: 1.0f, b: 1.0f);

        var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 1.0f);  // FULL METAL
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);

        using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileCombineShader();
        SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 1);

        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: For metallic=1.0, diffuseWeight=0, so output ≈ direct only
        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                var (r, g, b, _) = ReadPixelScreen(outputData, px, py);

                // Output should be approximately direct (metals block diffuse)
                Assert.True(MathF.Abs(r - direct.r) < TestEpsilon,
                    $"Metal pixel ({px},{py}) R should be ≈{direct.r:F2} (no indirect for metal), got {r:F3}");
                Assert.True(MathF.Abs(g - direct.g) < TestEpsilon,
                    $"Metal pixel ({px},{py}) G should be ≈{direct.g:F2} (no indirect for metal), got {g:F3}");
                Assert.True(MathF.Abs(b - direct.b) < TestEpsilon,
                    $"Metal pixel ({px},{py}) B should be ≈{direct.b:F2} (no indirect for metal), got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion
}
