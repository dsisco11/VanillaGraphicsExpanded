using System;
using System.Numerics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrDirectLightingFunctionalTests : LumOnShaderFunctionalTestBase
{
    public PbrDirectLightingFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    public void DirectLighting_RoughnessExtremes_AreFinite(float roughness)
    {
        EnsureShaderTestAvailable();

        var programId = CompilePbrDirectLightingProgram();
        {
            using var output = TestFramework.CreateTestGBuffer(1, 1,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            using var primaryScene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 0.5f, 1f });
            using var primaryDepth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 0.0f });
            using var gBufferNormal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 1.0f, 1.0f });
            using var gBufferMaterial = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { roughness, 0.0f, 0.0f, 1.0f });
            using var dummyShadow = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 1.0f });

            RenderDirectLighting(programId, output,
                primaryScene, primaryDepth, gBufferNormal, gBufferMaterial,
                dummyShadow, dummyShadow,
                lightDirection: (0f, 0f, 1f),
                rgbaLightIn: (1f, 1f, 1f),
                pointLightCount: 0,
                pointLightPos0: (0f, 0f, 0f),
                pointLightColor0: (0f, 0f, 0f),
                cameraOriginFloor: (0f, 0f, 0f),
                cameraOriginFrac: (0f, 0f, 0f));

            var dd = ReadPixelFromAttachment(output, 0);
            var ds = ReadPixelFromAttachment(output, 1);

            AssertAllFinite(dd);
            AssertAllFinite(ds);
        }
    }

    [Fact]
    public void DirectLighting_ProducesExpectedLambertForDielectric()
    {
        EnsureShaderTestAvailable();

        var programId = CompilePbrDirectLightingProgram();
        {
            using var output = TestFramework.CreateTestGBuffer(1, 1,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            // Inputs
            var baseColor = (r: 0.8f, g: 0.2f, b: 0.1f);
            using var primaryScene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { baseColor.r, baseColor.g, baseColor.b, 1f });
            using var primaryDepth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 0.0f });

            // Normal = +Z (packed to 0..1)
            using var gBufferNormal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 1.0f, 1.0f });

            // Roughness, metallic, emissiveScalar, reflectivity
            using var gBufferMaterial = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.0f, 0.0f, 1.0f });

            using var dummyShadow = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 1.0f });

            RenderDirectLighting(programId, output,
                primaryScene, primaryDepth, gBufferNormal, gBufferMaterial,
                dummyShadow, dummyShadow,
                lightDirection: (0f, 0f, 1f),
                rgbaLightIn: (1f, 1f, 1f),
                pointLightCount: 0,
                pointLightPos0: (0f, 0f, 0f),
                pointLightColor0: (0f, 0f, 0f),
                cameraOriginFloor: (0f, 0f, 0f),
                cameraOriginFrac: (0f, 0f, 0f));

            var dd = ReadPixelFromAttachment(output, 0);

            // With N=V=L and dielectric metallic=0:
            // FresnelSchlick(1, F0) = F0, kD = (1-F0), diffuseBrdf = kD*baseColor.
            // NOTE: The shader intentionally does not apply 1/PI because the engine's light inputs
            // are not calibrated as physical radiance.
            float F0 = 0.04f;
            float kd = 1.0f - F0;
            float expectedR = kd * baseColor.r;
            float expectedG = kd * baseColor.g;
            float expectedB = kd * baseColor.b;

            AssertNear(expectedR, dd.R, 3e-2f);
            AssertNear(expectedG, dd.G, 3e-2f);
            AssertNear(expectedB, dd.B, 3e-2f);
        }
    }

    [Fact]
    public void DirectLighting_MetallicShiftsEnergyToSpecular()
    {
        EnsureShaderTestAvailable();

        var programId = CompilePbrDirectLightingProgram();
        {
            using var output = TestFramework.CreateTestGBuffer(1, 1,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            using var primaryScene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.7f, 0.7f, 0.7f, 1f });
            using var primaryDepth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 0.0f });
            using var gBufferNormal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 1.0f, 1.0f });
            using var dummyShadow = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 1.0f });

            // Metallic = 1, emissive = 0
            using var gBufferMaterialMetal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.2f, 1.0f, 0.0f, 1.0f });

            RenderDirectLighting(programId, output,
                primaryScene, primaryDepth, gBufferNormal, gBufferMaterialMetal,
                dummyShadow, dummyShadow,
                lightDirection: (0f, 0f, 1f),
                rgbaLightIn: (1f, 1f, 1f),
                pointLightCount: 0,
                pointLightPos0: (0f, 0f, 0f),
                pointLightColor0: (0f, 0f, 0f),
                cameraOriginFloor: (0f, 0f, 0f),
                cameraOriginFrac: (0f, 0f, 0f));

            var dd = ReadPixelFromAttachment(output, 0);
            var ds = ReadPixelFromAttachment(output, 1);

            // For metallic=1, diffuse contribution should be ~0 (kD -> 0).
            Assert.True(dd.R < 2e-2f && dd.G < 2e-2f && dd.B < 2e-2f,
                $"Expected near-zero diffuse for metallic=1, got ({dd.R}, {dd.G}, {dd.B})");

            Assert.True(ds.R > 1e-3f || ds.G > 1e-3f || ds.B > 1e-3f,
                $"Expected some specular for metallic=1, got ({ds.R}, {ds.G}, {ds.B})");
        }
    }

    [Fact]
    public void DirectLighting_EmissiveIsSeparateBuffer()
    {
        EnsureShaderTestAvailable();

        var programId = CompilePbrDirectLightingProgram();
        {
            using var output = TestFramework.CreateTestGBuffer(1, 1,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            var baseColor = (r: 0.3f, g: 0.4f, b: 0.5f);

            using var primaryScene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { baseColor.r, baseColor.g, baseColor.b, 1f });
            using var primaryDepth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 0.0f });
            using var gBufferNormal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 1.0f, 1.0f });

            float emissiveScalar = 0.9f;
            using var gBufferMaterial = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.0f, emissiveScalar, 1.0f });
            using var dummyShadow = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 1.0f });

            // No lights at all
            RenderDirectLighting(programId, output,
                primaryScene, primaryDepth, gBufferNormal, gBufferMaterial,
                dummyShadow, dummyShadow,
                lightDirection: (0f, 0f, 1f),
                rgbaLightIn: (0f, 0f, 0f),
                pointLightCount: 0,
                pointLightPos0: (0f, 0f, 0f),
                pointLightColor0: (0f, 0f, 0f),
                cameraOriginFloor: (0f, 0f, 0f),
                cameraOriginFrac: (0f, 0f, 0f));

            var dd = ReadPixelFromAttachment(output, 0);
            var ds = ReadPixelFromAttachment(output, 1);
            var em = ReadPixelFromAttachment(output, 2);

            Assert.True(dd.R < 1e-3f && dd.G < 1e-3f && dd.B < 1e-3f);
            Assert.True(ds.R < 1e-3f && ds.G < 1e-3f && ds.B < 1e-3f);

            AssertNear(baseColor.r * emissiveScalar, em.R, 2e-2f);
            AssertNear(baseColor.g * emissiveScalar, em.G, 2e-2f);
            AssertNear(baseColor.b * emissiveScalar, em.B, 2e-2f);
        }
    }

    [Fact]
    public void DirectLighting_StableUnderCameraMotion()
    {
        EnsureShaderTestAvailable();

        var programId = CompilePbrDirectLightingProgram();
        {
            using var outputA = TestFramework.CreateTestGBuffer(1, 1,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);
            using var outputB = TestFramework.CreateTestGBuffer(1, 1,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            using var primaryScene = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.6f, 0.6f, 0.6f, 1f });
            using var primaryDepth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 0.0f });
            using var gBufferNormal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 1.0f, 1.0f });
            using var gBufferMaterial = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.4f, 0.0f, 0.0f, 1.0f });
            using var dummyShadow = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 1.0f });

            // Only a point light; directional disabled.
            // For depth=0 and uv=center, viewPos=(0,0,-1) with identity invProjection.
            // worldPos = cameraOrigin + (0,0,-1). If we shift cameraOrigin and the point light by the same delta,
            // the relative vector to the light is preserved and output should match.

            var delta = (x: 1000f, y: 0f, z: 0f);

            RenderDirectLighting(programId, outputA,
                primaryScene, primaryDepth, gBufferNormal, gBufferMaterial,
                dummyShadow, dummyShadow,
                lightDirection: (0f, 0f, 1f),
                rgbaLightIn: (0f, 0f, 0f),
                pointLightCount: 1,
                pointLightPos0: (0f, 0f, 0f),
                pointLightColor0: (1f, 1f, 1f),
                cameraOriginFloor: (0f, 0f, 0f),
                cameraOriginFrac: (0f, 0f, 0f));

            RenderDirectLighting(programId, outputB,
                primaryScene, primaryDepth, gBufferNormal, gBufferMaterial,
                dummyShadow, dummyShadow,
                lightDirection: (0f, 0f, 1f),
                rgbaLightIn: (0f, 0f, 0f),
                pointLightCount: 1,
                pointLightPos0: (delta.x, delta.y, delta.z),
                pointLightColor0: (1f, 1f, 1f),
                cameraOriginFloor: (delta.x, delta.y, delta.z),
                cameraOriginFrac: (0f, 0f, 0f));

            var ddA = ReadPixelFromAttachment(outputA, 0);
            var dsA = ReadPixelFromAttachment(outputA, 1);

            var ddB = ReadPixelFromAttachment(outputB, 0);
            var dsB = ReadPixelFromAttachment(outputB, 1);

            AssertNear(ddA.R, ddB.R, 2e-2f);
            AssertNear(ddA.G, ddB.G, 2e-2f);
            AssertNear(ddA.B, ddB.B, 2e-2f);

            AssertNear(dsA.R, dsB.R, 2e-2f);
            AssertNear(dsA.G, dsB.G, 2e-2f);
            AssertNear(dsA.B, dsB.B, 2e-2f);
        }
    }

    [Fact]
    public void Integration_LumOnDisabled_PassesThroughDirect()
    {
        EnsureShaderTestAvailable();

        var programId = CompilePbrCompositeProgram();
        {
            using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f, 1);

            using var directDiffuse = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.1f, 0.2f, 0.3f, 1f });
            using var directSpecular = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.4f, 0.0f, 0.1f, 1f });
            using var emissive = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.05f, 0.05f, 0.0f, 1f });
            using var primaryDepth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { 0.0f });

            // Unused when lumOnEnabled=0, but provided for completeness.
            using var indirect = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0f, 0f, 0f, 1f });
            using var albedo = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0f, 0f, 0f, 1f });
            using var material = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0f, 0f, 0f, 0f });
            using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0.5f, 0.5f, 1f, 1f });

            RenderComposite(programId, output,
                directDiffuse, directSpecular, emissive,
                indirect,
                albedo, material, normal,
                primaryDepth);

            var outPx = ReadPixelFromAttachment(output, 0);

            // No fog (density=0) and lumOn disabled => direct+emissive passthrough.
            AssertNear(0.1f + 0.4f + 0.05f, outPx.R, 2e-2f);
            AssertNear(0.2f + 0.0f + 0.05f, outPx.G, 2e-2f);
            AssertNear(0.3f + 0.1f + 0.0f, outPx.B, 2e-2f);
        }
}

    /// <summary>Uses the reusable binary direct-lighting fixture.</summary>
    private PBRDirectLightingShaderProgram CompilePbrDirectLightingProgram() => Programs.Create<PBRDirectLightingShaderProgram>();

    /// <summary>Uses the reusable binary composite fixture.</summary>
    private PBRCompositeShaderProgram CompilePbrCompositeProgram() => Programs.Create<PBRCompositeShaderProgram>(shader => { shader.LumOnEnabled = false; shader.EnablePbrComposite = false; shader.EnableShortRangeAo = false; });
    private static (float R, float G, float B, float A) ReadPixelFromAttachment(GpuFramebuffer target, int attachmentIndex)
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, target.FboId);
        GL.ReadBuffer((ReadBufferMode)((int)ReadBufferMode.ColorAttachment0 + attachmentIndex));

        float[] pixel = new float[4];
        GL.ReadPixels(0, 0, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, pixel);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static void AssertNear(float expected, float actual, float epsilon)
    {
        Assert.True(MathF.Abs(expected - actual) <= epsilon, $"Expected {expected} +/- {epsilon}, got {actual}");
    }

    private static void AssertAllFinite((float R, float G, float B, float A) px)
    {
        Assert.True(float.IsFinite(px.R) && float.IsFinite(px.G) && float.IsFinite(px.B) && float.IsFinite(px.A),
            $"Expected finite RGBA, got ({px.R}, {px.G}, {px.B}, {px.A})");
    }

    /// <summary>Renders controlled lighting through the production parameters and resource setters.</summary>
    private void RenderDirectLighting(
        PBRDirectLightingShaderProgram programId,
        GpuFramebuffer output,
        DynamicTexture2D primaryScene,
        DynamicTexture2D primaryDepth,
        DynamicTexture2D gBufferNormal,
        DynamicTexture2D gBufferMaterial,
        DynamicTexture2D shadowNear,
        DynamicTexture2D shadowFar,
        (float x, float y, float z) lightDirection,
        (float r, float g, float b) rgbaLightIn,
        int pointLightCount,
        (float x, float y, float z) pointLightPos0,
        (float r, float g, float b) pointLightColor0,
        (float x, float y, float z) cameraOriginFloor,
        (float x, float y, float z) cameraOriginFrac)
    {
        output.BindWithViewport();
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        using var use = programId.UseScope();

        // Samplers
        programId.PrimaryScene = primaryScene.TextureId;
        programId.PrimaryDepth = primaryDepth.TextureId;
        programId.GBufferNormal = gBufferNormal.TextureId;
        programId.GBufferMaterial = gBufferMaterial.TextureId;
        programId.ShadowMapNear = shadowNear.TextureId;
        programId.ShadowMapFar = shadowFar.TextureId;

        // Identity matrices
        float[] identity =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        ];

        // Phase 23: UBO-backed params (VgePbrDirectLightingParamsUBO @ object binding).

        float[]? pointLightPositions3 = null;
        float[]? pointLightColors3 = null;
        if (pointLightCount > 0)
        {
            pointLightPositions3 = [pointLightPos0.x, pointLightPos0.y, pointLightPos0.z];
            pointLightColors3 = [pointLightColor0.r, pointLightColor0.g, pointLightColor0.b];
        }
        programId.InvProjectionMatrix = identity;
        programId.InvModelViewMatrix = identity;
        programId.ToShadowMapSpaceMatrixNear = identity;
        programId.ToShadowMapSpaceMatrixFar = identity;
        programId.ZPlanesAndShadowRanges = (zNear: 0.1f, zFar: 100f, shadowRangeNear: 1f, shadowRangeFar: 1f);
        programId.ShadowZExtendNear = 1; programId.ShadowZExtendFar = 1; programId.DropShadowIntensity = 0;
        programId.CameraOriginFloor = new(cameraOriginFloor.x, cameraOriginFloor.y, cameraOriginFloor.z);
        programId.CameraOriginFrac = new(cameraOriginFrac.x, cameraOriginFrac.y, cameraOriginFrac.z);
        programId.LightDirection = new(lightDirection.x, lightDirection.y, lightDirection.z);
        programId.RgbaLightIn = new(rgbaLightIn.r, rgbaLightIn.g, rgbaLightIn.b);
        programId.RgbaAmbientIn = new(0,0,0);
        programId.SetPointLights(pointLightCount, pointLightPositions3, pointLightColors3);

        // Draw
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        // Fullscreen triangle from ShaderTestFramework expects position at location 0.
        TestFramework.RenderQuad(programId);

        GpuFramebuffer.Unbind();
    }

    /// <summary>Renders direct-only composition with the production composite shader.</summary>
    private void RenderComposite(
        PBRCompositeShaderProgram programId,
        GpuFramebuffer output,
        DynamicTexture2D directDiffuse,
        DynamicTexture2D directSpecular,
        DynamicTexture2D emissive,
        DynamicTexture2D indirectDiffuse,
        DynamicTexture2D gBufferAlbedo,
        DynamicTexture2D gBufferMaterial,
        DynamicTexture2D gBufferNormal,
        DynamicTexture2D primaryDepth)
    {
        output.BindWithViewport();
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        using var use = programId.UseScope();

        programId.DirectDiffuse = directDiffuse;
        programId.DirectSpecular = directSpecular;
        programId.Emissive = emissive;
        programId.IndirectDiffuse = indirectDiffuse;
        programId.GBufferAlbedo = gBufferAlbedo.TextureId;
        programId.GBufferMaterial = gBufferMaterial.TextureId;
        programId.GBufferNormal = gBufferNormal.TextureId;
        programId.PrimaryDepth = primaryDepth.TextureId;

        // Disable LumOn + fog
        programId.IndirectIntensity = 0f;
        programId.IndirectTint = new(1f, 1f, 1f);

        programId.RgbaFogIn = new(0f, 0f, 0f, 0f);
        programId.FogDensityIn = 0f;
        programId.FogMinIn = 0f;

        // Ambient-occlusion controls are inactive when indirect lighting is disabled.
        programId.DiffuseAOStrength = 1f;
        programId.SpecularAOStrength = 1f;

        float[] identity =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        ];
        programId.InvProjectionMatrix = identity;
        programId.ViewMatrix = identity;

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        TestFramework.RenderQuad(programId);

        GpuFramebuffer.Unbind();
    }

}
