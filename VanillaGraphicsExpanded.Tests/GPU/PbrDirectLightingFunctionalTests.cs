using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

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

        int programId = CompilePbrDirectLightingProgram();
        try
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
        finally
        {
            GL.DeleteProgram(programId);
        }
    }

    [Fact]
    public void DirectLighting_ProducesExpectedLambertForDielectric()
    {
        EnsureShaderTestAvailable();

        int programId = CompilePbrDirectLightingProgram();
        try
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
        finally
        {
            GL.DeleteProgram(programId);
        }
    }

    [Fact]
    public void DirectLighting_MetallicShiftsEnergyToSpecular()
    {
        EnsureShaderTestAvailable();

        int programId = CompilePbrDirectLightingProgram();
        try
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
        finally
        {
            GL.DeleteProgram(programId);
        }
    }

    [Fact]
    public void DirectLighting_EmissiveIsSeparateBuffer()
    {
        EnsureShaderTestAvailable();

        int programId = CompilePbrDirectLightingProgram();
        try
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
        finally
        {
            GL.DeleteProgram(programId);
        }
    }

    [Fact]
    public void DirectLighting_StableUnderCameraMotion()
    {
        EnsureShaderTestAvailable();

        int programId = CompilePbrDirectLightingProgram();
        try
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
        finally
        {
            GL.DeleteProgram(programId);
        }
    }

    [Fact]
    public void Integration_LumOnDisabled_PassesThroughDirect()
    {
        EnsureShaderTestAvailable();

        int programId = CompilePbrCompositeProgram();
        try
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
        finally
        {
            GL.DeleteProgram(programId);
        }
    }

    private int CompilePbrDirectLightingProgram()
    {
        string? processedFragment = ShaderHelper.GetProcessedSource("pbr_direct_lighting.fsh");
        Assert.NotNull(processedFragment);

        processedFragment = SourceCodeImportsProcessor.StripNonAscii(processedFragment!);

        const string vertexSource = "#version 330 core\n" +
                                    "layout(location = 0) in vec2 position;\n" +
                                    "out vec2 uv;\n" +
                                    "void main(){ gl_Position = vec4(position, 0.0, 1.0); uv = position * 0.5 + 0.5; }\n";

        return CompileProgramFromSource(vertexSource, processedFragment!);
    }

    private int CompilePbrCompositeProgram()
    {
        string? processedFragment = ShaderHelper.GetProcessedSource("pbr_composite.fsh");
        Assert.NotNull(processedFragment);

        processedFragment = SourceCodeImportsProcessor.StripNonAscii(processedFragment!);

        const string vertexSource = "#version 330 core\n" +
                                    "layout(location = 0) in vec2 position;\n" +
                                    "void main(){ gl_Position = vec4(position, 0.0, 1.0); }\n";

        return CompileProgramFromSource(vertexSource, processedFragment!);
    }

    private static int CompileProgramFromSource(string vertexSource, string fragmentSource)
    {
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);
        GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vStatus);
        if (vStatus == 0)
        {
            var log = GL.GetShaderInfoLog(vertexShader);
            GL.DeleteShader(vertexShader);
            throw new InvalidOperationException($"Vertex shader compile error: {log}");
        }

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);
        GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fStatus);
        if (fStatus == 0)
        {
            var log = GL.GetShaderInfoLog(fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            throw new InvalidOperationException($"Fragment shader compile error: {log}");
        }

        int program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int lStatus);
        if (lStatus == 0)
        {
            var log = GL.GetProgramInfoLog(program);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteProgram(program);
            throw new InvalidOperationException($"Program link error: {log}");
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return program;
    }

    private static (float R, float G, float B, float A) ReadPixelFromAttachment(GBuffer target, int attachmentIndex)
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

    private void RenderDirectLighting(
        int programId,
        GBuffer output,
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

        GL.UseProgram(programId);

        // Samplers
        BindSampler(programId, "primaryScene", 0, primaryScene.TextureId);
        BindSampler(programId, "primaryDepth", 1, primaryDepth.TextureId);
        BindSampler(programId, "gBufferNormal", 2, gBufferNormal.TextureId);
        BindSampler(programId, "gBufferMaterial", 3, gBufferMaterial.TextureId);
        BindSampler(programId, "shadowMapNear", 4, shadowNear.TextureId);
        BindSampler(programId, "shadowMapFar", 5, shadowFar.TextureId);

        // Identity matrices
        float[] identity =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        ];

        SetMat4(programId, "invProjectionMatrix", identity);
        SetMat4(programId, "invModelViewMatrix", identity);

        // Z-planes (not used directly in the pass right now)
        SetFloat(programId, "zNear", 0.1f);
        SetFloat(programId, "zFar", 100f);

        // Camera origin split
        SetVec3(programId, "cameraOriginFloor", cameraOriginFloor.x, cameraOriginFloor.y, cameraOriginFloor.z);
        SetVec3(programId, "cameraOriginFrac", cameraOriginFrac.x, cameraOriginFrac.y, cameraOriginFrac.z);

        // Lighting
        SetVec3(programId, "lightDirection", lightDirection.x, lightDirection.y, lightDirection.z);
        SetVec3(programId, "rgbaLightIn", rgbaLightIn.r, rgbaLightIn.g, rgbaLightIn.b);
        SetVec3(programId, "rgbaAmbientIn", 0f, 0f, 0f);

        // Point lights
        SetInt(programId, "pointLightsCount", pointLightCount);
        SetVec3(programId, "pointLights3[0]", pointLightPos0.x, pointLightPos0.y, pointLightPos0.z);
        SetVec3(programId, "pointLightColors3[0]", pointLightColor0.r, pointLightColor0.g, pointLightColor0.b);

        // Shadow uniforms - set defaults to keep drivers happy
        SetMat4(programId, "toShadowMapSpaceMatrixNear", identity);
        SetMat4(programId, "toShadowMapSpaceMatrixFar", identity);
        SetFloat(programId, "shadowRangeNear", 1f);
        SetFloat(programId, "shadowRangeFar", 1f);
        SetFloat(programId, "shadowZExtendNear", 1f);
        SetFloat(programId, "shadowZExtendFar", 1f);
        SetFloat(programId, "dropShadowIntensity", 0f);

        // Draw
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        // Fullscreen triangle from ShaderTestFramework expects position at location 0.
        TestFramework.RenderQuad(programId);

        GL.UseProgram(0);
        GBuffer.Unbind();
    }

    private void RenderComposite(
        int programId,
        GBuffer output,
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

        GL.UseProgram(programId);

        BindSampler(programId, "directDiffuse", 0, directDiffuse.TextureId);
        BindSampler(programId, "directSpecular", 1, directSpecular.TextureId);
        BindSampler(programId, "emissive", 2, emissive.TextureId);
        BindSampler(programId, "indirectDiffuse", 3, indirectDiffuse.TextureId);
        BindSampler(programId, "gBufferAlbedo", 4, gBufferAlbedo.TextureId);
        BindSampler(programId, "gBufferMaterial", 5, gBufferMaterial.TextureId);
        BindSampler(programId, "gBufferNormal", 6, gBufferNormal.TextureId);
        BindSampler(programId, "primaryDepth", 7, primaryDepth.TextureId);

        // Disable LumOn + fog
        SetInt(programId, "lumOnEnabled", 0);
        SetFloat(programId, "indirectIntensity", 0f);
        SetVec3(programId, "indirectTint", 1f, 1f, 1f);

        SetVec4(programId, "rgbaFogIn", 0f, 0f, 0f, 0f);
        SetFloat(programId, "fogDensityIn", 0f);
        SetFloat(programId, "fogMinIn", 0f);

        // Phase 15 toggles irrelevant when lumOn disabled
        SetInt(programId, "enablePbrComposite", 0);
        SetInt(programId, "enableAO", 0);
        SetInt(programId, "enableShortRangeAo", 0);
        SetFloat(programId, "diffuseAOStrength", 1f);
        SetFloat(programId, "specularAOStrength", 1f);

        float[] identity =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        ];
        SetMat4(programId, "invProjectionMatrix", identity);
        SetMat4(programId, "viewMatrix", identity);

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        TestFramework.RenderQuad(programId);

        GL.UseProgram(0);
        GBuffer.Unbind();
    }

    private static void BindSampler(int programId, string name, int unit, int textureId)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc < 0)
        {
            return;
        }

        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.Uniform1(loc, unit);
    }

    private static void SetFloat(int programId, string name, float value)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private static void SetInt(int programId, string name, int value)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0) GL.Uniform1(loc, value);
    }

    private static void SetVec3(int programId, string name, float x, float y, float z)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0) GL.Uniform3(loc, x, y, z);
    }

    private static void SetVec4(int programId, string name, float x, float y, float z, float w)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0) GL.Uniform4(loc, x, y, z, w);
    }

    private static void SetMat4(int programId, string name, float[] m)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0) GL.UniformMatrix4(loc, 1, false, m);
    }
}
