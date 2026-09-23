using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that production use scopes preserve both engine ownership and actual driver binding.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramUseScopeTests : RenderTestBase
{
    /// <summary>Uses the mandatory headless graphics context.</summary>
    public GpuProgramUseScopeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Activation lifetime
    /// <summary>Switches instances, borrows nested uses of one instance, then restores an inactive engine.</summary>
    [Fact]
    public void NestedAndSequentialUsesRestoreEngineAndDriver()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<LumOnVelocityShaderProgram>();
        var second = programs.Create<LumOnVelocityShaderProgram>();
        using (first.UseScope())
        {
            AssertActive(first);
            using (first.UseScope()) AssertActive(first);
            AssertActive(first);
            using (second.UseScope()) AssertActive(second);
            AssertActive(first);
        }
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        using (second.UseScope()) AssertActive(second);
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An engine-independent binding is restored without inventing an engine shader owner.</summary>
    [Fact]
    public void RestoresRawBindingWithoutClaimingEngineOwnership()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var raw = programs.Create<LumOnVelocityShaderProgram>();
        var nested = programs.Create<LumOnVelocityShaderProgram>();
        // Deliberate low-level precondition: external callers can own a GL-only binding.
        GlStateCache.Current.UseProgram(raw.ProgramId);
        using (nested.UseScope()) AssertActive(nested);
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(raw.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        GlStateCache.Current.UnbindProgram();
    }

    /// <summary>Observes the actual driver state; the cache alone cannot detect a refused engine activation.</summary>
    private static void AssertActive(ShaderProgramBase expected)
    {
        Assert.Same(expected, ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(expected.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        Assert.True(GlStateCache.Current.TryGetCachedCurrentProgram(out int cached));
        Assert.Equal(expected.ProgramId, cached);
    }
    #endregion
}
