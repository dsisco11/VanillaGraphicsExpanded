using System;
using System.Linq;

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

        // Fixed matrices / params.
        float[] invProj = LumOnTestInputFactory.CreateRealisticInverseProjection();
        float[] proj = LumOnTestInputFactory.CreateRealisticProjection();
        float[] identity = LumOnTestInputFactory.CreateIdentityMatrix();

        // Inputs (deterministic).
        var primarySceneData = PbrLumOnPipelineInputFactory.CreatePrimarySceneColorUniform(0.5f, 0.5f, 0.5f);
        var primaryDepthData = PbrLumOnPipelineInputFactory.CreatePrimaryDepthUniform(depthRaw: 0.5f);
        var gBufferAlbedoData = PbrLumOnPipelineInputFactory.CreateGBufferAlbedoUniform(0.6f, 0.4f, 0.2f);
        var gBufferNormalData = PbrLumOnPipelineInputFactory.CreateGBufferNormalEncodedUniform(0f, 0f, 1f);
        var gBufferMaterialData = PbrLumOnPipelineInputFactory.CreateGBufferMaterialUniform(
            roughness: 0.5f,
            metallic: 0.0f,
            emissiveScalar: 0.3f,
            reflectivity: 1.0f);

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

        using var targets = new PbrLumOnPipelineTargets();
        using var baselineComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);

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
            traceProg = CompileShader("lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh");
            temporalProg = CompileShader("lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh");
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
            SetVec3(pbrDirectProg, "rgbaLightIn", 1.0f, 1.0f, 1.0f);
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

            TestFramework.RenderQuadTo(pbrDirectProg, targets.DirectLightingMrt);

            var directDiffusePixels = targets.DirectLightingMrt[0].ReadPixels();
            var emissivePixels = targets.DirectLightingMrt[2].ReadPixels();

            Assert.True(AnyRgbAbove(directDiffusePixels, 1e-4f), "Stage: PBR Direct → directDiffuse.rgb unexpectedly all zero");
            Assert.True(AnyRgbAbove(emissivePixels, 1e-4f), "Stage: PBR Direct → emissive.rgb unexpectedly all zero");
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
            GL.UseProgram(0);

            primaryDepth.Bind(0);
            TestFramework.RenderQuadTo(velocityProg, targets.Velocity);

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
            TestFramework.RenderQuad(hzbCopyProg);
            targets.Hzb.Unbind();

            // Downsample mip0->mip1 and mip1->mip2
            for (int dstMip = 1; dstMip <= 2; dstMip++)
            {
                int srcMip = dstMip - 1;

                targets.Hzb.BindMipForWrite(dstMip);
                GL.UseProgram(hzbDownProg);
                targets.Hzb.Texture.Bind(0);
                SetSampler(hzbDownProg, "hzbDepth", 0);
                SetInt(hzbDownProg, "srcMip", srcMip);
                TestFramework.RenderQuad(hzbDownProg);
                targets.Hzb.Unbind();
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

            SetFloat(anchorProg, "zNear", ZNear);
            SetFloat(anchorProg, "zFar", ZFar);
            SetFloat(anchorProg, "depthDiscontinuityThreshold", 0.1f);
            GL.UseProgram(0);

            primaryDepth.Bind(0);
            gBufferNormal.Bind(1);

            TestFramework.RenderQuadTo(anchorProg, targets.ProbeAnchor);

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
            const int texelsPerFrame = 64; // trace all texels per probe this frame

            GL.UseProgram(traceProg);
            SetSampler(traceProg, "probeAnchorPosition", 0);
            SetSampler(traceProg, "probeAnchorNormal", 1);
            SetSampler(traceProg, "primaryDepth", 2);
            SetSampler(traceProg, "directDiffuse", 3);
            SetSampler(traceProg, "emissive", 4);
            SetSampler(traceProg, "hzbDepth", 5);
            SetSampler(traceProg, "octahedralHistory", 6);
            SetSampler(traceProg, "probeAtlasMetaHistory", 7);

            SetInt(traceProg, "hzbCoarseMip", 0);

            SetMat4(traceProg, "invProjectionMatrix", invProj);
            SetMat4(traceProg, "projectionMatrix", proj);
            SetMat4(traceProg, "viewMatrix", identity);
            SetMat4(traceProg, "invViewMatrix", identity);

            SetVec2(traceProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetVec2(traceProg, "screenSize", ScreenWidth, ScreenHeight);

            SetInt(traceProg, "frameIndex", 0);
            SetInt(traceProg, "texelsPerFrame", texelsPerFrame);

            SetInt(traceProg, "raySteps", 8);
            SetFloat(traceProg, "rayMaxDistance", 2.0f);
            SetFloat(traceProg, "rayThickness", 0.5f);

            SetFloat(traceProg, "zNear", ZNear);
            SetFloat(traceProg, "zFar", ZFar);

            // Deterministic non-zero indirect: allow sky miss fallback to contribute.
            // This makes the one-frame integration test robust even if ray hits are rare.
            SetFloat(traceProg, "skyMissWeight", 1.0f);
            SetVec3(traceProg, "sunPosition", 0f, 1f, 0f);
            SetVec3(traceProg, "sunColor", 0.2f, 0.2f, 0.2f);
            SetVec3(traceProg, "ambientColor", 0.1f, 0.1f, 0.1f);

            SetVec3(traceProg, "indirectTint", 1f, 1f, 1f);
            GL.UseProgram(0);

            targets.ProbeAnchor[0].Bind(0);
            targets.ProbeAnchor[1].Bind(1);
            primaryDepth.Bind(2);
            targets.DirectLightingMrt[0].Bind(3);
            targets.DirectLightingMrt[2].Bind(4);
            targets.Hzb.Texture.Bind(5);
            historyRadiance.Bind(6);
            historyMeta.Bind(7);

            TestFramework.RenderQuadTo(traceProg, targets.AtlasTrace);

            var traceRadiance = targets.AtlasTrace[0].ReadPixels();
            var traceMeta = targets.AtlasTrace[1].ReadPixels();
            AssertAllFinite(traceRadiance, "Stage: Atlas Trace (radiance)");
            AssertAllFinite(traceMeta, "Stage: Atlas Trace (meta)");

            // Confidence is stored in meta.r.
            for (int i = 0; i < traceMeta.Length; i += 2)
            {
                Assert.InRange(traceMeta[i + 0], 0.0f, 1.0f);
            }

            Assert.True(AnyRgbAbove(traceRadiance, 1e-6f), "Stage: Atlas Trace → radiance.rgb unexpectedly all zero");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Temporal
            // -----------------------------------------------------------------
            GL.UseProgram(temporalProg);
            SetVec2(temporalProg, "probeGridSize", ProbeGridWidth, ProbeGridHeight);
            SetInt(temporalProg, "frameIndex", 0);
            SetInt(temporalProg, "texelsPerFrame", texelsPerFrame);
            SetFloat(temporalProg, "temporalAlpha", 0.9f);
            SetFloat(temporalProg, "hitDistanceRejectThreshold", 0.3f);

            SetSampler(temporalProg, "octahedralCurrent", 0);
            SetSampler(temporalProg, "octahedralHistory", 1);
            SetSampler(temporalProg, "probeAnchorPosition", 2);
            SetSampler(temporalProg, "probeAtlasMetaCurrent", 3);
            SetSampler(temporalProg, "probeAtlasMetaHistory", 4);
            GL.UseProgram(0);

            targets.AtlasTrace[0].Bind(0);
            historyRadiance.Bind(1);
            targets.ProbeAnchor[0].Bind(2);
            targets.AtlasTrace[1].Bind(3);
            historyMeta.Bind(4);

            TestFramework.RenderQuadTo(temporalProg, targets.AtlasTemporal);

            var temporalRadiance = targets.AtlasTemporal[0].ReadPixels();
            AssertAllFinite(temporalRadiance, "Stage: Atlas Temporal (radiance)");
            Assert.True(AnyRgbAbove(temporalRadiance, 1e-6f), "Stage: Atlas Temporal → radiance.rgb unexpectedly all zero");

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
            GL.UseProgram(0);

            targets.AtlasTemporal[0].Bind(0);
            targets.AtlasTemporal[1].Bind(1);
            targets.ProbeAnchor[0].Bind(2);

            TestFramework.RenderQuadTo(filterProg, targets.AtlasFiltered);

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
            GL.UseProgram(0);

            targets.AtlasFiltered[0].Bind(0);
            targets.ProbeAnchor[0].Bind(1);
            targets.ProbeAnchor[1].Bind(2);
            primaryDepth.Bind(3);
            gBufferNormal.Bind(4);

            TestFramework.RenderQuadTo(gatherProg, targets.IndirectHalf);

            var indirectHalf = targets.IndirectHalf[0].ReadPixels();
            AssertAllFinite(indirectHalf, "Stage: Gather (indirectHalf)");
            Assert.True(AnyRgbAbove(indirectHalf, 1e-6f), "Stage: Gather → indirectHalf.rgb unexpectedly all zero");

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
            GL.UseProgram(0);

            targets.IndirectHalf[0].Bind(0);
            primaryDepth.Bind(1);
            gBufferNormal.Bind(2);

            TestFramework.RenderQuadTo(upsampleProg, targets.IndirectFull);

            var indirectFull = targets.IndirectFull[0].ReadPixels();
            AssertAllFinite(indirectFull, "Stage: Upsample (indirectFull)");
            Assert.True(AnyRgbAbove(indirectFull, 1e-6f), "Stage: Upsample → indirectFull.rgb unexpectedly all zero");

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

            TestFramework.RenderQuadTo(pbrCompositeProg, targets.Composite);

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

            TestFramework.RenderQuadTo(pbrCompositeProg, baselineComposite);

            var compositeBaselinePixels = baselineComposite[0].ReadPixels();
            AssertAllFinite(compositeBaselinePixels, "Stage: Composite (baseline)");

            // Require that the pipeline's composite is measurably different from direct-only baseline.
            float maxDelta = 0f;
            for (int i = 0; i < compositeFull.Length; i++)
            {
                maxDelta = MathF.Max(maxDelta, MathF.Abs(compositeFull[i] - compositeBaselinePixels[i]));
            }

            Assert.True(maxDelta > 1e-4f, $"Composite did not change with indirect enabled (maxDelta={maxDelta})");
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

        // Keep composite deterministic (no AO/bent normal).
        SetInt(programId, "enablePbrComposite", 1);
        SetInt(programId, "enableAO", 0);
        SetInt(programId, "enableBentNormal", 0);
        SetFloat(programId, "diffuseAOStrength", 1.0f);
        SetFloat(programId, "specularAOStrength", 1.0f);

        SetMat4(programId, "invProjectionMatrix", invProjection);
        SetMat4(programId, "viewMatrix", viewMatrix);

        GL.UseProgram(0);
    }
}
