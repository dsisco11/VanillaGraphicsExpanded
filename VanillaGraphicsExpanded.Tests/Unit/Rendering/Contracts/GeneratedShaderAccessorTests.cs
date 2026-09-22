using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Exercises generated setters through the actual GpuProgram update hook.</summary>
public sealed class GeneratedShaderAccessorTests
{
    #region Accessor behavior
    /// <summary>Defaults, canonical alias updates and no-op writes use existing scheduling.</summary>
    [Fact]
    public void AccessorsValidateAndScheduleOnlyChangedValues()
    {
        var shader = new GeneratedAccessorShader();
        Assert.False(shader.Enabled);
        Assert.Equal(10, shader.Steps);
        shader.Steps = 10;
        Assert.Equal(0, shader.ReloadRequests);
        shader.Steps = 12;
        Assert.Equal(12, shader.Steps);
        Assert.Equal(0, shader.ReloadRequests);
        Assert.Throws<ArgumentException>(() => shader.Steps = 0);
        Assert.Equal(12, shader.Steps);
        Assert.Equal(0, shader.ReloadRequests);
        shader.SetDefine("GENERATED_LEGACY", "1");
        Assert.True(shader.Enabled);
        shader.Enabled = false;
        Assert.False(shader.Enabled);
        Assert.Equal(12, shader.Steps);
        Assert.Equal(2, shader.ReloadRequests);
        var other = new GeneratedAccessorShader();
        Assert.Equal(10, other.Steps);
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.Registry.FindProgram("generated/accessors"));
    }
    #endregion
}
