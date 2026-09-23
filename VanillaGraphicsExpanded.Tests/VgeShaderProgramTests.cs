using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks compatibility shader owners through the production bulk define API.</summary>
public sealed class VgeShaderProgramTests
{
    #region Compatibility settings
    /// <summary>Declares one numeric input and records scheduler requests without loading GPU objects.</summary>
    private sealed class TestProgram : VgeShaderProgram
    {
        private static readonly ShaderOption<int> Option = new("A", 0, minimum: 0, maximum: 2);
        private static readonly GpuShaderContract Contract = ShaderContractFixture.Program(
            fragment: ShaderContractFixture.Stage(constants: [new ShaderSpecialization(0, Option)]), options: [Option]);
        internal override GpuShaderContract ProgramContract => Contract;
        public int RecompileRequests { get; private set; }

        /// <summary>Counts notifications through the existing scheduler boundary.</summary>
        protected override void RequestRecompile()
        {
            RecompileRequests++;
        }
    }

    /// <summary>Changed names and default resets notify once; repeated values do no scheduling.</summary>
    [Fact]
    public void SetDefines_DoesNotRequestRecompile_WhenUnchanged()
    {
        var prog = new TestProgram();

        Assert.True(prog.SetDefines(new Dictionary<string, string?> { ["A"] = "1" }));
        Assert.Equal(1, prog.RecompileRequests);

        Assert.False(prog.SetDefines(new Dictionary<string, string?> { ["A"] = "1" }));
        Assert.Equal(1, prog.RecompileRequests);

        Assert.True(prog.SetDefines(new Dictionary<string, string?> { ["A"] = "2" }));
        Assert.Equal(2, prog.RecompileRequests);

        Assert.True(prog.SetDefines(new Dictionary<string, string?> { ["A"] = null }));
        Assert.Equal(3, prog.RecompileRequests);

        Assert.False(prog.SetDefines(new Dictionary<string, string?> { ["A"] = null }));
        Assert.Equal(3, prog.RecompileRequests);
    }
    #endregion
}
