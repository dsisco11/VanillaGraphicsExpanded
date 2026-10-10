using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Exercises default workflow dispatch and exceptional lifetime cleanup without a GL context.</summary>
public sealed class GpuProgramWorkflowTests
{
    #region Public API
    /// <summary>Preparation precedes binding, every activation publishes, and recursive admission owns no cleanup.</summary>
    [Fact]
    public void ActivationOrdersPreparationAndRejectsInnerCleanup()
    {
        var owner = new Owner();
        IGpuProgram program = owner;
        owner.Publication = () =>
        {
            Assert.Throws<InvalidOperationException>(program.Activate);
            Assert.Throws<InvalidOperationException>(() => program.BeginUse());
            Assert.Throws<InvalidOperationException>(() => program.Prepare());
            Assert.Throws<InvalidOperationException>(owner.Dispose);
            Assert.Throws<InvalidOperationException>(() => owner.Block.SetBytes(new byte[16]));
            Assert.Throws<InvalidOperationException>(() => program.RegisterUniform(new PackedUniformBuffer(16)));
            Assert.Equal(0, owner.Clears);
        };
        program.Activate();
        program.Activate();
        Assert.Equal("prepare;bind;publish;prepare;bind;publish;", owner.Events);
        Assert.False(program.Lifetime.IsPublishing);
        owner.Block.SetBytes(new byte[16]);
        owner.Dispose();
    }

    /// <summary>Preparation failure preserves existing binding ownership; publication failure retains cleanup errors.</summary>
    [Fact]
    public void FailedActivationReleasesGuardAndRetainsCleanupFailure()
    {
        var owner = new Owner { Ready = false };
        IGpuProgram program = owner;
        Assert.Throws<InvalidOperationException>(program.Activate);
        Assert.Equal("prepare;", owner.Events);
        Assert.Equal(0, owner.Clears);
        owner.Ready = true;
        owner.Events = "";
        owner.Publication = () => throw new ArgumentException("publish");
        owner.FailClear = true;
        var failure = Assert.Throws<AggregateException>(program.Activate);
        Assert.IsType<ArgumentException>(failure.InnerExceptions[0]);
        Assert.IsType<InvalidOperationException>(failure.InnerExceptions[1]);
        Assert.False(program.Lifetime.IsPublishing);
        owner.Block.SetBytes(new byte[16]);
        owner.FailClear = false;
        owner.Publication = null;
        program.Activate();
        owner.Dispose();
    }

    /// <summary>Shared scope orchestration restores once and preserves publication plus restoration failures.</summary>
    [Fact]
    public void ScopedWorkflowRestoresEnclosingLifetime()
    {
        var owner = new Owner();
        IGpuProgram program = owner;
        using (program.BeginUse()) Assert.Equal("capture;prepare;bind;publish;", owner.Events);
        Assert.Equal("capture;prepare;bind;publish;restore;", owner.Events);
        owner.Events = "";
        owner.Publication = () => throw new ArgumentException("publication");
        owner.FailRestore = true;
        var failure = Assert.Throws<AggregateException>(() => program.BeginUse());
        Assert.IsType<ArgumentException>(failure.InnerExceptions[0]);
        Assert.IsType<InvalidOperationException>(failure.InnerExceptions[1]);
        Assert.Equal("capture;prepare;bind;publish;clear;restore;", owner.Events);
        owner.Dispose();
    }

    /// <summary>All retirement entry points share terminal admission and attempt independent cleanup only once.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RetirementIsTerminalAcrossDispatchRoutes(int route)
    {
        var owner = new Owner { FailResources = true, FailExecutable = true };
        IGpuProgram program = owner;
        Action retire = route switch
        {
            0 => owner.Dispose,
            1 => program.Retire,
            _ => ((IDisposable)owner).Dispose
        };
        var failure = Assert.Throws<AggregateException>(retire);
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal("resources;executable;", owner.Events);
        Assert.Empty(program.Lifetime.OwnedUniforms);
        owner.Dispose(); program.Retire(); ((IDisposable)owner).Dispose();
        Assert.Equal("resources;executable;", owner.Events);
        Assert.False(program.Prepare());
        Assert.Throws<ObjectDisposedException>(program.Activate);
        Assert.Throws<ObjectDisposedException>(() => program.BeginUse());
        Assert.Throws<ObjectDisposedException>(() => owner.Block.SetBytes(new byte[16]));
        Assert.Throws<ObjectDisposedException>(() => program.RegisterUniform(new PackedUniformBuffer(16)));
    }
    #endregion

    #region Private
    /// <summary>Records family hooks while using production default workflow and CPU ownership policy.</summary>
    private sealed class Owner : IGpuProgram
    {
        #region Public API
        /// <summary>Stores stable owner lifecycle state.</summary>
        public GpuProgramLifetime Lifetime { get; } = new();
        /// <summary>Supplies an unused numeric executable view.</summary>
        public int ProgramId => 0;
        /// <summary>Supplies a CPU-only layout view.</summary>
        public GpuProgramLayout ProgramLayout { get; } = new();
        /// <summary>Records observable workflow order.</summary>
        public string Events = "";
        /// <summary>Counts activation cleanup calls.</summary>
        public int Clears;
        /// <summary>Controls preparation success.</summary>
        public bool Ready = true;
        /// <summary>Controls activation cleanup failure.</summary>
        public bool FailClear;
        /// <summary>Controls independent feature cleanup failure.</summary>
        public bool FailResources;
        /// <summary>Controls independent executable cleanup failure.</summary>
        public bool FailExecutable;
        /// <summary>Controls enclosing scope restoration failure.</summary>
        public bool FailRestore;
        /// <summary>Runs controlled publication work.</summary>
        public Action? Publication;
        /// <summary>Owns a block whose writes remain guarded after owner retirement.</summary>
        public PackedUniformBuffer Block { get; }
        /// <summary>Registers the same block twice to check singular CPU ownership.</summary>
        public Owner()
        {
            Block = ((IGpuProgram)this).RegisterUniform(new PackedUniformBuffer(16));
            ((IGpuProgram)this).RegisterUniform(Block);
            Assert.Single(Lifetime.OwnedUniforms);
        }
        /// <summary>Records preparation without allocating a native executable.</summary>
        public bool PrepareExecutable() { Events += "prepare;"; return Ready; }
        /// <summary>Records family binding ownership.</summary>
        public void BindExecutable() { Events += "bind;"; }
        /// <summary>Records publication and invokes controlled user code.</summary>
        public void PublishInputs() { Events += "publish;"; Publication?.Invoke(); }
        /// <summary>Records failed activation cleanup.</summary>
        public void ClearActivation(bool bindingEntered)
        {
            if (!bindingEntered) return;
            Events += "clear;"; Clears++;
            if (FailClear) throw new InvalidOperationException("clear");
        }
        /// <summary>Checks that scoped admission reaches its family hook.</summary>
        public IDisposable OpenUseScope()
        {
            Events += "capture;";
            return new Scope(this);
        }
        /// <summary>Records singular native ownership cleanup.</summary>
        public void ReleaseExecutable()
        {
            Events += "executable;";
            if (FailExecutable) throw new InvalidOperationException("executable");
        }
        /// <summary>Records additional resource cleanup independently of executable cleanup.</summary>
        public void ReleaseResources()
        {
            Events += "resources;";
            if (FailResources) throw new InvalidOperationException("resources");
        }
        /// <summary>Routes concrete and IDisposable lifetime calls to the distinct default workflow.</summary>
        public void Dispose() => ((IGpuProgram)this).Retire();
        #endregion

        #region Private
        /// <summary>Records exactly-once enclosing-scope restoration.</summary>
        private sealed class Scope(Owner owner) : IDisposable
        {
            private bool ended;
            /// <summary>Restores the captured lifetime even after failed publication.</summary>
            public void Dispose()
            {
                if (ended) return;
                ended = true;
                owner.Events += "restore;";
                if (owner.FailRestore) throw new InvalidOperationException("restore");
            }
        }
        #endregion
    }
    #endregion
}
