using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.PBR;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;


namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares production submission against the retained partial-state renderer on identical shader inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class DirectLightingSubmissionTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>All radiance attachments and alpha match for background, diffuse, metal, lights and explicit position.</summary>
    [Theory]
    [InlineData(32, 24)]
    [InlineData(31, 19)]
    public void ProductionMatchesReferenceAcrossFrozenReceivers(int width, int height)
    {
        EnsureContextValid();
        // This integration fixture exercises the installed manager registry, including per-allocation geometry revisions.
        fixture.InitializeResourceDisposal();
        try
        {
        using var scene = new DirectLightingComparisonFixture(width, height);
        using var declarations = new StaticPassDeclarationCapture();
        output.WriteLine($"{GL.GetString(StringName.Vendor)} / {GL.GetString(StringName.Renderer)} / {GL.GetString(StringName.Version)}; {width}x{height}");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var prepared = scene.Candidate.PrepareBoundaryPipeline()!; timer.Stop();
        output.WriteLine($"Prepared realization {timer.Elapsed.TotalMilliseconds:F3} ms; single nonstructural production variant; no specialization overrides; DropShadowIntensity controls cascades.");
        Assert.IsType<RHIRenderPassDescription>(scene.Actual.Pass);
        for (int kind = 0; kind < 7; kind++)
        {
            scene.Inputs(kind); scene.DrawReference(); Hostile();
            int before = scene.Draws;
            int drawFbo = GL.GetInteger(GetPName.DrawFramebufferBinding), readFbo = GL.GetInteger(GetPName.ReadFramebufferBinding);
            Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Assert.Equal(before + 1, scene.Draws);
            Compare(scene, kind == 0);
            if(kind==2)Assert.True(ImageAssertions.Inspect(scene.Actual.Radiance,selection:new(new(0,DirectLightingTargets.EmissiveLayer),0,0,0,scene.Actual.Radiance.Width,scene.Actual.Radiance.Height,1)).Maximum>.01f);
            AssertHostile(width, height, drawFbo, readFbo);
            Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
            if (kind is 3 or 6)
            {
                var selection=new RHITextureRegion(new(0,DirectLightingTargets.DiffuseLayer),0,0,0,scene.Actual.Radiance.Width,scene.Actual.Radiance.Height,1);
                using var shadowed=new GpuImageSnapshot(scene.Actual.Radiance,selection);
                scene.Uniforms.DropShadowIntensity = 0;
                Assert.True(scene.Candidate.RenderLighting(scene.Actual));
                using var unshadowed=new GpuImageSnapshot(scene.Actual.Radiance,selection);
                var brighter=ImageAssertions.Predicate(unshadowed.Texture,"return bvec2(true,any(greaterThan(a,b+vec4(.001))));",shadowed.Texture);
                brighter.AssertEqual();Assert.True(brighter.MatchingTexels>0);
                scene.Uniforms.DropShadowIntensity = 1;
                Assert.True(scene.Candidate.RenderLighting(scene.Actual));
            }
        }
        scene.Neutral();
        Assert.True(scene.Candidate.RenderLighting());
        for (int attachment = 0; attachment < 3; attachment++)
        {
            var selection=new RHITextureRegion(new(0,attachment),0,0,0,scene.Actual.Radiance.Width,scene.Actual.Radiance.Height,1);
            using var expected=new GpuImageSnapshot(Assert.IsType<Texture3D>(scene.Normal.Colors[attachment].Image!.Value.Texture.Resource),selection);
            ImageAssertions.CompareHistory(scene.Actual.Radiance,expected,selection).AssertEqual();
        }
        var unchangedPipeline = scene.Candidate.PrepareBoundaryPipeline();
        // Publish the changed inputs through the candidate first, so reference activation cannot hide skipped updates.
        scene.Inputs(2, .25f); Hostile();
        Assert.True(scene.Candidate.RenderLighting(scene.Actual)); scene.DrawReference(); Compare(scene, false);
        Assert.Same(unchangedPipeline, scene.Candidate.PrepareBoundaryPipeline());
        scene.FailPreparation(true);
        int failedDraws = scene.Draws;
        Assert.False(scene.Candidate.RenderLighting(scene.Actual)); Assert.Equal(failedDraws, scene.Draws);
        scene.FailPreparation(false);
        Assert.True(scene.Candidate.RenderLighting(scene.Actual));
        scene.Neutral();
        var pipeline = scene.Candidate.PrepareBoundaryPipeline()!;
#if DEBUG
        using var reflection = new ProgramAcquisitionCapture();
        int reflectionReads = reflection.Reads;
        long queries = OpenGlStateCache.Current.BoundaryQueries, calls = OpenGlStateCache.Current.FixedFunctionCalls;
#endif
        timer.Restart();
        Assert.True(scene.Candidate.RenderLighting(scene.Actual));
        timer.Stop();
#if DEBUG
        output.WriteLine($"Whole repeated submission {timer.Elapsed.TotalMilliseconds:F3} ms; fixed-function calls {OpenGlStateCache.Current.FixedFunctionCalls - calls}; repeated production draw boundary queries: {OpenGlStateCache.Current.BoundaryQueries - queries}; graphics reflection queries: {reflection.Reads - reflectionReads}");
#endif
        Assert.Same(pipeline, scene.Candidate.PrepareBoundaryPipeline());
#if DEBUG
        Assert.Equal(reflectionReads, reflection.Reads);
#endif
        scene.FailPreparation(false);
        Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Compare(scene, false);
        Assert.NotSame(pipeline, scene.Candidate.PrepareBoundaryPipeline());
        scene.ResizeOutputs(17, 13);
        scene.DrawReference(); Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Compare(scene, false);
        Hostile();
        int failureDraw = GL.GetInteger(GetPName.DrawFramebufferBinding), failureRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        scene.FailDraw = true;
        Assert.ThrowsAny<Exception>(() => scene.Candidate.RenderLighting(scene.Actual));
        Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
        AssertHostile(width, height, failureDraw, failureRead);
        scene.FailDraw = false;
        Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Compare(scene, false);
        declarations.AssertReusable();
        }
        finally { fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Compares every original channel against the independent reference with its float absolute-plus-relative tolerance.</summary>
    private void Compare(DirectLightingComparisonFixture scene, bool zero)
    {
        for (int attachment = 0; attachment < 3; attachment++)
        {
            var selection=new RHITextureRegion(new(0,attachment),0,0,0,scene.Actual.Radiance.Width,scene.Actual.Radiance.Height,1);
            using var expected=new GpuImageSnapshot(scene.Expected.Radiance,selection);
            using var actual=new GpuImageSnapshot(scene.Actual.Radiance,selection);
            var proof=ImageAssertions.Predicate(actual.Texture,"""
                precise vec4 difference=abs(a-b);
                precise vec4 allowed=vec4(.001)+.002*abs(b);
                bool finite=!any(isnan(a)) && !any(isinf(a)) && !any(isnan(b)) && !any(isinf(b));
                return bvec2(finite && all(lessThanEqual(difference,allowed)) && (multipliers.x==0 || (all(equal(a,vec4(0))) && all(equal(b,vec4(0))))),false);
                """,expected.Texture,parameters:new(zero?1:0,0,0,0));
            proof.AssertEqual();output.WriteLine($"attachment {attachment}: {proof}");
        }
    }

    /// <summary>Checks native caller state after successful submission and a draw exception.</summary>
    private static void AssertHostile(int width, int height, int draw, int read)
    {
        foreach (var cap in new[] { EnableCap.StencilTest, EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace,
            EnableCap.RasterizerDiscard, EnableCap.ScissorTest, EnableCap.SampleCoverage, EnableCap.ClipDistance0 })
            Assert.True(GL.IsEnabled(cap), cap.ToString());
        bool[] mask = new bool[4]; GL.GetBoolean(GetPName.ColorWritemask, mask); Assert.All(mask, value => Assert.False(value));
        int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport); Assert.Equal(new[] { 0, 0, width, height }, viewport);
        Assert.Equal(draw, GL.GetInteger(GetPName.DrawFramebufferBinding)); Assert.Equal(read, GL.GetInteger(GetPName.ReadFramebufferBinding));
    }
    /// <summary>Models untracked engine state that must be captured, defeated for the draw, and restored.</summary>
    private static void Hostile()
    {
        GL.Enable(EnableCap.StencilTest); GL.StencilFunc(StencilFunction.Never, 1, 255);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Never); GL.DepthMask(true);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.Zero);
        GL.ColorMask(false, false, false, false); GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.RasterizerDiscard); GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0);
        GL.Enable(EnableCap.SampleCoverage); GL.SampleCoverage(0, false);
        GL.Enable(EnableCap.ClipDistance0); OpenGlStateCache.Current.InvalidateAll();
    }
    #endregion
}
