using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Exercises generated setters through the actual GpuProgram update hook.</summary>
public sealed class GeneratedShaderAccessorTests
{
    #region Public API
    #region Accessor behavior
    /// <summary>Defaults, canonical alias updates and no-op writes use existing scheduling.</summary>
    [Fact]
    public void AccessorsValidateAndScheduleOnlyChangedValues()
    {
        // Arrange: Use the generated owner so setter validation and scheduling stay on the production path.
        var shader = new GeneratedAccessorShader();
        // Assert the default values before testing setter transitions.
        Assert.False(shader.Enabled);
        Assert.Equal(10, shader.Steps);
        // Act: Apply an unchanged numeric value.
        shader.Steps = 10;
        // Assert: An unchanged write does not request preparation.
        Assert.Equal(0, shader.ReloadRequests);
        // Act: Apply a valid changed value, then an invalid value.
        shader.Steps = 12;
        // Assert: The value changes while this asset-free owner suppresses real preparation.
        Assert.Equal(12, shader.Steps);
        Assert.Equal(0, shader.ReloadRequests);
        Assert.Throws<ArgumentException>(() => shader.Steps = 0);
        Assert.Equal(12, shader.Steps);
        Assert.Equal(0, shader.ReloadRequests);
        // Act: Exercise the legacy alias and canonical setter through the same generated owner.
        shader.SetDefines(new Dictionary<string, string?> { ["GENERATED_LEGACY"] = "1" });
        // Assert: Alias and typed writes update one selection and schedule one preparation each.
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
    #endregion
}
