using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
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
    /// <summary>Every explicit use submits, including interface activation and nested borrowing of one program.</summary>
    [Fact]
    public void EveryUseSubmitsAndRestorationResubmitsRetainedState()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<CountingShader>();
        var second = programs.Create<CountingShader>();
        using (first.UseScope())
        {
            Assert.Equal(1, first.Submissions);
            ((IGpuProgram)first).Activate();
            Assert.Equal(2, first.Submissions);
            using (first.UseScope()) Assert.Equal(3, first.Submissions);
            Assert.Equal(4, first.Submissions);
            using (second.UseScope()) Assert.Equal(1, second.Submissions);
            Assert.Equal(5, first.Submissions);
            ((IGpuProgram)first).Activate();
            Assert.Equal(6, first.Submissions);
            using (((IGpuProgram)second).BeginUse()) Assert.Equal(2, second.Submissions);
            Assert.Equal(7, first.Submissions);
        }
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
    }

    /// <summary>Failed publication clears activation and the nonthrowing path reports failure.</summary>
    [Fact]
    public void SubmissionFailureCannotLeaveExecutableOwnerActive()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<CountingShader>();
        shader.FailSubmission = true;
        Assert.False(shader.TryUse());
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Throws<InvalidOperationException>(shader.Use);
        shader.FailSubmission = false;
        shader.ProbeRecursiveUse = true;
        shader.Use();
        AssertActive(shader);
        shader.Stop();
    }

    /// <summary>Failed publication clears an owner that was already active before resubmission.</summary>
    [Fact]
    public void FailedResubmissionClearsPreviouslyActiveOwnerOnUse()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<CountingShader>();
        shader.Use();
        AssertActive(shader);
        shader.FailSubmission = true;
        Assert.False(shader.TryUse());
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
    }

    /// <summary>Failed re-preparation of an already-active graphics owner withdraws its incompatible activation.</summary>
    [Fact]
    public void FailedPreparationClearsOnlyAlreadyActiveGraphicsOwner()
    {
        EnsureContextValid();
        using var engine = new EngineShaderPlatformScope();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var shader = new CountingShader { PassName = "lumon_velocity" };
        shader.Initialize(assets.Api);
        shader.Use();
        AssertActive(shader);
        assets.BeforeRead = path =>
        {
            if (path.EndsWith(".spv", StringComparison.Ordinal)) assets.Overrides[path] = new byte[20];
        };
        Assert.Throws<InvalidOperationException>(shader.InvalidateAssets);
        AccessTools.Field(typeof(VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram), "reloadRequired").SetValue(shader, true);
        Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)shader).Activate());
        AssertActive(shader);
        shader.Stop();
        Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)shader).Activate());
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>VGE executables cannot enter engine activation or registration contracts.</summary>
    [Fact]
    public void ExecutableOwnersAreIndependentOfEngineTypes()
    {
        var type = typeof(VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram);
        Assert.False(typeof(ShaderProgramBase).IsAssignableFrom(type));
        Assert.False(typeof(Vintagestory.API.Client.IShaderProgram).IsAssignableFrom(type));
    }

    /// <summary>Switches instances, borrows nested uses of one instance, then restores an inactive engine.</summary>
    [Fact]
    public void NestedAndSequentialUsesRestoreEngineAndDriver()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<CountingShader>();
        var second = programs.Create<CountingShader>();
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
        var raw = programs.Create<CountingShader>();
        var nested = programs.Create<CountingShader>();
        // Deliberate low-level precondition: external callers can own a GL-only binding.
        StateCache.Current.UseProgram(raw.ProgramId);
        using (nested.UseScope()) AssertActive(nested);
        Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(raw.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        StateCache.Current.UnbindProgram();
    }

    /// <summary>Observes the actual driver state; the cache alone cannot detect a refused engine activation.</summary>
    private static void AssertActive(VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram expected)
    {
        Assert.Same(expected, StateCache.ActiveProgram);
        Assert.Equal(expected.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        Assert.True(StateCache.Current.TryGetCachedCurrentProgram(out int cached));
        Assert.Equal(expected.ProgramId, cached);
    }
    #endregion

    /// <summary>Uses a production contract while observing publication independently of resource uploads.</summary>
    public sealed class CountingShader : LumOnVelocityShaderProgram
    {
        #region Public API
        /// <summary>Counts completed publication requests.</summary>
        public int Submissions { get; private set; }
        /// <summary>Requests a controlled publication failure.</summary>
        public bool FailSubmission { get; set; }
        /// <summary>Checks that rejected recursive use preserves the enclosing publication activation.</summary>
        public bool ProbeRecursiveUse { get; set; }
        /// <summary>Constructs the isolated counter owner.</summary>
        public CountingShader() { }
        #endregion

        #region Private
        /// <summary>Observes each publication and models a resource submission error.</summary>
        protected override void Submit()
        {
            if (FailSubmission) throw new InvalidOperationException("Controlled submission failure.");
            if (ProbeRecursiveUse)
            {
                Assert.False(TryUse());
                Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)this).Activate());
                Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)this).BeginUse());
                Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)this).Prepare());
                Assert.Throws<InvalidOperationException>(() => ((IDisposable)this).Dispose());
                AssertActive(this);
            }
            Submissions++;
        }
        #endregion
    }
}
