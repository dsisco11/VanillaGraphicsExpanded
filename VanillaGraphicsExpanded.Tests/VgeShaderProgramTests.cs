using VanillaGraphicsExpanded.Rendering.Shaders;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class VgeShaderProgramTests
{
    private sealed class TestProgram : VgeShaderProgram
    {
        public int RecompileRequests { get; private set; }

        protected override void RequestRecompile()
        {
            RecompileRequests++;
        }
    }

    [Fact]
    public void SetDefine_DoesNotRequestRecompile_WhenUnchanged()
    {
        var prog = new TestProgram();

        Assert.True(prog.SetDefine("A", "1"));
        Assert.Equal(1, prog.RecompileRequests);

        Assert.False(prog.SetDefine("A", "1"));
        Assert.Equal(1, prog.RecompileRequests);

        Assert.True(prog.SetDefine("A", "2"));
        Assert.Equal(2, prog.RecompileRequests);

        Assert.True(prog.RemoveDefine("A"));
        Assert.Equal(3, prog.RecompileRequests);

        Assert.False(prog.RemoveDefine("A"));
        Assert.Equal(3, prog.RecompileRequests);
    }
}
