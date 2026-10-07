using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;


namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares production submission against the retained partial-state renderer on identical shader inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        using var scene = new DirectLightingComparisonFixture(width, height);
        output.WriteLine($"{GL.GetString(StringName.Vendor)} / {GL.GetString(StringName.Renderer)} / {GL.GetString(StringName.Version)}; {width}x{height}");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var prepared = scene.Candidate.PrepareBoundaryPipeline()!; timer.Stop();
        output.WriteLine($"Prepared realization {timer.Elapsed.TotalMilliseconds:F3} ms; single nonstructural production variant; no specialization overrides; DropShadowIntensity controls cascades.");
        output.WriteLine($"Active borrowed integer sampler entries: {prepared.Bindings.Entries.Count(entry => entry.Active && entry.Contract.Kind == VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler)}; each active sampler performs GL.IsTexture validation per submission, excluded from boundary-query counters.");
        output.WriteLine($"Pass-local routing reads: {GpuSupport.Graphics.MaxDrawBuffers} GL.GetInteger calls per pass, excluded from boundary-query counters.");
        for (int kind = 0; kind < 7; kind++)
        {
            scene.Inputs(kind); scene.DrawReference(); Hostile();
            int before = scene.Draws;
            int drawFbo = GL.GetInteger(GetPName.DrawFramebufferBinding), readFbo = GL.GetInteger(GetPName.ReadFramebufferBinding);
            Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Assert.Equal(before + 1, scene.Draws);
            Compare(scene, kind == 0);
            if (kind == 2) Assert.Contains(scene.Actual.Emissive.ReadPixels(), value => value > .01f);
            AssertHostile(width, height, drawFbo, readFbo);
            Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
            if (kind is 3 or 6)
            {
                float[] shadowed = scene.Actual.DirectDiffuse.ReadPixels();
                scene.Uniforms.DropShadowIntensity = 0;
                Assert.True(scene.Candidate.RenderLighting(scene.Actual));
                Assert.True(scene.Actual.DirectDiffuse.ReadPixels().Zip(shadowed).Any(pair => pair.First > pair.Second + .001f));
                scene.Uniforms.DropShadowIntensity = 1;
                Assert.True(scene.Candidate.RenderLighting(scene.Actual));
            }
        }
        scene.Neutral();
        Assert.True(scene.Candidate.RenderLighting());
        for (int attachment = 0; attachment < 3; attachment++)
            Assert.Equal(scene.Actual.Framebuffer![attachment].ReadPixels(), scene.Normal[attachment].ReadPixels());
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
        var shader = pipeline.Shader;
        int reflection = shader.GraphicsInterface!.ReflectionQueries;
        long queries = StateCache.Current.BoundaryQueries, calls = StateCache.Current.FixedFunctionCalls;
        timer.Restart();
        Assert.True(scene.Candidate.RenderLighting(scene.Actual));
        timer.Stop();
        output.WriteLine($"Whole repeated submission {timer.Elapsed.TotalMilliseconds:F3} ms; fixed-function calls {StateCache.Current.FixedFunctionCalls - calls}; repeated production draw boundary queries: {StateCache.Current.BoundaryQueries - queries}; graphics reflection queries: {shader.GraphicsInterface.ReflectionQueries - reflection}");
        Assert.Same(pipeline, scene.Candidate.PrepareBoundaryPipeline());
        Assert.Equal(reflection, shader.GraphicsInterface.ReflectionQueries);
        shader.InvalidateAssets();
        Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Compare(scene, false);
        Assert.NotSame(pipeline, scene.Candidate.PrepareBoundaryPipeline());
        Assert.True(scene.Expected.Resize(17, 13)); Assert.True(scene.Actual.Resize(17, 13));
        scene.DrawReference(); Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Compare(scene, false);
        Hostile();
        int failureDraw = GL.GetInteger(GetPName.DrawFramebufferBinding), failureRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        scene.FailDraw = true;
        Assert.ThrowsAny<Exception>(() => scene.Candidate.RenderLighting(scene.Actual));
        Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
        AssertHostile(width, height, failureDraw, failureRead);
        scene.FailDraw = false;
        Assert.True(scene.Candidate.RenderLighting(scene.Actual)); Compare(scene, false);
    }
    #endregion

    #region Private
    /// <summary>Compares every channel and reports maximum error rather than a selected sample.</summary>
    private void Compare(DirectLightingComparisonFixture scene, bool zero)
    {
        for (int attachment = 0; attachment < 3; attachment++)
        {
            float[] expected = scene.Expected.Framebuffer![attachment].ReadPixels();
            float[] actual = scene.Actual.Framebuffer![attachment].ReadPixels();
            Assert.Equal(expected.Length, actual.Length); float maximum = 0; int mismatches = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(float.IsFinite(expected[i]) && float.IsFinite(actual[i]));
                float error = MathF.Abs(actual[i] - expected[i]); maximum = MathF.Max(maximum, error);
                if (error > .001f + .002f * MathF.Abs(expected[i])) mismatches++;
                if (zero) { Assert.Equal(0, expected[i]); Assert.Equal(0, actual[i]); }
            }
            output.WriteLine($"attachment {attachment}: maximum error {maximum}; mismatched components {mismatches}");
            Assert.Equal(0, mismatches);
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
        GL.Enable(EnableCap.ClipDistance0); StateCache.Current.InvalidateAll();
    }
    #endregion
}
