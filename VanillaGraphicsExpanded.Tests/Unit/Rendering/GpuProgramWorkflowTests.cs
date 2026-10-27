using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Exercises asset preparation admission and independent terminal cleanup without graphics activation.</summary>
public sealed class GpuProgramWorkflowTests
{
    #region Public API
    /// <summary>Preparation returns family readiness without binding an executable or publishing inputs.</summary>
    [Fact]
    public void PreparationIsAnExplicitAssetOperation()
    {
        using var owner=new Owner();IGpuProgram program=owner;
        Assert.True(program.Prepare());Assert.Equal("prepare;",owner.Events);
        owner.Ready=false;Assert.False(program.Prepare());Assert.Equal("prepare;prepare;",owner.Events);
        owner.Lifetime.SubmissionAdmission=()=>false;Assert.False(program.Prepare());
        Assert.Equal("prepare;prepare;",owner.Events);
    }
    /// <summary>Retirement attempts independent cleanup once, preserving failures and closing CPU block capture.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RetirementIsTerminalAcrossDispatchRoutes(int route)
    {
        var owner=new Owner{FailResources=true,FailExecutable=true};IGpuProgram program=owner;
        Action retire=route switch{0=>owner.Dispose,1=>program.Retire,_=>((IDisposable)owner).Dispose};
        var failure=Assert.Throws<AggregateException>(retire);Assert.Equal(2,failure.InnerExceptions.Count);
        Assert.Equal("resources;executable;",owner.Events);Assert.Empty(program.Lifetime.OwnedUniforms);
        owner.Dispose();program.Retire();((IDisposable)owner).Dispose();Assert.Equal("resources;executable;",owner.Events);
        Assert.False(program.Prepare());
        Assert.Throws<ObjectDisposedException>(()=>owner.Block.SetBytes(new byte[16]));
        Assert.Throws<ObjectDisposedException>(()=>owner.Block.Capture());
        Assert.Throws<ObjectDisposedException>(()=>program.RegisterUniform(new PackedUniformBuffer(16)));
    }
    #endregion

    #region Private
    /// <summary>Supplies only the asset family and cleanup hooks required by the CPU-only workflow contract.</summary>
    private sealed class Owner:IGpuProgram
    {
        public GpuProgramLifetime Lifetime{get;}=new();
        public int ProgramId=>0;
        public ulong ExecutableRevision=>0;
        public (nint Handle,long Generation) ExecutableContext=>default;
        public GpuProgramLayout ProgramLayout{get;}=new();
        public bool RequiresPreparation=>!Ready;
        internal bool Ready=true,FailResources,FailExecutable;
        internal string Events="";
        internal PackedUniformBuffer Block{get;}
        /// <summary>Checks that duplicate explicit adoption retains one original CPU block owner.</summary>
        internal Owner(){Block=((IGpuProgram)this).RegisterUniform(new PackedUniformBuffer(16));((IGpuProgram)this).RegisterUniform(Block);Assert.Single(Lifetime.OwnedUniforms);}
        /// <summary>Reports controlled readiness without any native executable.</summary>
        public bool PrepareExecutable(){Events+="prepare;";return Ready;}
        /// <summary>Rejects capture because this workflow fixture deliberately has no ready RHI executable.</summary>
        public RHIResourceBindings CaptureBindings()=>throw new NotSupportedException("The workflow fixture has no executable.");
        /// <summary>Models independent executable cleanup failure.</summary>
        public void ReleaseExecutable(){Events+="executable;";if(FailExecutable)throw new InvalidOperationException("executable");}
        /// <summary>Models independent extra-resource cleanup failure.</summary>
        public void ReleaseResources(){Events+="resources;";if(FailResources)throw new InvalidOperationException("resources");}
        /// <summary>Uses the shared terminal retirement policy.</summary>
        public void Dispose()=>((IGpuProgram)this).Retire();
    }
    #endregion
}
