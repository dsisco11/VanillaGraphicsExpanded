using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates concrete compute facades and interface workflows against a real executable.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuComputeProgramWorkflowTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Every activation publishes and recursion cannot clear an enclosing native binding.</summary>
    [Fact]
    public void ConcreteAndInterfaceActivationPublishAndGuardLifetime()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var owner = new Owner(GpuComputePipeline.DeclareFromAssets(assets.Api, settings));
        IGpuProgram program = owner;
        Assert.True(program.Prepare());
        owner.Probe = true;
        using (owner.UseScope())
        {
            Assert.Equal(1, owner.Publications);
            program.Activate();
            Assert.Equal(2, owner.Publications);
            using (program.BeginUse()) Assert.Equal(3, owner.Publications);
            Assert.Equal(4, owner.Publications);
            Assert.Equal(owner.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        }
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        owner.Fail = true;
        Assert.Throws<InvalidOperationException>(program.Activate);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        owner.Block.SetBytes(new byte[16]);
        owner.Fail = false;
        owner.Use();
        StateCache.Current.UnbindProgram();
        int installed = owner.ProgramId;
        ((IDisposable)owner).Dispose();
        program.Retire(); owner.Dispose();
        Assert.Equal(1, owner.ResourceReleases);
        Assert.False(GL.IsProgram(installed));
        Assert.False(owner.EnsureReady());
        Assert.Throws<ObjectDisposedException>(() => owner.Block.SetBytes(new byte[16]));
        Assert.Throws<ObjectDisposedException>(owner.Use);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>A new compute owner with failed preparation cannot clear an enclosing graphics or compute binding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPreparationPreservesEnclosingOwner(bool graphics)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var programs = new ComponentShaderPrograms();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var outerCompute = new Owner(GpuComputePipeline.DeclareFromAssets(assets.Api, settings));
        var outerGraphics = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        using var scope = graphics ? (IDisposable)outerGraphics.UseScope() : outerCompute.UseScope();
        int expected = GL.GetInteger(GetPName.CurrentProgram);
        var engineOwner = StateCache.ActiveProgram;
        assets.BeforeRead = path =>
        {
            if (path.EndsWith(".spv", StringComparison.Ordinal)) assets.Overrides[path] = new byte[20];
        };
        using var failed = new Owner(GpuComputePipeline.DeclareFromAssets(assets.Api, settings));
        Assert.Throws<InvalidOperationException>(failed.Use);
        Assert.Equal(expected, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Same(engineOwner, StateCache.ActiveProgram);
        Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)failed).BeginUse());
        Assert.Equal(expected, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Same(engineOwner, StateCache.ActiveProgram);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Managed and binding-only scopes reuse the native resource borrower guard against disposal and detach.</summary>
    [Fact]
    public void NativePipelineCannotRetireWhileScoped()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        var native = GpuComputePipeline.DeclareFromAssets(assets.Api, settings);
        using var owner = new Owner(native);
        Assert.True(owner.EnsureReady());
        using (owner.UseScope())
        {
            Assert.Throws<InvalidOperationException>(native.Dispose);
            Assert.Throws<InvalidOperationException>(() => native.Detach());
        }
        using (native.UseScope())
        {
            Assert.Null(StateCache.ActiveProgram);
            Assert.Throws<InvalidOperationException>(native.Dispose);
            Assert.Throws<InvalidOperationException>(() => native.Detach());
        }
        Assert.True(native.IsValid);
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
    }
    /// <summary>Raw compute entry rejects a genuine engine shader before changing its native or managed ownership.</summary>
    [Fact]
    public void RawComputeCannotInterruptNativeShader()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new ComponentShaderPrograms();
        var graphics = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var raw = GpuComputePipeline.DeclareFromAssets(assets.Api, settings);
        Assert.True(raw.EnsureReady());
        var native = new ShaderProgram { ProgramId = graphics.ProgramId };
        StateCache.Current.UseProgram(graphics.ProgramId);
        ShaderProgramBase.CurrentShaderProgram = native;
        try
        {
            Assert.Throws<InvalidOperationException>(raw.Use);
            Assert.Throws<InvalidOperationException>(() => raw.TryUse());
            Assert.Throws<InvalidOperationException>(() => raw.UseScope());
            Assert.Same(native, ShaderProgramBase.CurrentShaderProgram);
            Assert.Equal(graphics.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Null(StateCache.ActiveProgram);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { ShaderProgramBase.CurrentShaderProgram = null; StateCache.Current.UnbindProgram(); }
    }

    /// <summary>Raw compute entry rejects an obsolete installed context tuple before altering a later owner.</summary>
    [Fact]
    public void RawComputeCannotActivateAfterContextGenerationReuse()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new ComponentShaderPrograms();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var raw = GpuComputePipeline.DeclareFromAssets(assets.Api, settings);
        Assert.True(raw.EnsureReady());
        var replacement = new object();
        RenderContextRegistry.Retire(fixture);
        RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
        try
        {
            // The integer GL namespace stays unchanged while its registered lifetime is replaced.
            var later = programs.Create<GpuProgramUseScopeTests.CountingShader>();
            later.Use();
            Assert.Throws<InvalidOperationException>(raw.Use);
            Assert.Throws<InvalidOperationException>(() => raw.TryUse());
            Assert.Throws<InvalidOperationException>(() => raw.UseScope());
            Assert.Same(later, StateCache.ActiveProgram);
            Assert.Equal(later.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
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

    #region Private
    /// <summary>Uses production compute preparation and scopes while observing the Submit boundary.</summary>
    private sealed class Owner : GpuComputeProgram
    {
        #region Public API
        /// <summary>Counts complete input publication attempts.</summary>
        public int Publications;
        /// <summary>Counts terminal cleanup of extra feature resources.</summary>
        public int ResourceReleases;
        /// <summary>Enables recursive admission checks during publication.</summary>
        public bool Probe;
        /// <summary>Requests a controlled publication failure.</summary>
        public bool Fail;
        /// <summary>Owns CPU storage independently of the executable.</summary>
        public PackedUniformBuffer Block { get; }
        /// <summary>Adopts the singular compute pipeline and registers owned CPU storage.</summary>
        public Owner(GpuComputePipeline pipeline) : base(pipeline)
        {
            Block = OwnUniformBuffer(new PackedUniformBuffer(16));
        }
        #endregion
        #region Protected API
        /// <summary>Checks recursive rejection before observing the still-active executable.</summary>
        protected override void Submit()
        {
            if (Probe)
            {
                Assert.Throws<InvalidOperationException>(Use);
                Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)this).Activate());
                Assert.Throws<InvalidOperationException>(() => ((IGpuProgram)this).BeginUse());
                Assert.Throws<InvalidOperationException>(() => ((IDisposable)this).Dispose());
                Assert.Throws<InvalidOperationException>(() => Block.SetBytes(new byte[16]));
                Assert.Equal(ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            }
            if (Fail) throw new InvalidOperationException("publication");
            Publications++;
        }
        /// <summary>Observes the extra-resource cleanup hook without owning another native executable.</summary>
        protected override void ReleaseResources() => ResourceReleases++;
        #endregion
    }
    #endregion
}
