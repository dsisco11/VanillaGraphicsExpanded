using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
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
    private int CompileCombineShader(
        int lumOnEnabled = 1,
        int enablePbrComposite = 0,
        int enableAO = 0,
        int enableShortRangeAo = 0) =>
        CompileShaderWithDefines(
            "lumon_combine.vsh",
            "lumon_combine.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_ENABLED"] = lumOnEnabled.ToString(),
                ["VGE_LUMON_PBR_COMPOSITE"] = enablePbrComposite.ToString(),
                ["VGE_LUMON_ENABLE_AO"] = enableAO.ToString(),
                ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = enableShortRangeAo.ToString(),
            });

    /// <summary>
    /// Compiles and links the LumOn debug shader.
    /// Used for Phase 15 composite debug views (moved out of lumon_combine).
    /// </summary>
    private int CompileDebugShader(
        int enablePbrComposite = 1,
        int enableAO = 0,
        int enableShortRangeAo = 0) =>
        CompileShaderWithDefines(
            "lumon_debug.vsh",
            "lumon_debug.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_PBR_COMPOSITE"] = enablePbrComposite.ToString(),
                ["VGE_LUMON_ENABLE_AO"] = enableAO.ToString(),
                ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = enableShortRangeAo.ToString(),
            });

    /// <summary>
    /// Sets up common uniforms for the combine shader.
    /// </summary>
    private void SetupCombineUniforms(
        int programId,
        float indirectIntensity = 1.0f,
        (float r, float g, float b) indirectTint = default,
        int lumOnEnabled = 1,
        int enablePbrComposite = 0,
        int enableAO = 0,
        float diffuseAOStrength = 1.0f,
        float specularAOStrength = 0.5f,
        float[]? invProjection = null,
        float[]? view = null)
    {
        GL.UseProgram(programId);

        // Intensity and tint
        var intensityLoc = GL.GetUniformLocation(programId, "indirectIntensity");
        var tintLoc = GL.GetUniformLocation(programId, "indirectTint");
        
        GL.Uniform1(intensityLoc, indirectIntensity);
        var tint = indirectTint == default ? (1.0f, 1.0f, 1.0f) : indirectTint;
        GL.Uniform3(tintLoc, tint.Item1, tint.Item2, tint.Item3);

        // Phase 15 composite toggles
        var diffAoLoc = GL.GetUniformLocation(programId, "diffuseAOStrength");
        var specAoLoc = GL.GetUniformLocation(programId, "specularAOStrength");
        GL.Uniform1(diffAoLoc, diffuseAOStrength);
        GL.Uniform1(specAoLoc, specularAOStrength);

        // Matrices (identity defaults are fine for deterministic testing)
        var invProjLoc = GL.GetUniformLocation(programId, "invProjectionMatrix");
        var viewLoc = GL.GetUniformLocation(programId, "viewMatrix");

        var identity = new float[]
        {
            1,0,0,0,
            0,1,0,0,
            0,0,1,0,
            0,0,0,1
        };

        GL.UniformMatrix4(invProjLoc, 1, false, invProjection ?? identity);
        GL.UniformMatrix4(viewLoc, 1, false, view ?? identity);

        // Phase 23: UBO-backed frame state.
        UpdateAndBindLumOnFrameUbo(
            programId,
            invProjectionMatrix: invProjection ?? identity,
            viewMatrix: view ?? identity);

        // Texture sampler uniforms
        var sceneDirectLoc = GL.GetUniformLocation(programId, "sceneDirect");
        var indirectLoc = GL.GetUniformLocation(programId, "indirectDiffuse");
        var albedoLoc = GL.GetUniformLocation(programId, "gBufferAlbedo");
        var materialLoc = GL.GetUniformLocation(programId, "gBufferMaterial");
        var normalLoc = GL.GetUniformLocation(programId, "gBufferNormal");
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        GL.Uniform1(sceneDirectLoc, 0);
        GL.Uniform1(indirectLoc, 1);
        GL.Uniform1(albedoLoc, 2);
        GL.Uniform1(materialLoc, 3);
        GL.Uniform1(depthLoc, 4);
        GL.Uniform1(normalLoc, 5);

        GL.UseProgram(0);
    }

    private static float[] IdentityMatrix4x4 =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];

    /// <summary>
    /// Sets up uniforms for composite debug views in lumon_debug.fsh.
    /// </summary>
    private void SetupDebugCompositeUniforms(
        int programId,
        int debugMode,
        float indirectIntensity,
        (float r, float g, float b) indirectTint,
        int enablePbrComposite,
        int enableAO,
        float diffuseAOStrength,
        float specularAOStrength,
        float[] invProjection)
    {
        GL.UseProgram(programId);

        GL.Uniform1(GL.GetUniformLocation(programId, "debugMode"), debugMode);

        // Required sizing uniforms
        GL.Uniform2(GL.GetUniformLocation(programId, "screenSize"), (float)ScreenWidth, (float)ScreenHeight);
        GL.Uniform2(GL.GetUniformLocation(programId, "probeGridSize"), (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeSpacing"), ProbeSpacing);

        GL.Uniform1(GL.GetUniformLocation(programId, "zNear"), ZNear);
        GL.Uniform1(GL.GetUniformLocation(programId, "zFar"), ZFar);

        GL.UniformMatrix4(GL.GetUniformLocation(programId, "invProjectionMatrix"), 1, false, invProjection);
        GL.UniformMatrix4(GL.GetUniformLocation(programId, "invViewMatrix"), 1, false, IdentityMatrix4x4);
        GL.UniformMatrix4(GL.GetUniformLocation(programId, "prevViewProjMatrix"), 1, false, IdentityMatrix4x4);

        // Phase 23: UBO-backed frame state.
        UpdateAndBindLumOnFrameUbo(
            programId,
            invProjectionMatrix: invProjection,
            invViewMatrix: IdentityMatrix4x4,
            viewMatrix: IdentityMatrix4x4,
            prevViewProjMatrix: IdentityMatrix4x4);

        // Required temporal uniforms (not used by composite modes)
        GL.Uniform1(GL.GetUniformLocation(programId, "temporalAlpha"), 0.9f);
        GL.Uniform1(GL.GetUniformLocation(programId, "depthRejectThreshold"), 0.1f);
        GL.Uniform1(GL.GetUniformLocation(programId, "normalRejectThreshold"), 0.9f);
        GL.Uniform1(GL.GetUniformLocation(programId, "gatherAtlasSource"), 0);

        // Composite params
        GL.Uniform1(GL.GetUniformLocation(programId, "indirectIntensity"), indirectIntensity);
        var tint = indirectTint == default ? (1.0f, 1.0f, 1.0f) : indirectTint;
        GL.Uniform3(GL.GetUniformLocation(programId, "indirectTint"), tint.Item1, tint.Item2, tint.Item3);
        _ = enablePbrComposite;
        _ = enableAO;
        GL.Uniform1(GL.GetUniformLocation(programId, "diffuseAOStrength"), diffuseAOStrength);
        GL.Uniform1(GL.GetUniformLocation(programId, "specularAOStrength"), specularAOStrength);

        // Sampler units (match LumOnDebugShaderProgram bindings)
        GL.Uniform1(GL.GetUniformLocation(programId, "primaryDepth"), 0);
        GL.Uniform1(GL.GetUniformLocation(programId, "gBufferNormal"), 1);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorPosition"), 2);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorNormal"), 3);
        GL.Uniform1(GL.GetUniformLocation(programId, "radianceTexture0"), 4);
        GL.Uniform1(GL.GetUniformLocation(programId, "radianceTexture1"), 5);
        GL.Uniform1(GL.GetUniformLocation(programId, "indirectHalf"), 6);
        GL.Uniform1(GL.GetUniformLocation(programId, "historyMeta"), 7);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAtlasMeta"), 8);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAtlasCurrent"), 9);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAtlasFiltered"), 10);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAtlasGatherInput"), 11);
        GL.Uniform1(GL.GetUniformLocation(programId, "indirectDiffuseFull"), 12);
        GL.Uniform1(GL.GetUniformLocation(programId, "gBufferAlbedo"), 13);
        GL.Uniform1(GL.GetUniformLocation(programId, "gBufferMaterial"), 14);

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

    #region Phase 15 Tests

    [Fact]
    public void Composite_Metallic0_UsesDiffuseDominant()
    {
        EnsureShaderTestAvailable();

        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f);
        var albedo = (r: 1.0f, g: 0.0f, b: 0.0f);

        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f, emissive: 0.0f, reflectivity: 1.0f);
        var normalData = CreateUniformNormalData(ScreenWidth, ScreenHeight, 0f, 0f, 1f);
        // Use a depth that produces a non-zero view vector with our test invProjection.
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.25f);

        // Column-major diag(0, 0, 1, 1) => forces reconstructed viewPos.x/y=0.
        var invProj = new float[]
        {
            0,0,0,0,
            0,0,0,0,
            0,0,1,0,
            0,0,0,1
        };

        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);

        // Bind required-but-unused debug shader textures
        var dummyRgba = CreateUniformColorData(ScreenWidth, ScreenHeight, 0f, 0f, 0f, 0f);
        using var dummyTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, dummyRgba);

        var programId = CompileDebugShader(enablePbrComposite: 1, enableAO: 0);

        // Diffuse debug view
        SetupDebugCompositeUniforms(programId,
            debugMode: (int)LumOnDebugMode.CompositeIndirectDiffuse,
            indirectIntensity: 1.0f,
            indirectTint: (1f, 1f, 1f),
            enablePbrComposite: 1,
            enableAO: 0,
            diffuseAOStrength: 1.0f,
            specularAOStrength: 1.0f,
            invProjection: invProj);

        depthTex.Bind(0);
        normalTex.Bind(1);
        dummyTex.Bind(2);
        dummyTex.Bind(3);
        dummyTex.Bind(4);
        dummyTex.Bind(5);
        dummyTex.Bind(6);
        dummyTex.Bind(7);
        dummyTex.Bind(8);
        dummyTex.Bind(9);
        dummyTex.Bind(10);
        dummyTex.Bind(11);
        indirectTex.Bind(12);
        albedoTex.Bind(13);
        materialTex.Bind(14);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var diffuseOut = outputGBuffer[0].ReadPixels();

        // Specular debug view
        SetupDebugCompositeUniforms(programId,
            debugMode: (int)LumOnDebugMode.CompositeIndirectSpecular,
            indirectIntensity: 1.0f,
            indirectTint: (1f, 1f, 1f),
            enablePbrComposite: 1,
            enableAO: 0,
            diffuseAOStrength: 1.0f,
            specularAOStrength: 1.0f,
            invProjection: invProj);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var specOut = outputGBuffer[0].ReadPixels();

        // Compare at center-ish pixel for stability
        var (dr, dg, db, _) = ReadPixelScreen(diffuseOut, 2, 2);
        var (sr, sg, sb, _) = ReadPixelScreen(specOut, 2, 2);

        float diffuseLuma = dr + dg + db;
        float specLuma = sr + sg + sb;

        Assert.True(diffuseLuma > specLuma,
            $"Expected diffuse to dominate for metallic=0. Diffuse={diffuseLuma:F3}, Spec={specLuma:F3}");

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Composite_Metallic1_UsesSpecularDominant()
    {
        EnsureShaderTestAvailable();

        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f);
        var albedo = (r: 0.8f, g: 0.2f, b: 0.1f);

        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.1f, metallic: 1.0f, emissive: 0.0f, reflectivity: 1.0f);
        var normalData = CreateUniformNormalData(ScreenWidth, ScreenHeight, 0f, 0f, 1f);
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.25f);

        var invProj = new float[]
        {
            0,0,0,0,
            0,0,0,0,
            0,0,1,0,
            0,0,0,1
        };

        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);

        var dummyRgba = CreateUniformColorData(ScreenWidth, ScreenHeight, 0f, 0f, 0f, 0f);
        using var dummyTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, dummyRgba);

        var programId = CompileDebugShader(enablePbrComposite: 1, enableAO: 0);

        SetupDebugCompositeUniforms(programId,
            debugMode: (int)LumOnDebugMode.CompositeIndirectDiffuse,
            indirectIntensity: 1.0f,
            indirectTint: (1f, 1f, 1f),
            enablePbrComposite: 1,
            enableAO: 0,
            diffuseAOStrength: 1.0f,
            specularAOStrength: 1.0f,
            invProjection: invProj);

        depthTex.Bind(0);
        normalTex.Bind(1);
        dummyTex.Bind(2);
        dummyTex.Bind(3);
        dummyTex.Bind(4);
        dummyTex.Bind(5);
        dummyTex.Bind(6);
        dummyTex.Bind(7);
        dummyTex.Bind(8);
        dummyTex.Bind(9);
        dummyTex.Bind(10);
        dummyTex.Bind(11);
        indirectTex.Bind(12);
        albedoTex.Bind(13);
        materialTex.Bind(14);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var diffuseOut = outputGBuffer[0].ReadPixels();

        SetupDebugCompositeUniforms(programId,
            debugMode: (int)LumOnDebugMode.CompositeIndirectSpecular,
            indirectIntensity: 1.0f,
            indirectTint: (1f, 1f, 1f),
            enablePbrComposite: 1,
            enableAO: 0,
            diffuseAOStrength: 1.0f,
            specularAOStrength: 1.0f,
            invProjection: invProj);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var specOut = outputGBuffer[0].ReadPixels();

        var (dr, dg, db, _) = ReadPixelScreen(diffuseOut, 2, 2);
        var (sr, sg, sb, _) = ReadPixelScreen(specOut, 2, 2);

        float diffuseLuma = dr + dg + db;
        float specLuma = sr + sg + sb;

        Assert.True(specLuma > diffuseLuma,
            $"Expected specular to dominate for metallic=1. Diffuse={diffuseLuma:F3}, Spec={specLuma:F3}");

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Composite_AO_ReducesIndirectInCreases()
    {
        EnsureShaderTestAvailable();

        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f);
        var albedo = (r: 1.0f, g: 1.0f, b: 1.0f);

        var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
        var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);

        // AO is currently stubbed (no-op). Reflectivity must not be treated as AO.
        var materialAo1 = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f, emissive: 0.0f, reflectivity: 1.0f);
        var materialAo0 = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.0f, emissive: 0.0f, reflectivity: 0.0f);

        var normalData = CreateUniformNormalData(ScreenWidth, ScreenHeight, 0f, 0f, 1f);
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.25f);

        var invProj = new float[]
        {
            0,0,0,0,
            0,0,0,0,
            0,0,1,0,
            0,0,0,1
        };

        using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
        using var materialAo1Tex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialAo1);
        using var materialAo0Tex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialAo0);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);

        var dummyRgba = CreateUniformColorData(ScreenWidth, ScreenHeight, 0f, 0f, 0f, 0f);
        using var dummyTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, dummyRgba);

        var programId = CompileDebugShader(enablePbrComposite: 1, enableAO: 1);

        float RenderWithAoTexture(DynamicTexture2D materialTex)
        {
            SetupDebugCompositeUniforms(programId,
                debugMode: (int)LumOnDebugMode.CompositeIndirectDiffuse,
                indirectIntensity: 1.0f,
                indirectTint: (1f, 1f, 1f),
                enablePbrComposite: 1,
                enableAO: 1,
                diffuseAOStrength: 1.0f,
                specularAOStrength: 0.0f,
                invProjection: invProj);

            depthTex.Bind(0);
            normalTex.Bind(1);
            dummyTex.Bind(2);
            dummyTex.Bind(3);
            dummyTex.Bind(4);
            dummyTex.Bind(5);
            dummyTex.Bind(6);
            dummyTex.Bind(7);
            dummyTex.Bind(8);
            dummyTex.Bind(9);
            dummyTex.Bind(10);
            dummyTex.Bind(11);
            indirectTex.Bind(12);
            albedoTex.Bind(13);
            materialTex.Bind(14);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelScreen(outData, 2, 2);
            return r + g + b;
        }

        float lumaAo1 = RenderWithAoTexture(materialAo1Tex);
        float lumaAo0 = RenderWithAoTexture(materialAo0Tex);

        Assert.True(MathF.Abs(lumaAo1 - lumaAo0) < 1e-3f,
            $"AO is stubbed; reflectivity must not attenuate indirect. Reflectivity1={lumaAo1:F3}, Reflectivity0={lumaAo0:F3}");

        GL.DeleteProgram(programId);
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

        var programId = CompileCombineShader(enablePbrComposite: 0);
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

        var programId = CompileCombineShader(enablePbrComposite: 0);
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

        var programId = CompileCombineShader(enablePbrComposite: 0);
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

        var programId = CompileCombineShader(lumOnEnabled: 0, enablePbrComposite: 0);
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

        var programId = CompileCombineShader(enablePbrComposite: 0);
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

        var programId = CompileCombineShader(enablePbrComposite: 0);
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

    #region Phase 5 Tests: Medium Priority

    /// <summary>
    /// Tests that indirectTint colors the indirect lighting contribution.
    /// 
    /// DESIRED BEHAVIOR:
    /// - indirectTint multiplies the indirect contribution
    /// - Allows artistic control over indirect light color
    /// 
    /// Setup:
    /// - White indirect light
    /// - Red tint (1, 0, 0)
    /// 
    /// Expected:
    /// - Indirect contribution should be red-tinted
    /// </summary>
    [Fact]
    public void IndirectTint_ColorsIndirectLight()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.0f, g: 0.0f, b: 0.0f);   // No direct
        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f); // White indirect
        var albedo = (r: 1.0f, g: 1.0f, b: 1.0f);   // White albedo

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

        var programId = CompileCombineShader(enablePbrComposite: 0);
        SetupCombineUniforms(programId, indirectIntensity: 1.0f, indirectTint: (1f, 0f, 0f), lumOnEnabled: 1);  // Red tint

        sceneDirectTex.Bind(0);
        indirectTex.Bind(1);
        albedoTex.Bind(2);
        materialTex.Bind(3);
        depthTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Output should be red (white indirect * red tint)
        var (r, g, b, _) = ReadPixelScreen(outputData, 2, 2);
        Assert.True(r > 0.5f, $"Red tint should produce red output, got R={r:F3}");
        Assert.True(g < 0.1f, $"Red tint should suppress green, got G={g:F3}");
        Assert.True(b < 0.1f, $"Red tint should suppress blue, got B={b:F3}");

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that partial metallic values blend diffuse correctly.
    /// 
    /// DESIRED BEHAVIOR:
    /// - metallic=0.5 should give diffuseWeight=0.5
    /// - Indirect contribution should be half of full dielectric
    /// 
    /// Setup:
    /// - Metallic = 0.5
    /// - Bright indirect light
    /// 
    /// Expected:
    /// - Output between full dielectric and full metal
    /// </summary>
    [Fact]
    public void PartialMetallic_BlendsDiffuse()
    {
        EnsureShaderTestAvailable();

        var direct = (r: 0.2f, g: 0.2f, b: 0.2f);
        var indirect = (r: 1.0f, g: 1.0f, b: 1.0f);
        var albedo = (r: 1.0f, g: 1.0f, b: 1.0f);

        float dielectricBrightness;
        float halfMetallicBrightness;
        float fullMetallicBrightness;

        // Dielectric (metallic=0)
        {
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

            var programId = CompileCombineShader(enablePbrComposite: 0);
            SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 1);

            sceneDirectTex.Bind(0);
            indirectTex.Bind(1);
            albedoTex.Bind(2);
            materialTex.Bind(3);
            depthTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelScreen(outputData, 2, 2);
            dielectricBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Half metallic (metallic=0.5)
        {
            var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
            var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
            var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
            var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0.5f);
            var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);

            using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
            using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
            using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
            using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ScreenWidth, ScreenHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileCombineShader(enablePbrComposite: 0);
            SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 1);

            sceneDirectTex.Bind(0);
            indirectTex.Bind(1);
            albedoTex.Bind(2);
            materialTex.Bind(3);
            depthTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelScreen(outputData, 2, 2);
            halfMetallicBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Full metallic (metallic=1.0)
        {
            var sceneDirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, direct.r, direct.g, direct.b);
            var indirectData = CreateUniformColorData(ScreenWidth, ScreenHeight, indirect.r, indirect.g, indirect.b);
            var albedoData = CreateUniformColorData(ScreenWidth, ScreenHeight, albedo.r, albedo.g, albedo.b);
            var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 1.0f);
            var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, 0.5f);

            using var sceneDirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, sceneDirectData);
            using var indirectTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, indirectData);
            using var albedoTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, albedoData);
            using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, materialData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ScreenWidth, ScreenHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileCombineShader(enablePbrComposite: 0);
            SetupCombineUniforms(programId, indirectIntensity: 1.0f, lumOnEnabled: 1);

            sceneDirectTex.Bind(0);
            indirectTex.Bind(1);
            albedoTex.Bind(2);
            materialTex.Bind(3);
            depthTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelScreen(outputData, 2, 2);
            fullMetallicBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Half metallic should be between dielectric and full metallic
        Assert.True(halfMetallicBrightness < dielectricBrightness,
            $"Half metallic ({halfMetallicBrightness:F3}) should be darker than dielectric ({dielectricBrightness:F3})");
        Assert.True(halfMetallicBrightness > fullMetallicBrightness,
            $"Half metallic ({halfMetallicBrightness:F3}) should be brighter than full metallic ({fullMetallicBrightness:F3})");
    }

    #endregion
}
