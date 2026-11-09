using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

using OpenTK.Graphics.OpenGL;


using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

using Xunit;



using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;
/// <summary>Exercises the complete lighting pipeline with deterministic inputs and owned target storage.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrLumOnFullPipelineIntegrationTests : LumOnShaderFunctionalTestBase
{


    #region Public API
    /// <summary>Uses the shared GL context and its deferred resource cleanup service.</summary>
    public PbrLumOnFullPipelineIntegrationTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Verifies that one frame carries indirect lighting through the final composite.</summary>
    [Fact]
    public void Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite()
    {
        // Arrange: Author deterministic scene, lighting, depth, history, and target inputs for a single frame.
        EnsureShaderTestAvailable();
        fixture.InitializeResourceDisposal();

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

        using var primaryScene = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, primarySceneData);
        using var primaryDepth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, primaryDepthData);
        using var gBufferAlbedo = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, gBufferAlbedoData);
        using var gBufferNormal = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, gBufferNormalData);
        using var gBufferMaterial = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, gBufferMaterialData);

        // Comparison samplers require depth storage, independently of the numerical fixture values.
        using var shadowNear = new DepthTexture(ScreenWidth, ScreenHeight, TextureFormat.DepthComponent32f);
        shadowNear.UploadDataImmediate(shadowNearData);
        using var shadowFar = new DepthTexture(ScreenWidth, ScreenHeight, TextureFormat.DepthComponent32f);
        shadowFar.UploadDataImmediate(shadowFarData);

        using var historyRadiance = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyRadianceData);
        using var historyMeta = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, historyMetaData);

        using var zeroIndirectFull = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, zeroIndirectFullData);
        using var injectedIndirectFull = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, injectedIndirectFullData);

        using var targets = new PbrLumOnPipelineTargets();
        // The composite executable exports color and both refraction outputs, even when only color is inspected.
        // Isolate composite arithmetic: half-float output spacing can exceed the independent sum's
        // tolerance. Direct-lighting inputs retain their original storage and quantization.
        using var baselineComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight,
            TextureFormat.Rgba32f, TextureFormat.Rgba16f, TextureFormat.R32f);
        using var injectedComposite = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight,
            TextureFormat.Rgba32f, TextureFormat.Rgba16f, TextureFormat.R32f);

        {
            var pbrDirectProg = Programs.Create<PBRDirectLightingShaderProgram>();
            var velocityProg = Programs.Create<LumOnVelocityShaderProgram>();
            var hzbCompute = Programs.CreateDepthHierarchy();

            var anchorProg = Programs.Create<LumOnProbeAnchorShaderProgram>();
                var traceProg = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(settings: new Dictionary<string, string?>
                    {
                        ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                        ["VGE_LUMON_RAY_STEPS"] = "8",
                        ["VGE_LUMON_RAY_MAX_DISTANCE"] = "2.0",
                        ["VGE_LUMON_RAY_THICKNESS"] = "0.5",
                        ["VGE_LUMON_HZB_COARSE_MIP"] = "0",
                        ["VGE_LUMON_SKY_MISS_WEIGHT"] = "1.0"
                    });
                var temporalProg = Programs.Create<LumOnScreenProbeAtlasTemporalShaderProgram>(settings: new Dictionary<string, string?>
                    {
                        ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64"
                    });
            var filterProg = Programs.Create<LumOnScreenProbeAtlasFilterShaderProgram>();
            var gatherProg = Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>();
            var upsampleProg = Programs.Create<LumOnUpsampleShaderProgram>();
            var pbrCompositeProg = Programs.Create<PBRCompositeShaderProgram>();

            // Act: Execute the real shader stages in dependency order, carrying their outputs through the frame.
            // -----------------------------------------------------------------
            // Stage: PBR Direct Lighting (MRT)
            // -----------------------------------------------------------------

            // Directional light aligned with the test normal.

            // Shadow uniforms are present but currently not used by the fragment shader.
            // These calls safely no-op if uniforms are optimized out.

            // UBO-backed direct lighting params (VgePbrDirectLightingParamsUBO @ object binding).
            // The shader now aliases former uniform names to this UBO; without binding it, outputs will be black.

            {
                using var directCamera = TestFrameCamera.CreateFromInverseView(invProj, identity);
                pbrDirectProg.FrameInputs = directCamera;

                // Shadows are wired but effectively disabled for this test.
                pbrDirectProg.ToShadowMapSpaceMatrixNear = identity;
                pbrDirectProg.ToShadowMapSpaceMatrixFar = identity;
                pbrDirectProg.ShadowRanges = (0f, 0f);
                pbrDirectProg.ShadowZExtendNear = 0; pbrDirectProg.ShadowZExtendFar = 0; pbrDirectProg.DropShadowIntensity = 0;


                pbrDirectProg.LightDirection = new(0f, 0f, 1f);
                pbrDirectProg.RgbaLightIn = new(0.35f, 0.55f, 0.75f);
                pbrDirectProg.RgbaAmbientIn = new(0,0,0);

                using var sharedLights = new VgeLightsUniformBuffer();

                sharedLights.Capture(0, [], []);

                pbrDirectProg.LightsInputs = sharedLights;
            }

            pbrDirectProg.PrimaryScene = primaryScene;
            pbrDirectProg.PrimaryDepth = primaryDepth;

            // This synthetic outdoor scene has full propagated sunlight at every receiver.
            using var directEnvironment = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0f, 0f, 0f, 1f]);
            using var surface = LayeredTestTexture.Create(ImageAssertions.Programs, gBufferNormal, gBufferMaterial, directEnvironment);
            pbrDirectProg.GBufferSurface = surface;
            pbrCompositeProg.GBufferSurface = surface;
            using var receiverPosition = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, new float[4]);
            pbrDirectProg.GBufferPosition = receiverPosition;
            pbrCompositeProg.GBufferPosition = receiverPosition;
            pbrDirectProg.ShadowMapNear = shadowNear;
            pbrDirectProg.ShadowMapFar = shadowFar;

            // Binding audit (used samplers only)
            AssertSamplerBinding("Stage: PBR Direct", pbrDirectProg, "primaryScene", primaryScene);
            AssertSamplerBinding("Stage: PBR Direct", pbrDirectProg, "primaryDepth", primaryDepth);
            AssertSamplerBinding("Stage: PBR Direct", pbrDirectProg, "gBufferSurface", surface);

            AssertGBufferFboAttachments("Stage: PBR Direct", targets.DirectLightingMrt);
            TestFramework.RenderQuadTo(pbrDirectProg, targets.DirectLightingMrt);
            AssertNoGLError("Stage: PBR Direct");

            var directDiffusePixels = targets.DirectLightingMrt[0];
            var directSpecularPixels = targets.DirectLightingMrt[1];
            var emissivePixels = targets.DirectLightingMrt[2];

            AssertStageHasRgbEnergy(directDiffusePixels, 1e-4f, "Stage: PBR Direct → directDiffuse");
            AssertAllFinite(directSpecularPixels, "Stage: PBR Direct (directSpecular)");
            AssertStageHasRgbEnergy(emissivePixels, 1e-4f, "Stage: PBR Direct → emissive");
            AssertAllFinite(directDiffusePixels, "Stage: PBR Direct (directDiffuse)");
            AssertAllFinite(emissivePixels, "Stage: PBR Direct (emissive)");

            // -----------------------------------------------------------------
            // Stage: LumOn Velocity
            // -----------------------------------------------------------------

            // UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                velocityProg,
                invCurrViewProjMatrix: invProj,
                prevViewProjMatrix: proj,
                historyValid: 1);

            using var patchIdentity=TestFramework.CreateTexture(ScreenWidth,ScreenHeight,TextureFormat.Rgba32ui);
            patchIdentity.UploadDataImmediate(new uint[ScreenWidth*ScreenHeight*4]);
            velocityProg.PatchIdentity=patchIdentity;
            velocityProg.PrimaryDepth = primaryDepth;

            AssertSamplerBinding("Stage: Velocity", velocityProg, "primaryDepth", primaryDepth);
            AssertGBufferFboAttachments("Stage: Velocity", targets.Velocity);
            TestFramework.RenderQuadTo(velocityProg, targets.Velocity);
            AssertNoGLError("Stage: Velocity");

            var velocityPixels = targets.Velocity[0];
            AssertAllFinite(velocityPixels, "Stage: Velocity");

            ImageAssertions.Statistics.Inspect(velocityPixels,-TestEpsilon,TestEpsilon,channelMask:3).AssertFiniteRange();
            // -----------------------------------------------------------------
            // Stage: LumOn HZB build
            // -----------------------------------------------------------------
            targets.Hzb.Prepare(ScreenWidth,ScreenHeight,hzbCompute);
            targets.Hzb.Render(primaryDepth);
            AssertNoGLError("Stage: shared depth hierarchy");
            using var hzb0=new GpuImageSnapshot(targets.Hzb.Texture,GpuImageSnapshot.Mip(targets.Hzb.Texture,0));
            using var hzb2=new GpuImageSnapshot(targets.Hzb.Texture,GpuImageSnapshot.Mip(targets.Hzb.Texture,2));
            AssertAllFinite(hzb0.Texture,"Stage: HZB mip0");AssertAllFinite(hzb2.Texture,"Stage: HZB mip2");
            ImageAssertions.Statistics.Inspect(hzb0.Texture,.5f-TestEpsilon,.5f+TestEpsilon).AssertFiniteRange();
            Assert.InRange(GpuLocalRead.Scalar(hzb2.Texture,0),.5f-TestEpsilon,.5f+TestEpsilon);
            // -----------------------------------------------------------------
            // Stage: LumOn Probe Anchor
            // -----------------------------------------------------------------


            // UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                anchorProg,
                invProjectionMatrix: invProj,
                invViewMatrix: identity,
                probeSpacing: ProbeSpacing,
                frameIndex: 0,
                anchorJitterEnabled: 0,
                anchorJitterScale: 0f,
                pmjCycleLength: 1);

            // UBO-backed probe params.
            anchorProg.DepthDiscontinuityThreshold = .1f;

            anchorProg.PrimaryDepth = primaryDepth;
            anchorProg.GBufferSurface = surface;
            anchorProg.PmjJitter = GetOrCreatePmjJitterTexture(1);

            AssertSamplerBinding("Stage: Probe Anchor", anchorProg, "primaryDepth", primaryDepth);
            AssertSamplerBinding("Stage: Probe Anchor", anchorProg, "gBufferSurface", surface);

            AssertGBufferFboAttachments("Stage: Probe Anchor", targets.ProbeAnchor);
            TestFramework.RenderQuadTo(anchorProg, targets.ProbeAnchor);
            AssertNoGLError("Stage: Probe Anchor");

            var anchorPosPixels = targets.ProbeAnchor[0];
            var anchorNormalPixels = targets.ProbeAnchor[1];
            AssertAllFinite(anchorPosPixels, "Stage: Probe Anchor (pos)");
            AssertAllFinite(anchorNormalPixels, "Stage: Probe Anchor (normal)");

            var valid=ImageAssertions.Predicates.Predicate(anchorPosPixels, MaterialPredicates.PbrLumOnFullPipelineIntegrationTests_Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite);
            valid.AssertEqual();Assert.True(valid.MatchingTexels>0,"Stage: Probe Anchor -> no valid probes");
            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Trace
            // -----------------------------------------------------------------


            // Deterministic non-zero indirect: allow sky miss fallback to contribute.
            // This makes the one-frame integration test robust even if ray hits are rare.

            // UBO-backed frame state.
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

            // UBO-backed probe params (indirectTint/intensity etc).
            traceProg.IndirectTint = new(1,1,1);

            using var anchorInputs1 = LayeredTestTexture.Create(ImageAssertions.Programs, targets.ProbeAnchor[0], targets.ProbeAnchor[1]);
            traceProg.ProbeAnchors = anchorInputs1;
            traceProg.PrimaryDepth = primaryDepth;
            traceProg.SurfaceAlbedo = gBufferAlbedo;
            traceProg.GBufferSurface = surface;
            traceProg.ScreenProbeAtlasHistory = historyRadiance;
            traceProg.HzbDepth = targets.Hzb.Texture;
            traceProg.ScreenProbeAtlasMetaHistory = historyMeta;

            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "probeAnchors", anchorInputs1);
            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "primaryDepth", primaryDepth);
            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "surfaceAlbedo", gBufferAlbedo);
            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "gBufferSurface", surface);
            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "octahedralHistory", historyRadiance);
            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "hzbDepth", targets.Hzb.Texture);
            AssertSamplerBinding("Stage: Atlas Trace", traceProg, "probeAtlasMetaHistory", historyMeta);

            AssertGBufferFboAttachments("Stage: Atlas Trace", targets.AtlasTrace);
            TestFramework.RenderQuadTo(traceProg, targets.AtlasTrace);
            AssertNoGLError("Stage: Atlas Trace");

            var traceRadiance = targets.AtlasTrace[0];
            var traceMeta = targets.AtlasTrace[1];
            AssertAllFinite(traceRadiance, "Stage: Atlas Trace (radiance)");
            AssertAllFinite(traceMeta, "Stage: Atlas Trace (meta)");

            ImageAssertions.Statistics.Inspect(traceMeta,0,1,channelMask:1).AssertFiniteRange();

            AssertStageHasRgbEnergy(traceRadiance, 1e-6f, "Stage: Atlas Trace → radiance");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Temporal
            // -----------------------------------------------------------------

            // Keep jitter off in this test (matches Probe Anchor stage).

            // enable velocity reprojection (velocity is near-zero in this test)

            // UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                temporalProg,
                probeSpacing: ProbeSpacing,
                frameIndex: 0,
                anchorJitterEnabled: 0,
                anchorJitterScale: 0f,
                pmjCycleLength: 1,
                enableVelocityReprojection: 1,
                velocityRejectThreshold: 0.01f);

            // UBO-backed probe params (temporalAlpha/hitDistanceRejectThreshold).
            temporalProg.TemporalAlpha = .9f; temporalProg.HitDistanceRejectThreshold = .3f;

            temporalProg.ScreenProbeAtlasCurrent = targets.AtlasTrace[0];
            temporalProg.ScreenProbeAtlasHistory = historyRadiance;
            temporalProg.ProbeAnchors = anchorInputs1;
            temporalProg.ScreenProbeAtlasMetaCurrent = targets.AtlasTrace[1];
            temporalProg.ScreenProbeAtlasMetaHistory = historyMeta;
            temporalProg.VelocityTex = targets.Velocity[0];
            temporalProg.PmjJitter = GetOrCreatePmjJitterTexture(1);

            AssertSamplerBinding("Stage: Atlas Temporal", temporalProg, "octahedralCurrent", targets.AtlasTrace[0]);
            AssertSamplerBinding("Stage: Atlas Temporal", temporalProg, "octahedralHistory", historyRadiance);
            AssertSamplerBinding("Stage: Atlas Temporal", temporalProg, "probeAnchors", anchorInputs1);
            AssertSamplerBinding("Stage: Atlas Temporal", temporalProg, "probeAtlasMetaCurrent", targets.AtlasTrace[1]);
            AssertSamplerBinding("Stage: Atlas Temporal", temporalProg, "probeAtlasMetaHistory", historyMeta);
            AssertSamplerBinding("Stage: Atlas Temporal", temporalProg, "velocityTex", targets.Velocity[0]);

            AssertGBufferFboAttachments("Stage: Atlas Temporal", targets.AtlasTemporal);
            TestFramework.RenderQuadTo(temporalProg, targets.AtlasTemporal);
            AssertNoGLError("Stage: Atlas Temporal");

            var temporalRadiance = targets.AtlasTemporal[0];
            AssertAllFinite(temporalRadiance, "Stage: Atlas Temporal (radiance)");
            AssertStageHasRgbEnergy(temporalRadiance, 1e-6f, "Stage: Atlas Temporal → radiance");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Filter
            // -----------------------------------------------------------------

            // UBO-backed frame state (probeGridSize).
            UpdateAndBindLumOnFrameUbo(filterProg);

            // UBO-backed probe params (filterRadius/hitDistanceSigma).
            filterProg.FilterRadius = 1; filterProg.HitDistanceSigma = 1;

            filterProg.ScreenProbeAtlas = targets.AtlasTemporal[0];
            filterProg.ScreenProbeAtlasMeta = targets.AtlasTemporal[1];
            filterProg.ProbeAnchors = anchorInputs1;

            AssertSamplerBinding("Stage: Atlas Filter", filterProg, "octahedralAtlas", targets.AtlasTemporal[0]);
            AssertSamplerBinding("Stage: Atlas Filter", filterProg, "probeAtlasMeta", targets.AtlasTemporal[1]);
            AssertSamplerBinding("Stage: Atlas Filter", filterProg, "probeAnchors", anchorInputs1);

            AssertGBufferFboAttachments("Stage: Atlas Filter", targets.AtlasFiltered);
            TestFramework.RenderQuadTo(filterProg, targets.AtlasFiltered);
            AssertNoGLError("Stage: Atlas Filter");

            var filteredRadiance = targets.AtlasFiltered[0];
            AssertAllFinite(filteredRadiance, "Stage: Atlas Filter (radiance)");

            // -----------------------------------------------------------------
            // Stage: LumOn Atlas Gather (half-res)
            // -----------------------------------------------------------------

            // UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(
                gatherProg,
                invProjectionMatrix: invProj,
                viewMatrix: identity,
                probeSpacing: ProbeSpacing);

            // UBO-backed probe params (intensity/indirectTint/leakThreshold/sampleStride).
            gatherProg.Intensity = 1; gatherProg.IndirectTint = [1,1,1]; gatherProg.LeakThreshold = .5f; gatherProg.SampleStride = 1;

            gatherProg.ScreenProbeAtlas = targets.AtlasFiltered[0];
            using var anchorInputs2 = LayeredTestTexture.Create(ImageAssertions.Programs, targets.ProbeAnchor[0], targets.ProbeAnchor[1]);
            gatherProg.ProbeAnchors = anchorInputs2;
            gatherProg.PrimaryDepth = primaryDepth;
            gatherProg.GBufferSurface = surface;

            AssertSamplerBinding("Stage: Gather", gatherProg, "octahedralAtlas", targets.AtlasFiltered[0]);
            AssertSamplerBinding("Stage: Gather", gatherProg, "probeAnchors", anchorInputs2);
            AssertSamplerBinding("Stage: Gather", gatherProg, "primaryDepth", primaryDepth);
            AssertSamplerBinding("Stage: Gather", gatherProg, "gBufferSurface", surface);

            AssertGBufferFboAttachments("Stage: Gather", targets.IndirectHalf);
            TestFramework.RenderQuadTo(gatherProg, targets.IndirectHalf);
            AssertNoGLError("Stage: Gather");

            var indirectHalf = targets.IndirectHalf[0];
            AssertAllFinite(indirectHalf, "Stage: Gather (indirectHalf)");
            AssertStageHasRgbEnergy(indirectHalf, 1e-6f, "Stage: Gather → indirectHalf");

            // -----------------------------------------------------------------
            // Stage: LumOn Upsample (full-res)
            // -----------------------------------------------------------------


            // UBO-backed frame state.
            UpdateAndBindLumOnFrameUbo(upsampleProg);

            // UBO-backed upsample params (depth/normal/spatial sigmas, hole-fill thresholds).
            upsampleProg.UpsampleDepthSigma = .1f; upsampleProg.UpsampleNormalSigma = 16; upsampleProg.UpsampleSpatialSigma = 1; upsampleProg.HoleFillMinConfidence = .05f; upsampleProg.HoleFillRadius = 2;

            upsampleProg.IndirectHalf = targets.IndirectHalf[0];
            upsampleProg.PrimaryDepth = primaryDepth;
            upsampleProg.GBufferSurface = surface;

            AssertSamplerBinding("Stage: Upsample", upsampleProg, "indirectHalf", targets.IndirectHalf[0]);
            AssertSamplerBinding("Stage: Upsample", upsampleProg, "primaryDepth", primaryDepth);
            AssertSamplerBinding("Stage: Upsample", upsampleProg, "gBufferSurface", surface);

            AssertGBufferFboAttachments("Stage: Upsample", targets.IndirectFull);
            TestFramework.RenderQuadTo(upsampleProg, targets.IndirectFull);
            AssertNoGLError("Stage: Upsample");

            var indirectFull = targets.IndirectFull[0];
            AssertAllFinite(indirectFull, "Stage: Upsample (indirectFull)");
            AssertStageHasRgbEnergy(indirectFull, 1e-6f, "Stage: Upsample → indirectFull");

            // -----------------------------------------------------------------
            // Stage: PBR Composite
            // -----------------------------------------------------------------
            // Full composite (indirect from pipeline)
            using var compositeCamera1 = SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            using var fullDirectLighting = LayeredTestTexture.Create(ImageAssertions.Programs, targets.DirectLightingMrt[0], targets.DirectLightingMrt[1], targets.DirectLightingMrt[2]);
            pbrCompositeProg.DirectLighting = fullDirectLighting;
            pbrCompositeProg.IndirectDiffuse = targets.IndirectFull[0];
            pbrCompositeProg.GBufferAlbedo = gBufferAlbedo;
            pbrCompositeProg.GBufferSurface = surface;
            pbrCompositeProg.GBufferSurface = surface;
            pbrCompositeProg.PrimaryDepth = primaryDepth;

            AssertSamplerBinding("Stage: Composite (full)", pbrCompositeProg, "directLighting", fullDirectLighting);
            AssertSamplerBinding("Stage: Composite (full)", pbrCompositeProg, "indirectDiffuse", targets.IndirectFull[0]);
            AssertSamplerBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferAlbedo", gBufferAlbedo);
            AssertSamplerBinding("Stage: Composite (full)", pbrCompositeProg, "gBufferSurface", surface);
            AssertSamplerBinding("Stage: Composite (full)", pbrCompositeProg, "primaryDepth", primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (full)", targets.Composite);
            TestFramework.RenderQuadTo(pbrCompositeProg, targets.Composite);
            AssertNoGLError("Stage: Composite (full)");

            var compositeFull = targets.Composite[0];
            AssertAllFinite(compositeFull, "Stage: Composite (full)");

            // Baseline (same wiring, but indirectDiffuse is forced to 0)
            using var compositeCamera2 = SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            using var baselineDirectLighting = LayeredTestTexture.Create(ImageAssertions.Programs, targets.DirectLightingMrt[0], targets.DirectLightingMrt[1], targets.DirectLightingMrt[2]);
            pbrCompositeProg.DirectLighting = baselineDirectLighting;
            pbrCompositeProg.IndirectDiffuse = zeroIndirectFull;
            pbrCompositeProg.GBufferAlbedo = gBufferAlbedo;
            pbrCompositeProg.GBufferSurface = surface;
            pbrCompositeProg.GBufferSurface = surface;
            pbrCompositeProg.PrimaryDepth = primaryDepth;

            AssertSamplerBinding("Stage: Composite (baseline)", pbrCompositeProg, "directLighting", baselineDirectLighting);
            AssertSamplerBinding("Stage: Composite (baseline)", pbrCompositeProg, "indirectDiffuse", zeroIndirectFull);
            AssertSamplerBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferAlbedo", gBufferAlbedo);
            AssertSamplerBinding("Stage: Composite (baseline)", pbrCompositeProg, "gBufferSurface", surface);
            AssertSamplerBinding("Stage: Composite (baseline)", pbrCompositeProg, "primaryDepth", primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (baseline)", baselineComposite);
            TestFramework.RenderQuadTo(pbrCompositeProg, baselineComposite);
            AssertNoGLError("Stage: Composite (baseline)");

            var compositeBaselinePixels = baselineComposite[0];
            AssertAllFinite(compositeBaselinePixels, "Stage: Composite (baseline)");

            // Baseline must match "direct-only" expectation (fog disabled, indirect=0):
            // composite.rgb == directDiffuse + directSpecular + emissive.
            AssertCompositeMatchesDirectOnly(
                compositeBaselinePixels,
                directDiffusePixels,
                directSpecularPixels,
                emissivePixels,
                epsilon: 2e-3f);

            // Assert: The zero-indirect composite matches direct-only energy; injecting indirect makes the final output brighter.
            // Indirect-injected sanity: bypass LumOn, bind a known constant indirect and prove
            // composite brightens vs baseline. This isolates composite binding/uniform logic.
            using var compositeCamera3 = SetupPbrCompositeUniforms(pbrCompositeProg, invProj, identity, lumOnEnabled: 1);

            using var injectedDirectLighting = LayeredTestTexture.Create(ImageAssertions.Programs, targets.DirectLightingMrt[0], targets.DirectLightingMrt[1], targets.DirectLightingMrt[2]);
            pbrCompositeProg.DirectLighting = injectedDirectLighting;
            pbrCompositeProg.IndirectDiffuse = injectedIndirectFull;
            pbrCompositeProg.GBufferAlbedo = gBufferAlbedo;
            pbrCompositeProg.GBufferSurface = surface;
            pbrCompositeProg.GBufferSurface = surface;
            pbrCompositeProg.PrimaryDepth = primaryDepth;

            AssertSamplerBinding("Stage: Composite (injected)", pbrCompositeProg, "directLighting", injectedDirectLighting);
            AssertSamplerBinding("Stage: Composite (injected)", pbrCompositeProg, "indirectDiffuse", injectedIndirectFull);
            AssertSamplerBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferAlbedo", gBufferAlbedo);
            AssertSamplerBinding("Stage: Composite (injected)", pbrCompositeProg, "gBufferSurface", surface);
            AssertSamplerBinding("Stage: Composite (injected)", pbrCompositeProg, "primaryDepth", primaryDepth);

            AssertGBufferFboAttachments("Stage: Composite (injected)", injectedComposite);
            TestFramework.RenderQuadTo(pbrCompositeProg, injectedComposite);
            AssertNoGLError("Stage: Composite (injected)");

            var injectedCompositePixels = injectedComposite[0];
            AssertAllFinite(injectedCompositePixels, "Stage: Composite (injected-indirect)");

            float baselineLuma = ComputeAverageLuminance(compositeBaselinePixels);
            float injectedLuma = ComputeAverageLuminance(injectedCompositePixels);
            Assert.True(injectedLuma > baselineLuma + 1e-4f,
                $"Composite did not brighten with injected indirect (baselineLuma={baselineLuma}, injectedLuma={injectedLuma})");

            var changed=ImageAssertions.Predicates.Predicate(compositeFull, MaterialPredicates.PbrLumOnFullPipelineIntegrationTests_Integration_OneFrame_FullPipeline_PropagatesIndirectIntoComposite_2,compositeBaselinePixels);
            changed.AssertEqual();Assert.True(changed.MatchingTexels>0,"Composite did not change with indirect enabled.");
            // Final hygiene checkpoint: no GL errors should remain queued for subsequent tests.
            AssertNoGLError("PbrLumOnFullPipelineIntegrationTests end");
        }

    }

    #endregion

    #region Protected API
    /// <summary>Drains owned target storage after all per-test rendering resources have retired.</summary>
    protected override void Dispose(bool disposing)
    {
        try { base.Dispose(disposing); }
        finally { if (disposing) fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    private readonly HeadlessGLFixture fixture;

    /// <summary>Checks every represented stage component while retaining its stage identity in failures.</summary>
    private void AssertAllFinite(GpuTexture image,string stage)
    {
        var proof=ImageAssertions.Statistics.Inspect(image);
        Assert.True(proof.NonFinite==0,$"{stage}: {proof}");
    }

    /// <summary>Requires a positive RGB witness anywhere in the complete original stage output.</summary>
    private void AssertStageHasRgbEnergy(GpuTexture image,float threshold,string stage)
    {
        var proof=ImageAssertions.Predicates.Predicate(image, MaterialPredicates.PbrLumOnFullPipelineIntegrationTests_AssertStageHasRgbEnergy,
            parameters:new(threshold,0,0,0));
        proof.AssertEqual();Assert.True(proof.MatchingTexels>0,$"{stage}: no RGB energy above {threshold}; {proof}");
    }

    /// <summary>Checks every direct-only RGB component against the independent ordered float sum.</summary>
    private void AssertCompositeMatchesDirectOnly(GpuTexture composite,GpuTexture diffuse,GpuTexture specular,GpuTexture emissive,float epsilon)
    {
        ImageAssertions.Predicates.Predicate(composite, MaterialPredicates.PbrLumOnFullPipelineIntegrationTests_AssertCompositeMatchesDirectOnly,
            diffuse,specular,emissive,parameters:new(epsilon,0,0,0)).AssertEqual();
    }

    /// <summary>Preserves the original scalar float Rec.709 expression and ordered accumulation over every pixel.</summary>
    private float ComputeAverageLuminance(GpuTexture image)
    {
        float sum=ImageAssertions.Statistics.OrderedFloatSum(image, MaterialProjections.PbrLumOnFullPipelineIntegrationTests_ComputeAverageLuminance);
        return sum/(image.Width*image.Height);
    }
    /// <summary>Submits the retained inputs before observing the actual driver sampler binding.</summary>
    private static void AssertSamplerBinding(string stage, GpuProgram program, string samplerUniform, GpuTexture expectedTexture)
    {
        using var use = ShaderBindingTestPipeline.Create(program);
        RHICommandContext.Current.SetPipeline(use);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        ArgumentNullException.ThrowIfNull(expectedTexture);
        Assert.True(expectedTexture.IsValid, $"{stage}: expected texture for '{samplerUniform}' is invalid/disposed");
        Assert.NotEqual(0, expectedTexture.TextureId);

        int loc = program.ProgramLayout.BinaryInterface!.GetUniformLocation(samplerUniform);
        Assert.True(loc >= 0, $"{stage}: sampler uniform '{samplerUniform}' not found");

        Assert.True(program.ProgramLayout.TryGetContractSamplerUnit(samplerUniform, out int expectedUnit));
        GL.GetUniform(program.ProgramId, loc, out int actualUnit);
        Assert.Equal(expectedUnit, actualUnit);

        // Preserve active texture unit.
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);

        GL.ActiveTexture(TextureUnit.Texture0 + expectedUnit);
        GL.GetInteger(expectedTexture.TextureTarget == TextureDimension.TwoArray ? GetPName.TextureBinding2DArray : GetPName.TextureBinding2D, out int boundTexId);

        // Restore.
        GL.ActiveTexture((TextureUnit)prevActiveTex);

        Assert.Equal(expectedTexture.TextureId, boundTexId);
    }

    /// <summary>Checks that every G-buffer attachment is valid, bound, and has the expected texture metadata.</summary>
    private static void AssertGBufferFboAttachments(string stage, GpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Assert.True(target.IsValid, $"{stage}: target GBuffer is invalid/disposed");

        // Bind (so we can query attachment state) and restore.
        FramebufferTestSetup.BindForEngineRendering(target);

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

    /// <summary>Checks the texture identity and mip level for color attachment zero.</summary>
    private static void AssertFboColorAttachment0(string stage, int expectedTextureId, int expectedMipLevel)
    {
        AssertFboColorAttachment(stage, attachmentIndex: 0, expectedTextureId, expectedMipLevel);
    }

    /// <summary>Checks the native texture object and mip level for a color attachment.</summary>
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

    /// <summary>Checks that a multi-target framebuffer routes each color output to its matching attachment.</summary>
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

    /// <summary>Checks that a single-color framebuffer routes output zero to color attachment zero.</summary>
    private static void AssertDrawBuffersForSingleColorTarget(string stage)
    {
        GL.GetInteger(GetPName.DrawBuffer0, out int drawBufferEnum);
        Assert.Equal(DrawBufferMode.ColorAttachment0, (DrawBufferMode)drawBufferEnum);
    }

    /// <summary>Checks texture storage format and extent for the requested mip level.</summary>
    private static void AssertTexture2DLevelFormatAndSize(
        string stage,
        int textureId,
        int mipLevel,
        TextureFormat expectedInternalFormat,
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

        Assert.Equal(expectedInternalFormat, OpenGLImageValues.Format((PixelInternalFormat)internalFormat));
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    /// <summary>Reads one native framebuffer attachment property.</summary>
    private static int GetFramebufferAttachmentInt(FramebufferTarget target, FramebufferAttachment attachment, FramebufferParameterName pname)
    {
        int[] values = new int[1];
        GL.GetFramebufferAttachmentParameter(target, attachment, pname, values);
        return values[0];
    }

    /// <summary>Uses production setters for composition parameters, preserving the controlled lighting comparison.</summary>
    private static VgeFrameUniformBuffer SetupPbrCompositeUniforms(PBRCompositeShaderProgram program, float[] invProjection, float[] viewMatrix, int lumOnEnabled)
    {
        var frameCamera = TestFrameCamera.Create(invProjection, viewMatrix);
        program.FrameInputs = frameCamera;
        program.IndirectIntensity = 1; program.IndirectTint = new(1,1,1);
        program.DiffuseAOStrength = 1; program.SpecularAOStrength = 1;
        return frameCamera;
    }
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
        programs.Prepare("projection-material", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion
}
