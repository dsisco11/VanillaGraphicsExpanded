using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Exercises borrowed generations, stale tokens and binding-only restoration on a real native context.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramScopeIdentityTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Suspended and raw-interrupted generations retain retirement guards; copied tokens end once.</summary>
    [Fact]
    public void BorrowedScopesRejectReplacementAndRetirement()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var second = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var scope = first.UseScope(); var copy = scope;
        using (second.UseScope()) Assert.Throws<InvalidOperationException>(first.Dispose);
        using (StateCache.Current.UseProgramScope(second.ProgramId))
        {
            Assert.Null(StateCache.ActiveProgram);
            Assert.Throws<InvalidOperationException>(first.Dispose);
        }
        Assert.Same(first, StateCache.ActiveProgram);
        Assert.Equal(2, first.Submissions); // Managed nesting replays; the raw binding scope does not.
        first.InvalidateAssets();
        Assert.Throws<InvalidOperationException>(() => first.EnsureReady());
        Assert.Throws<InvalidOperationException>(first.Use);
        Assert.Same(first, StateCache.ActiveProgram);
        using (second.UseScope()) { }
        Assert.Same(first, StateCache.ActiveProgram);
        scope.Dispose(); copy.Dispose();
        Assert.True(first.EnsureReady());
        Assert.Null(StateCache.ActiveProgram);
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
    }
    /// <summary>Direct use establishes a borrowed lifetime and Stop cannot clear a different owner.</summary>
    [Fact]
    public void DirectUseRequiresMatchingStopBeforeRetirement()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var second = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        first.Use();
        Assert.Throws<InvalidOperationException>(first.Dispose);
        second.Use(); first.Stop();
        Assert.Same(second, StateCache.ActiveProgram);
        Assert.Equal(second.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        second.Stop(); first.Dispose(); second.Dispose();
    }
    /// <summary>Out-of-order tokens reject before touching a later activation and remain usable in stack order.</summary>
    [Fact]
    public void OutOfOrderDisposalCannotUnbindLaterOwner()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var second = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var outer = first.UseScope(); var inner = second.UseScope();
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        Assert.Same(second, StateCache.ActiveProgram);
        Assert.Equal(second.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        inner.Dispose(); outer.Dispose();
    }
    /// <summary>A retired raw name cannot be restored even when its integer is still the current native binding.</summary>
    [Fact]
    public void RetiredRawRestorationCannotClearLaterOwner()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var later = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var rawOwner = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        StateCache.Current.UseProgram(rawOwner.ProgramId);
        var token = later.UseScope();
        StateCache.Current.DeleteProgram(rawOwner.ProgramId);
        Assert.Throws<VanillaGraphicsExpanded.Rendering.Shaders.ShaderOwnershipRestoreException>(token.Dispose);
        Assert.Same(later, StateCache.ActiveProgram);
        Assert.Equal(later.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        later.Stop();
        // Native deletion was deliberately external to the owner in this fixture.
        rawOwner.ProgramId = 0;
    }
    /// <summary>An unchanged numeric name cannot authorize restoration of an obsolete executable revision.</summary>
    [Fact]
    public void ExecutableRevisionRejectsRestorationWithoutClearingLaterOwner()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var previous = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var later = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        previous.Use(); var token = later.UseScope();
        var revision = AccessTools.Property(typeof(VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram), "ExecutableRevision");
        ulong original = previous.ExecutableRevision;
        try
        {
            // Model a reused installed identity while deliberately keeping the old native integer unchanged.
            revision.SetValue(previous, original + 1);
            Assert.Throws<VanillaGraphicsExpanded.Rendering.Shaders.ShaderOwnershipRestoreException>(token.Dispose);
            Assert.Same(later, StateCache.ActiveProgram);
            Assert.Equal(later.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        }
        finally { revision.SetValue(previous, original); later.Stop(); }
    }
    /// <summary>Registration reuse invalidates old scope tokens before they can clear a new-context owner.</summary>
    [Fact]
    public void ContextGenerationReuseRejectsOldTokenBeforeNativeCleanup()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var old = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var token = old.UseScope();
        var replacement = new object();
        RenderContextRegistry.Retire(fixture);
        RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
        try
        {
            Assert.Null(StateCache.ActiveProgram);
            var later = programs.Create<GpuProgramUseScopeTests.CountingShader>();
            later.Use();
            Assert.Throws<InvalidOperationException>(token.Dispose);
            Assert.Same(later, StateCache.ActiveProgram);
            Assert.Equal(later.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            later.Stop();
        }
        finally
        {
            StateCache.Current.UnbindProgram();
            RenderContextRegistry.Retire(replacement);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
        }
    }
    #endregion
}
