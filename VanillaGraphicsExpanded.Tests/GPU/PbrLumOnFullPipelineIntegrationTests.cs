using System;
using System.Linq;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrLumOnFullPipelineIntegrationTests : LumOnShaderFunctionalTestBase
{
    public PbrLumOnFullPipelineIntegrationTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite()
    {
        EnsureShaderTestAvailable();

        // Binding audit: these tests are intended to verify correct program sampler wiring.
        // If a uniform gets optimized out, that's usually a sign the shader changed and the
        // test should be revisited rather than silently skipping.

        // Fixed matrices / params.
        float[] invProj = LumOnTestInputFactory.CreateRealisticInverseProjection();
        float[] proj = LumOnTestInputFactory.CreateRealisticProjection();
        float[] identity = LumOnTestInputFactory.CreateIdentityMatrix();

        // Inputs (deterministic).
        // Sentinel-ish: make primary scene color asymmetric so accidental swaps are easier to spot.
        var primarySceneData = PbrLumOnPipelineInputFactory.CreatePrimarySceneColorUniform(0.11f, 0.23f, 0.37f);
        var primaryDepthData = PbrLumOnPipelineInputFactory.CreatePrimaryDepthUniform(depthRaw: 0.5f);
        var gBufferAlbedoData = PbrLumOnPipelineInputFactory.CreateGBufferAlbedoUniform(0.61f, 0.42f, 0.19f);
        var gBufferNormalData = PbrLumOnPipelineInputFactory.CreateGBufferNormalEncodedUniform(0f, 0f, 1f);
        var gBufferMaterialData = PbrLumOnPipelineInputFactory.CreateGBufferMaterialUniform(
            roughness: 0.63f,
            metallic: 0.0f,
            emissiveScalar: 1.25f,
            reflectivity: 0.7f);

        var shadowNearData = PbrLumOnPipelineInputFactory.CreateShadowMapUniform(depthRaw: 1.0f);
        var shadowFarData = PbrLumOnPipelineInputFactory.CreateShadowMapUniform(depthRaw: 1.0f);

        // History inputs (fixed). We trace all texels so history should not materially matter,
        // but these must still be bound for shader completeness.
        var historyRadianceData = PbrLumOnPipelineInputFactory.CreateProbeAtlasHistoryRadianceUniform(
            r: 0.0f,
            g: 0.0f,
            b: 0.0f,
            encodedHitDistance: 0.0f);
        var historyMetaData = PbrLumOnPipelineInputFactory.CreateProbeAtlasHistoryMetaUniform(confidence: 0.0f, flags: 0);

        var zeroIndirectFullData = CreateUniformColorData(ScreenWidth, ScreenHeight, 0f, 0f, 0f, 1f);
        var injectedIndirectFullData = CreateUniformColorData(ScreenWidth, ScreenHeight, 0.2f, 0.2f, 0.2f, 1f);

        using var primaryScene = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, primarySceneData);
        using var primaryDepth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, primaryDepthData);
        using var gBufferAlbedo = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gBufferAlbedoData);
        using var gBufferNormal = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gBufferNormalData);
        using var gBufferMaterial = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, gBufferMaterialData);

        using var shadowNear = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, shadowNearData);
        using var shadowFar = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, shadowFarData);

        using var historyRadiance = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyRadianceData);
        using var historyMeta = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, historyMetaData);

        using var zeroIndirectFull = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, zeroIndirectFullData);
        using var injectedIndirectFull = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, injectedIndirectFullData);

        using var targets = new PbrLumOnPipelineTargets();
        using var baselineComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
        using var injectedComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);

        int pbrDirectProg = 0;
        int velocityProg = 0;
        int hzbCopyProg = 0;
        int hzbDownProg = 0;
        int anchorProg = 0;
        int traceProg = 0;
        int temporalProg = 0;
        int filterProg = 0;
        int gatherProg = 0;
        int upsampleProg = 0;
        int pbrCompositeProg = 0;

        try
        {
            pbrDirectProg = PbrShaderPrograms.CompilePbrDirectLightingProgram(ShaderHelper);
            velocityProg = CompileShader("lumon_velocity.vsh", "lumon_velocity.fsh");
            hzbCopyProg = CompileShader("lumon_hzb_copy.vsh", "lumon_hzb_copy.fsh");
            hzbDownProg = CompileShader("lumon_hzb_downsample.vsh", "lumon_hzb_downsample.fsh");
            anchorProg = CompileShader("lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh");
                traceProg = CompileShaderWithDefines(
                    "lumon_probe_atlas_trace.vsh",
                    "lumon_probe_atlas_trace.fsh",
                    new Dictionary<string, string?>
                    {
                        ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                        ["VGE_LUMON_RAY_STEPS"] = "8",
                        ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                        ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                        ["VGE_LUMON_HZB_COARSE_MIP"] = "0",
                        ["VGE_LUMON_SKY_MISS_WEIGHT"] = "1.0"
                    });
                temporalProg = CompileShaderWithDefines(
                    "lumon_probe_atlas_temporal.vsh",
                    "lumon_probe_atlas_temporal.fsh",
                    new Dictionary<string, string?>
                    {
                        ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64"
                    });
            filterProg = CompileShader("lumon_probe_atlas_filter.vsh", "lumon_probe_atlas_filter.fsh");
            gatherProg = CompileShader("lumon_probe_atlas_gather.vsh", "lumon_probe_atlas_gather.fsh");
            upsampleProg = CompileShader("lumon_upsample.vsh", "lumon_upsample.fsh");
            pbrCompositeProg = PbrShaderPrograms.CompilePbrCompositeProgram(ShaderHelper);

            // -----------------------------------------------------------------
            // Stage: PBR Direct Lighting (MRT)
            // -----------------------------------------------------------------
            GL.UseProgram(pbrDirectProg);
            SetSampler(pbrDirectProg, "primaryScene", 0);
            SetSampler(pbrDirectProg, "primaryDepth", 1);
            SetSampler(pbrDirectProg, "gBufferNormal", 2);
            SetSampler(pbrDirectProg, "gBufferMaterial", 3);
            SetSampler(pbrDirectProg, "shadowMapNear", 4);
            SetSampler(pbrDirectProg, "shadowMapFar", 5);

            SetMat4(pbrDirectProg, "invProjectionMatrix", invProj);
            SetMat4(pbrDirectProg, "invModelViewMatrix", identity);

            SetFloat(pbrDirectProg, "zNear", ZNear);
            SetFloat(pbrDirectProg, "zFar", ZFar);

            SetVec3(pbrDirectProg, "cameraOriginFloor", 0f, 0f, 0f);
            SetVec3(pbrDirectProg, "cameraOriginFrac", 0f, 0f, 0f);

            // Directional light aligned with the test normal.
            SetVec3(pbrDirectProg, "lightDirection", 0f, 0f, 1f);
            SetVec3(pbrDirectProg, "rgbaLightIn", 0.35f, 0.55f, 0.75f);
            SetVec3(pbrDirectProg, "rgbaAmbientIn", 0.0f, 0.0f, 0.0f);

            SetInt(pbrDirectProg, "pointLightsCount", 0);

            // Shadow uniforms are present but currently not used by the fragment shader.
            // These calls safely no-op if uniforms are optimized out.
            SetFloat(pbrDirectProg, "shadowRangeNear", 0f);
            SetFloat(pbrDirectProg, "shadowRangeFar", 0f);
            SetFloat(pbrDirectProg, "shadowZExtendNear", 0f);
            SetFloat(pbrDirectProg, "shadowZExtendFar", 0f);
            SetFloat(pbrDirectProg, "dropShadowIntensity", 0f);

            primaryScene.Bind(0);
            primaryDepth.Bind(1);
            gBufferNormal.Bind(2);
            gBufferMaterial.Bind(3);
            shadowNear.Bind(4);
            shadowFar.Bind(5);

            // Binding audit (used samplers only)
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "primaryScene", 0, primaryScene);
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "primaryDepth", 1, primaryDepth);
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "gBufferNormal", 2, gBufferNormal);
            AssertSampler2DBinding("Stage: PBR Direct", pbrDirectProg, "gBufferMaterial", 3, gBufferMaterial);

            AssertGBufferFboAttachments("Stage: PBR Direct", targets.DirectLightingMrt);
            TestFramework.RenderQuadTo(pbrDirectProg, targets.DirectLightingMrt);
            AssertNoGLError("Stage: PBR Direct");

            var directDiffusePixels = targets.DirectLightingMrt[0].ReadPixels();
            var directSpecularPixels = targets.DirectLightingMrt[1].ReadPixels();
            var emissivePixels = targets.DirectLightingMrt[2].ReadPixels();

            AssertStageHasRgbEnergy(directDiffusePixels, 1e-4f, "Stage: PBR Direct → directDiffuse");
            AssertAllFinite(directSpecularPixels, "Stage: PBR Direct (directSpecular)");
            AssertStageHasRgbEnergy(emissivePixels, 1e-4f, "Stage: PBR Direct → emissive");
            AssertAllFinite(directDiffusePixels, "Stage: PBR Direct (directDiffuse)");
            AssertAllFinite(emissivePixels, "Stage: PBR Direct (emissive)");

            // -----------------------------------------------------------------
            // Stage: LumOn Velocity
            // -----------------------------------------------------------------
            GL.UseProgram(velocityProg);
            SetSampler(velocityProg, "primaryDepth", 0);
            SetVec2(velocityProg, "screenSize", ScreenWidth, ScreenHeight);
            SetMat4(velocityProg, "invCurrViewProjMatrix", invProj);
            SetMat4(velocityProg, "prevViewProjMatrix", proj);
            SetInt(velocityProg, "historyValid", 1);

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                velocityProg,
                invCurrViewProjMatrix: invProj,
                prevViewProjMatrix: proj,
                historyValid: 1);
            GL.UseProgram(0);

            primaryDepth.Bind(0);

            AssertSampler2DBinding("Stage: Velocity", velocityProg, "primaryDepth", 0, primaryDepth);
            AssertGBufferFboAttachments("Stage: Velocity", targets.Velocity);
            TestFramework.RenderQuadTo(velocityProg, targets.Velocity);
            AssertNoGLError("Stage: Velocity");

            var velocityPixels = targets.Velocity[0].ReadPixels();
            AssertAllFinite(velocityPixels, "Stage: Velocity");

            // Expect near-zero velocity (RG) when prev == curr.
            for (int i = 0; i < velocityPixels.Length; i += 4)
            {
                Assert.InRange(velocityPixels[i + 0], -TestEpsilon, TestEpsilon);
                Assert.InRange(velocityPixels[i + 1], -TestEpsilon, TestEpsilon);
            }

            // -----------------------------------------------------------------
            // Stage: LumOn HZB build
            // -----------------------------------------------------------------
            // Copy mip0
            targets.Hzb.BindMipForWrite(0);
            GL.UseProgram(hzbCopyProg);
            primaryDepth.Bind(0);
            SetSampler(hzbCopyProg, "primaryDepth", 0);

            AssertSampler2DBinding("Stage: HZB Copy", hzbCopyProg, "primaryDepth", 0, primaryDepth);
            AssertFboColorAttachment0("Stage: HZB Copy", expectedTextureId: targets.Hzb.Texture.TextureId, expectedMipLevel: 0);
            AssertDrawBuffersForSingleColorTarget("Stage: HZB Copy");
            AssertTexture2DLevelFormatAndSize(
                stage: "Stage: HZB Copy",
                textureId: targets.Hzb.Texture.TextureId,
                mipLevel: 0,
                expectedInternalFormat: PixelInternalFormat.R32f,
                expectedWidth: ScreenWidth,
                expectedHeight: ScreenHeight);
            TestFramework.RenderQuad(hzbCopyProg);
            targets.Hzb.Unbind();
            AssertNoGLError("Stage: HZB Copy");

            // Downsample mip0->mip1 and mip1->mip2
            for (int dstMip = 1; dstMip <= 2; dstMip++)
            {
                int srcMip = dstMip - 1;

                targets.Hzb.BindMipForWrite(dstMip);
                GL.UseProgram(hzbDownProg);
                targets.Hzb.Texture.Bind(0);
                SetSampler(hzbDownProg, "hzbDepth", 0);
                SetInt(hzbDownProg, "srcMip", srcMip);

                AssertSampler2DBinding($"Stage: HZB Downsample mip{dstMip}", hzbDownProg, "hzbDepth", 0, targets.Hzb.Texture);
                AssertFboColorAttachment0($"Stage: HZB Downsample mip{dstMip}", expectedTextureId: targets.Hzb.Texture.TextureId, expectedMipLevel: dstMip);
                AssertDrawBuffersForSingleColorTarget($"Stage: HZB Downsample mip{dstMip}");
                AssertTexture2DLevelFormatAndSize(
                    stage: $"Stage: HZB Downsample mip{dstMip}",
                    textureId: targets.Hzb.Texture.TextureId,
                    mipLevel: dstMip,
                    expectedInternalFormat: PixelInternalFormat.R32f,
                    expectedWidth: Math.Max(1, ScreenWidth >> dstMip),
                    expectedHeight: Math.Max(1, ScreenHeight >> dstMip));
                TestFramework.RenderQuad(hzbDownProg);
                targets.Hzb.Unbind();
                AssertNoGLError($"Stage: HZB Downsample mip{dstMip}");
            }

            var hzbMip0 = targets.Hzb.Texture.ReadPixels(mipLevel: 0);
            var hzbMip2 = targets.Hzb.Texture.ReadPixels(mipLevel: 2);
            AssertAllFinite(hzbMip0, "Stage: HZB mip0");
            AssertAllFinite(hzbMip2, "Stage: HZB mip2");

            // Uniform depth scene => mip0 and mip2 should match 0.5.
            Assert.All(hzbMip0, v => Assert.InRange(v, 0.5f - TestEpsilon, 0.5f + TestEpsilon));
            Assert.InRange(hzbMip2[0], 0.5f - TestEpsilon, 0.5f + TestEpsilon);

            // -----------------------------------------------------------------
            // Stage: LumOn Probe Anchor
            // -----------------------------------------------------------------
            GL.UseProgram(anchorProg);
            SetSampler(anchorProg, "primaryDepth", 0);
            SetSampler(anchorProg, "gBufferNormal", 1);

            SetMat4(anchorProg, "invProjectionMatrix", invProj);
            SetMat4(anchorProg, "invViewMatrix", identity);

            SetInt(anchorProg, "probeSpacing", ProbeSpacing);
            SetVec2(anchorProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetVec2(anchorProg, "screenSize", ScreenWidth, ScreenHeight);

            SetInt(anchorProg, "frameIndex", 0);
            SetInt(anchorProg, "anchorJitterEnabled", 0);
            SetFloat(anchorProg, "anchorJitterScale", 0f);

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                anchorProg,
                invProjectionMatrix: invProj,
                invViewMatrix: identity,
                probeSpacing: ProbeSpacing,
                frameIndex: 0,
                anchorJitterEnabled: 0,
                anchorJitterScale: 0f,
                pmjCycleLength: 1);

            SetFloat(anchorProg, "zNear", ZNear);
            SetFloat(anchorProg, "zFar", ZFar);
            SetFloat(anchorProg, "depthDiscontinuityThreshold", 0.1f);
            GL.UseProgram(0);

            primaryDepth.Bind(0);
            gBufferNormal.Bind(1);

            AssertSampler2DBinding("Stage: Probe Anchor", anchorProg, "primaryDepth", 0, primaryDepth);
            AssertSampler2DBinding("Stage: Probe Anchor", anchorProg, "gBufferNormal", 1, gBufferNormal);

            AssertGBufferFboAttachments("Stage: Probe Anchor", targets.ProbeAnchor);
            TestFramework.RenderQuadTo(anchorProg, targets.ProbeAnchor);
            AssertNoGLError("Stage: Probe Anchor");

            var anchorPosPixels = targets.ProbeAnchor[0].ReadPixels();
            var anchorNormalPixels = targets.ProbeAnchor[1].ReadPixels();
            AssertAllFinite(anchorPosPixels, "Stage: Probe Anchor (pos)");
            AssertAllFinite(anchorNormalPixels, "Stage: Probe Anchor (normal)");

            bool anyValid = false;
            for (int i = 0; i < anchorPosPixels.Length; i += 4)
            {
                if (anchorPosPixels[i + 3] > 0.5f)
                {
                    anyValid = true;
                    break;
                }
            }
            Assert.True(anyValid, "Stage: Probe Anchor → no valid probes");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Trace
            // -----------------------------------------------------------------

            GL.UseProgram(traceProg);
            SetSampler(traceProg, "probeAnchorPosition", 0);
            SetSampler(traceProg, "probeAnchorNormal", 1);
            SetSampler(traceProg, "primaryDepth", 2);
            SetSampler(traceProg, "directDiffuse", 3);
            SetSampler(traceProg, "emissive", 4);
            SetSampler(traceProg, "hzbDepth", 5);
            SetSampler(traceProg, "octahedralHistory", 6);
            SetSampler(traceProg, "probeAtlasMetaHistory", 7);


            SetMat4(traceProg, "invProjectionMatrix", invProj);
            SetMat4(traceProg, "projectionMatrix", proj);
            SetMat4(traceProg, "viewMatrix", identity);
            SetMat4(traceProg, "invViewMatrix", identity);

            SetVec2(traceProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetVec2(traceProg, "screenSize", ScreenWidth, ScreenHeight);

            SetInt(traceProg, "frameIndex", 0);

            SetFloat(traceProg, "zNear", ZNear);
            SetFloat(traceProg, "zFar", ZFar);

            // Deterministic non-zero indirect: allow sky miss fallback to contribute.
            // This makes the one-frame integration test robust even if ray hits are rare.
            SetVec3(traceProg, "sunPosition", 0f, 1f, 0f);
            SetVec3(traceProg, "sunColor", 0.2f, 0.2f, 0.2f);
            SetVec3(traceProg, "ambientColor", 0.1f, 0.1f, 0.1f);

            SetVec3(traceProg, "indirectTint", 1f, 1f, 1f);

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                traceProg,
                invProjectionMatrix: invProj,
                projectionMatrix: proj,
                viewMatrix: identity,
                invViewMatrix: identity,
                frameIndex: 0,
                sunPosition: new Vintagestory.API.MathTools.Vec3f(0f, 1f, 0f),
                sunColor: new Vintagestory.API.MathTools.Vec3f(0.2f, 0.2f, 0.2f),
                ambientColor: new Vintagestory.API.MathTools.Vec3f(0.1f, 0.1f, 0.1f));
            GL.UseProgram(0);

            targets.ProbeAnchor[0].Bind(0);
            targets.ProbeAnchor[1].Bind(1);
            primaryDepth.Bind(2);
            targets.DirectLightingMrt[0].Bind(3);
            targets.DirectLightingMrt[2].Bind(4);
            targets.Hzb.Texture.Bind(5);
            historyRadiance.Bind(6);
            historyMeta.Bind(7);

            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "probeAnchorPosition", 0, targets.ProbeAnchor[0]);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "probeAnchorNormal", 1, targets.ProbeAnchor[1]);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "primaryDepth", 2, primaryDepth);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "directDiffuse", 3, targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "emissive", 4, targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "hzbDepth", 5, targets.Hzb.Texture);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "octahedralHistory", 6, historyRadiance);
            AssertSampler2DBinding("Stage: Atlas Trace", traceProg, "probeAtlasMetaHistory", 7, historyMeta);

            AssertGBufferFboAttachments("Stage: Atlas Trace", targets.AtlasTrace);
            TestFramework.RenderQuadTo(traceProg, targets.AtlasTrace);
            AssertNoGLError("Stage: Atlas Trace");

            var traceRadiance = targets.AtlasTrace[0].ReadPixels();
            var traceMeta = targets.AtlasTrace[1].ReadPixels();
            AssertAllFinite(traceRadiance, "Stage: Atlas Trace (radiance)");
            AssertAllFinite(traceMeta, "Stage: Atlas Trace (meta)");

            // Confidence is stored in meta.r.
            for (int i = 0; i < traceMeta.Length; i += 2)
            {
                Assert.InRange(traceMeta[i + 0], 0.0f, 1.0f);
            }

            AssertStageHasRgbEnergy(traceRadiance, 1e-6f, "Stage: Atlas Trace → radiance");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Temporal
            // -----------------------------------------------------------------
            GL.UseProgram(temporalProg);
            SetVec2(temporalProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetInt(temporalProg, "probeSpacing", ProbeSpacing);
            SetVec2(temporalProg, "screenSize", ScreenWidth, ScreenHeight);

            // Keep jitter off in this test (matches Probe Anchor stage).
            SetInt(temporalProg, "anchorJitterEnabled", 0);
            SetFloat(temporalProg, "anchorJitterScale", 0f);
            SetInt(temporalProg, "pmjCycleLength", 1);

            SetInt(temporalProg, "frameIndex", 0);
            SetFloat(temporalProg, "temporalAlpha", 0.9f);
            SetFloat(temporalProg, "hitDistanceRejectThreshold", 0.3f);

            // Phase 14: enable velocity reprojection (velocity is near-zero in this test)
            SetInt(temporalProg, "enableVelocityReprojection", 1);
            SetFloat(temporalProg, "velocityRejectThreshold", 0.01f);

            SetSampler(temporalProg, "octahedralCurrent", 0);
            SetSampler(temporalProg, "octahedralHistory", 1);
            SetSampler(temporalProg, "probeAnchorPosition", 2);
            SetSampler(temporalProg, "probeAtlasMetaCurrent", 3);
            SetSampler(temporalProg, "probeAtlasMetaHistory", 4);
            SetSampler(temporalProg, "velocityTex", 5);

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                temporalProg,
                probeSpacing: ProbeSpacing,
                frameIndex: 0,
                anchorJitterEnabled: 0,
                anchorJitterScale: 0f,
                pmjCycleLength: 1,
                enableVelocityReprojection: 1,
                velocityRejectThreshold: 0.01f);
            GL.UseProgram(0);

            targets.AtlasTrace[0].Bind(0);
            historyRadiance.Bind(1);
            targets.ProbeAnchor[0].Bind(2);
            targets.AtlasTrace[1].Bind(3);
            historyMeta.Bind(4);
            targets.Velocity[0].Bind(5);

            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "octahedralCurrent", 0, targets.AtlasTrace[0]);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "octahedralHistory", 1, historyRadiance);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "probeAnchorPosition", 2, targets.ProbeAnchor[0]);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "probeAtlasMetaCurrent", 3, targets.AtlasTrace[1]);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "probeAtlasMetaHistory", 4, historyMeta);
            AssertSampler2DBinding("Stage: Atlas Temporal", temporalProg, "velocityTex", 5, targets.Velocity[0]);

            AssertGBufferFboAttachments("Stage: Atlas Temporal", targets.AtlasTemporal);
            TestFramework.RenderQuadTo(temporalProg, targets.AtlasTemporal);
            AssertNoGLError("Stage: Atlas Temporal");

            var temporalRadiance = targets.AtlasTemporal[0].ReadPixels();
            AssertAllFinite(temporalRadiance, "Stage: Atlas Temporal (radiance)");
            AssertStageHasRgbEnergy(temporalRadiance, 1e-6f, "Stage: Atlas Temporal → radiance");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Filter
            // -----------------------------------------------------------------
            GL.UseProgram(filterProg);
            SetVec2(filterProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetInt(filterProg, "filterRadius", 1);
            SetFloat(filterProg, "hitDistanceSigma", 1.0f);

            SetSampler(filterProg, "octahedralAtlas", 0);
            SetSampler(filterProg, "probeAtlasMeta", 1);
            SetSampler(filterProg, "probeAnchorPosition", 2);

            // Phase 23: UBO-backed frame state (probeGridSize).
            UpdateAndBindLumOnFrameUbo(filterProg);
            GL.UseProgram(0);

            targets.AtlasTemporal[0].Bind(0);
            targets.AtlasTemporal[1].Bind(1);
            targets.ProbeAnchor[0].Bind(2);

            AssertSampler2DBinding("Stage: Atlas Filter", filterProg, "octahedralAtlas", 0, targets.AtlasTemporal[0]);
            AssertSampler2DBinding("Stage: Atlas Filter", filterProg, "probeAtlasMeta", 1, targets.AtlasTemporal[1]);
            AssertSampler2DBinding("Stage: Atlas Filter", filterProg, "probeAnchorPosition", 2, targets.ProbeAnchor[0]);

            AssertGBufferFboAttachments("Stage: Atlas Filter", targets.AtlasFiltered);
            TestFramework.RenderQuadTo(filterProg, targets.AtlasFiltered);
            AssertNoGLError("Stage: Atlas Filter");

            var filteredRadiance = targets.AtlasFiltered[0].ReadPixels();
            AssertAllFinite(filteredRadiance, "Stage: Atlas Filter (radiance)");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Gather (half-res)
            // -----------------------------------------------------------------
            GL.UseProgram(gatherProg);
            SetMat4(gatherProg, "invProjectionMatrix", invProj);
            SetMat4(gatherProg, "viewMatrix", identity);

            SetInt(gatherProg, "probeSpacing", ProbeSpacing);
            SetVec2(gatherProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetVec2(gatherProg, "screenSize", ScreenWidth, ScreenHeight);
            SetVec2(gatherProg, "halfResSize", HalfResWidth, HalfResHeight);

            SetFloat(gatherProg, "zNear", ZNear);
            SetFloat(gatherProg, "zFar", ZFar);

            SetFloat(gatherProg, "intensity", 1.0f);
            SetVec3(gatherProg, "indirectTint", 1.0f, 1.0f, 1.0f);
            SetFloat(gatherProg, "leakThreshold", 0.5f);
            SetInt(gatherProg, "sampleStride", 1);

            SetSampler(gatherProg, "octahedralAtlas", 0);
            SetSampler(gatherProg, "probeAnchorPosition", 1);
            SetSampler(gatherProg, "probeAnchorNormal", 2);
            SetSampler(gatherProg, "primaryDepth", 3);
            SetSampler(gatherProg, "gBufferNormal", 4);

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                gatherProg,
                invProjectionMatrix: invProj,
                viewMatrix: identity,
                probeSpacing: ProbeSpacing);
            GL.UseProgram(0);

            targets.AtlasFiltered[0].Bind(0);
            targets.ProbeAnchor[0].Bind(1);
            targets.ProbeAnchor[1].Bind(2);
            primaryDepth.Bind(3);
            gBufferNormal.Bind(4);

            AssertSampler2DBinding("Stage: Gather", gatherProg, "octahedralAtlas", 0, targets.AtlasFiltered[0]);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "probeAnchorPosition", 1, targets.ProbeAnchor[0]);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "probeAnchorNormal", 2, targets.ProbeAnchor[1]);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "primaryDepth", 3, primaryDepth);
            AssertSampler2DBinding("Stage: Gather", gatherProg, "gBufferNormal", 4, gBufferNormal);

            AssertGBufferFboAttachments("Stage: Gather", targets.IndirectHalf);
            TestFramework.RenderQuadTo(gatherProg, targets.IndirectHalf);
            AssertNoGLError("Stage: Gather");

            var indirectHalf = targets.IndirectHalf[0].ReadPixels();
            AssertAllFinite(indirectHalf, "Stage: Gather (indirectHalf)");
            AssertStageHasRgbEnergy(indirectHalf, 1e-6f, "Stage: Gather → indirectHalf");

            // -----------------------------------------------------------------
            // Stage: LumOn Upsample (full-res)
            // -----------------------------------------------------------------
            GL.UseProgram(upsampleProg);
            SetVec2(upsampleProg, "screenSize", ScreenWidth, ScreenHeight);
            SetVec2(upsampleProg, "halfResSize", HalfResWidth, HalfResHeight);

            SetFloat(upsampleProg, "zNear", ZNear);
            SetFloat(upsampleProg, "zFar", ZFar);

            SetInt(upsampleProg, "denoiseEnabled", 1);
            SetFloat(upsampleProg, "upsampleDepthSigma", 0.1f);
            SetFloat(upsampleProg, "upsampleNormalSigma", 16.0f);
            SetFloat(upsampleProg, "upsampleSpatialSigma", 1.0f);

            SetInt(upsampleProg, "holeFillEnabled", 0);
            SetInt(upsampleProg, "holeFillRadius", 2);
            SetFloat(upsampleProg, "holeFillMinConfidence", 0.05f);

            SetSampler(upsampleProg, "indirectHalf", 0);
            SetSampler(upsampleProg, "primaryDepth", 1);
            SetSampler(upsampleProg, "gBufferNormal", 2);

            // Phase 23: UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(upsampleProg);
            GL.UseProgram(0);

            targets.IndirectHalf[0].Bind(0);
            primaryDepth.Bind(1);
            gBufferNormal.Bind(2);

            AssertSampler2DBinding("Stage: Upsample", upsampleProg, "indirectHalf", 0, targets.IndirectHalf[0]);
            AssertSampler2DBinding("Stage: Upsample", upsampleProg, "primaryDepth", 1, primaryDepth);
            AssertSampler2DBinding("Stage: Upsample", upsampleProg, "gBufferNormal", 2, gBufferNormal);

            AssertGBufferFboAttachments("Stage: Upsample", targets.IndirectFull);
            TestFramework.RenderQuadTo(upsampleProg, targets.IndirectFull);
            AssertNoGLError("Stage: Upsample");

            var indirectFull = targets.IndirectFull[0].ReadPixels();
            AssertAllFinite(indirectFull, "Stage: Upsample (indirectFull)");
            AssertStageHasRgbEnergy(indirectFull, 1e-6f, "Stage: Upsample → indirectFull");

            // -----------------------------------------------------------------
            // Stage: PBR Composite
            // -----------------------------------------------------------------
            // Full composite (indirect from pipeline)
            SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            targets.DirectLightingMrt[0].Bind(0);
            targets.DirectLightingMrt[1].Bind(1);
            targets.DirectLightingMrt[2].Bind(2);
            targets.IndirectFull[0].Bind(3);
            gBufferAlbedo.Bind(4);
            gBufferMaterial.Bind(5);
            gBufferNormal.Bind(6);
            primaryDepth.Bind(7);

            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "directDiffuse", 0, targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "directSpecular", 1, targets.DirectLightingMrt[1]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "emissive", 2, targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "indirectDiffuse", 3, targets.IndirectFull[0]);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferAlbedo", 4, gBufferAlbedo);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferMaterial", 5, gBufferMaterial);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferNormal", 6, gBufferNormal);
            AssertSampler2DBinding("Stage: Composite (full)", pbrCompositeProg, "primaryDepth", 7, primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (full)", targets.Composite);
            TestFramework.RenderQuadTo(pbrCompositeProg, targets.Composite);
            AssertNoGLError("Stage: Composite (full)");

            var compositeFull = targets.Composite[0].ReadPixels();
            AssertAllFinite(compositeFull, "Stage: Composite (full)");

            // Baseline (same wiring, but indirectDiffuse is forced to 0)
            SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            targets.DirectLightingMrt[0].Bind(0);
            targets.DirectLightingMrt[1].Bind(1);
            targets.DirectLightingMrt[2].Bind(2);
            zeroIndirectFull.Bind(3);
            gBufferAlbedo.Bind(4);
            gBufferMaterial.Bind(5);
            gBufferNormal.Bind(6);
            primaryDepth.Bind(7);

            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "directDiffuse", 0, targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "directSpecular", 1, targets.DirectLightingMrt[1]);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "emissive", 2, targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "indirectDiffuse", 3, zeroIndirectFull);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferAlbedo", 4, gBufferAlbedo);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferMaterial", 5, gBufferMaterial);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferNormal", 6, gBufferNormal);
            AssertSampler2DBinding("Stage: Composite (baseline)", pbrCompositeProg, "primaryDepth", 7, primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (baseline)", baselineComposite);
            TestFramework.RenderQuadTo(pbrCompositeProg, baselineComposite);
            AssertNoGLError("Stage: Composite (baseline)");

            var compositeBaselinePixels = baselineComposite[0].ReadPixels();
            AssertAllFinite(compositeBaselinePixels, "Stage: Composite (baseline)");

            // Baseline must match "direct-only" expectation (fog disabled, indirect=0):
            // composite.rgb == directDiffuse + directSpecular + emissive.
            AssertCompositeMatchesDirectOnly(
                compositeBaselinePixels,
                directDiffusePixels,
                directSpecularPixels,
                emissivePixels,
                epsilon: 2e-3f);

            // Indirect-injected sanity: bypass LumOn, bind a known constant indirect and prove
            // composite brightens vs baseline. This isolates composite binding/uniform logic.
            SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            targets.DirectLightingMrt[0].Bind(0);
            targets.DirectLightingMrt[1].Bind(1);
            targets.DirectLightingMrt[2].Bind(2);
            injectedIndirectFull.Bind(3);
            gBufferAlbedo.Bind(4);
            gBufferMaterial.Bind(5);
            gBufferNormal.Bind(6);
            primaryDepth.Bind(7);

            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "directDiffuse", 0, targets.DirectLightingMrt[0]);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "directSpecular", 1, targets.DirectLightingMrt[1]);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "emissive", 2, targets.DirectLightingMrt[2]);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "indirectDiffuse", 3, injectedIndirectFull);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferAlbedo", 4, gBufferAlbedo);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferMaterial", 5, gBufferMaterial);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferNormal", 6, gBufferNormal);
            AssertSampler2DBinding("Stage: Composite (injected)", pbrCompositeProg, "primaryDepth", 7, primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (injected)", injectedComposite);
            TestFramework.RenderQuadTo(pbrCompositeProg, injectedComposite);
            AssertNoGLError("Stage: Composite (injected)");

            var injectedCompositePixels = injectedComposite[0].ReadPixels();
            AssertAllFinite(injectedCompositePixels, "Stage: Composite (injected-indirect)");

            float baselineLuma = ComputeAverageLuminance(compositeBaselinePixels);
            float injectedLuma = ComputeAverageLuminance(injectedCompositePixels);
            Assert.True(injectedLuma > baselineLuma + 1e-4f,
                $"Composite did not brighten with injected indirect (baselineLuma={baselineLuma}, injectedLuma={injectedLuma})");

            // Require that the pipeline's composite is measurably different from direct-only baseline.
            float maxDelta = 0f;
            for (int i = 0; i < compositeFull.Length; i++)
            {
                maxDelta = MathF.Max(maxDelta, MathF.Abs(compositeFull[i] - compositeBaselinePixels[i]));
            }

            Assert.True(maxDelta > 1e-4f, $"Composite did not change with indirect enabled (maxDelta={maxDelta})");

            // Final hygiene checkpoint: no GL errors should remain queued for subsequent tests.
            AssertNoGLError("PbrLumOnFullPipelineIntegrationTests end");
        }
        finally
        {
            if (pbrDirectProg != 0) GL.DeleteProgram(pbrDirectProg);
            if (velocityProg != 0) GL.DeleteProgram(velocityProg);
            if (hzbCopyProg != 0) GL.DeleteProgram(hzbCopyProg);
            if (hzbDownProg != 0) GL.DeleteProgram(hzbDownProg);
            if (anchorProg != 0) GL.DeleteProgram(anchorProg);
            if (traceProg != 0) GL.DeleteProgram(traceProg);
            if (temporalProg != 0) GL.DeleteProgram(temporalProg);
            if (filterProg != 0) GL.DeleteProgram(filterProg);
            if (gatherProg != 0) GL.DeleteProgram(gatherProg);
            if (upsampleProg != 0) GL.DeleteProgram(upsampleProg);
            if (pbrCompositeProg != 0) GL.DeleteProgram(pbrCompositeProg);
        }
    }

    private static void AssertAllFinite(float[] values, string stage)
    {
        for (int i = 0; i < values.Length; i++)
        {
            float v = values[i];
            Assert.False(float.IsNaN(v) || float.IsInfinity(v), $"{stage}: non-finite value at idx {i}: {v}");
        }
    }

    private static bool AnyRgbAbove(float[] rgba, float threshold)
    {
        for (int i = 0; i + 2 < rgba.Length; i += 4)
        {
            if (rgba[i + 0] > threshold || rgba[i + 1] > threshold || rgba[i + 2] > threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertStageHasRgbEnergy(float[] rgba, float threshold, string stage)
    {
        if (AnyRgbAbove(rgba, threshold))
        {
            return;
        }

        (float min, float max) = GetMinMax(rgba);
        Assert.Fail($"{stage} unexpectedly has no RGB energy (threshold={threshold}, min={min}, max={max})");
    }

    private static (float min, float max) GetMinMax(float[] values)
    {
        if (values.Length == 0)
        {
            return (0f, 0f);
        }

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < values.Length; i++)
        {
            float v = values[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        return (min, max);
    }

    private static void AssertCompositeMatchesDirectOnly(
        float[] compositeRgba,
        float[] directDiffuseRgba,
        float[] directSpecularRgba,
        float[] emissiveRgba,
        float epsilon)
    {
        Assert.Equal(directDiffuseRgba.Length, compositeRgba.Length);
        Assert.Equal(directSpecularRgba.Length, compositeRgba.Length);
        Assert.Equal(emissiveRgba.Length, compositeRgba.Length);

        for (int i = 0; i + 3 < compositeRgba.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                float expected = directDiffuseRgba[i + c] + directSpecularRgba[i + c] + emissiveRgba[i + c];
                float actual = compositeRgba[i + c];
                float delta = MathF.Abs(actual - expected);
                Assert.True(delta <= epsilon,
                    $"Stage: Composite baseline mismatch at idx {i / 4} channel {c} (expected={expected}, actual={actual}, delta={delta}, eps={epsilon})");
            }
        }
    }

    private static float ComputeAverageLuminance(float[] rgba)
    {
        // Rec.709 luma for quick scalar compare.
        const float wr = 0.2126f;
        const float wg = 0.7152f;
        const float wb = 0.0722f;

        float sum = 0f;
        int count = 0;
        for (int i = 0; i + 2 < rgba.Length; i += 4)
        {
            sum += rgba[i + 0] * wr + rgba[i + 1] * wg + rgba[i + 2] * wb;
            count++;
        }

        return count == 0 ? 0f : sum / count;
    }

    private static void AssertSampler2DBinding(string stage, int programId, string samplerUniform, int expectedUnit, DynamicTexture2D expectedTexture)
    {
        ArgumentNullException.ThrowIfNull(expectedTexture);
        Assert.True(expectedTexture.IsValid, $"{stage}: expected texture for '{samplerUniform}' is invalid/disposed");
        Assert.NotEqual(0, expectedTexture.TextureId);

        int loc = GL.GetUniformLocation(programId, samplerUniform);
        Assert.True(loc >= 0, $"{stage}: sampler uniform '{samplerUniform}' not found");

        GL.GetUniform(programId, loc, out int actualUnit);
        Assert.Equal(expectedUnit, actualUnit);

        // Preserve active texture unit.
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);

        GL.ActiveTexture(TextureUnit.Texture0 + expectedUnit);
        GL.GetInteger(GetPName.TextureBinding2D, out int boundTexId);

        // Restore.
        GL.ActiveTexture((TextureUnit)prevActiveTex);

        Assert.Equal(expectedTexture.TextureId, boundTexId);
    }

    private static void AssertGBufferFboAttachments(string stage, GpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Assert.True(target.IsValid, $"{stage}: target GBuffer is invalid/disposed");

        // Bind (so we can query attachment state) and restore.
        target.BindWithViewport();

        // Ensure we are querying the correct FBO.
        GL.GetInteger(GetPName.FramebufferBinding, out int boundFbo);
        Assert.Equal(target.FboId, boundFbo);

        AssertDrawBuffersForMrtTarget(stage, target.ColorAttachmentCount);

        for (int i = 0; i < target.ColorAttachmentCount; i++)
        {
            var expected = target[i];
            Assert.True(expected.IsValid, $"{stage}: attachment {i} texture invalid");
            AssertFboColorAttachment(
                stage,
                attachmentIndex: i,
                expectedTextureId: expected.TextureId,
                expectedMipLevel: 0);

            AssertTexture2DLevelFormatAndSize(
                stage: stage,
                textureId: expected.TextureId,
                mipLevel: 0,
                expectedInternalFormat: expected.InternalFormat,
                expectedWidth: expected.Width,
                expectedHeight: expected.Height);
        }

        GpuFramebuffer.Unbind();
    }

    private static void AssertFboColorAttachment0(string stage, int expectedTextureId, int expectedMipLevel)
    {
        AssertFboColorAttachment(stage, attachmentIndex: 0, expectedTextureId, expectedMipLevel);
    }

    private static void AssertFboColorAttachment(string stage, int attachmentIndex, int expectedTextureId, int expectedMipLevel)
    {
        var attachment = FramebufferAttachment.ColorAttachment0 + attachmentIndex;

        int type = GetFramebufferAttachmentInt(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentObjectType);
        Assert.Equal(FramebufferAttachmentObjectType.Texture, (FramebufferAttachmentObjectType)type);

        int objectName = GetFramebufferAttachmentInt(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentObjectName);
        Assert.Equal(expectedTextureId, objectName);

        int level = GetFramebufferAttachmentInt(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentTextureLevel);
        Assert.Equal(expectedMipLevel, level);
    }

    private static void AssertDrawBuffersForMrtTarget(string stage, int colorAttachmentCount)
    {
        Assert.True(colorAttachmentCount > 0, $"{stage}: expected at least one color attachment");

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            var pname = (GetPName)((int)GetPName.DrawBuffer0 + i);
            GL.GetInteger(pname, out int drawBufferEnum);

            var expected = (DrawBufferMode)((int)DrawBufferMode.ColorAttachment0 + i);
            Assert.Equal(expected, (DrawBufferMode)drawBufferEnum);
        }
    }

    private static void AssertDrawBuffersForSingleColorTarget(string stage)
    {
        GL.GetInteger(GetPName.DrawBuffer0, out int drawBufferEnum);
        Assert.Equal(DrawBufferMode.ColorAttachment0, (DrawBufferMode)drawBufferEnum);
    }

    private static void AssertTexture2DLevelFormatAndSize(
        string stage,
        int textureId,
        int mipLevel,
        PixelInternalFormat expectedInternalFormat,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.True(textureId != 0, $"{stage}: textureId is 0");

        // Preserve binding.
        GL.GetInteger(GetPName.TextureBinding2D, out int prevBinding);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureInternalFormat, out int internalFormat);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureHeight, out int height);

        GL.BindTexture(TextureTarget.Texture2D, prevBinding);

        Assert.Equal((int)expectedInternalFormat, internalFormat);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    private static int GetFramebufferAttachmentInt(FramebufferTarget target, FramebufferAttachment attachment, FramebufferParameterName pname)
    {
        int[] values = new int[1];
        GL.GetFramebufferAttachmentParameter(target, attachment, pname, values);
        return values[0];
    }

    private static void SetSampler(int programId, string name, int unit)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0)
        {
            GL.Uniform1(loc, unit);
        }
    }

    private static void SetInt(int programId, string name, int value)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0)
        {
            GL.Uniform1(loc, value);
        }
    }

    private static void SetFloat(int programId, string name, float value)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0)
        {
            GL.Uniform1(loc, value);
        }
    }

    private static void SetVec2(int programId, string name, float x, float y)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0)
        {
            GL.Uniform2(loc, x, y);
        }
    }

    private static void SetVec3(int programId, string name, float x, float y, float z)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0)
        {
            GL.Uniform3(loc, x, y, z);
        }
    }

    private static void SetMat4(int programId, string name, float[] matrix)
    {
        int loc = GL.GetUniformLocation(programId, name);
        if (loc >= 0)
        {
            GL.UniformMatrix4(loc, 1, false, matrix);
        }
    }

    private static void SetupPbrCompositeUniforms(int programId, float[] invProjection, float[] viewMatrix, int lumOnEnabled)
    {
        GL.UseProgram(programId);

        SetSampler(programId, "directDiffuse", 0);
        SetSampler(programId, "directSpecular", 1);
        SetSampler(programId, "emissive", 2);
        SetSampler(programId, "indirectDiffuse", 3);
        SetSampler(programId, "gBufferAlbedo", 4);
        SetSampler(programId, "gBufferMaterial", 5);
        SetSampler(programId, "gBufferNormal", 6);
        SetSampler(programId, "primaryDepth", 7);

        // Fog disabled
        int fogColorLoc = GL.GetUniformLocation(programId, "rgbaFogIn");
        if (fogColorLoc >= 0)
        {
            GL.Uniform4(fogColorLoc, 0f, 0f, 0f, 0f);
        }
        SetFloat(programId, "fogDensityIn", 0f);
        SetFloat(programId, "fogMinIn", 0f);

        // Indirect enabled, but intensity/tint can still be tuned.
        SetFloat(programId, "indirectIntensity", 1.0f);
        SetVec3(programId, "indirectTint", 1.0f, 1.0f, 1.0f);
        SetInt(programId, "lumOnEnabled", lumOnEnabled);

        // Keep composite deterministic (no AO/short-range AO).
        SetInt(programId, "enablePbrComposite", 1);
        SetInt(programId, "enableAO", 0);
        SetInt(programId, "enableShortRangeAo", 0);
        SetFloat(programId, "diffuseAOStrength", 1.0f);
        SetFloat(programId, "specularAOStrength", 1.0f);

        SetMat4(programId, "invProjectionMatrix", invProjection);
        SetMat4(programId, "viewMatrix", viewMatrix);

        GL.UseProgram(0);
    }
}
