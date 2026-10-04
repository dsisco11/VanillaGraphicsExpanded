using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks conservative registry classification without creating or compiling GPU programs.</summary>
public sealed class SceneColorShaderInventoryTests
{
    #region Public API
    /// <summary>Only published capabilities for the current executable admit a scene producer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProducerRequiresPublishedCurrentCapability(bool materialCapture)
    {
        var capability = materialCapture ? ShaderCapability.SceneMaterialCapture : ShaderCapability.SceneColorConvention;
        var program = new ShaderProgram { PassName = "customscene", ProgramId = 17 };
        try
        {
            ShaderCapabilities.Declare(program, capability);
            Assert.False(SceneColorShaderInventory.IsCompatible([program], out var unsupported));
            Assert.Equal("customscene", unsupported);

            ShaderCapabilities.PublishDeclared(program);
            Assert.True(SceneColorShaderInventory.IsCompatible([program], out unsupported));
            Assert.Null(unsupported);

            program.ProgramId = 18;
            Assert.False(SceneColorShaderInventory.IsCompatible([program], out unsupported));
            Assert.Equal("customscene", unsupported);
        }
        finally { ShaderCapabilities.Forget(program); }
    }

    /// <summary>A nonzero handle does not classify an otherwise unknown engine or third-party program.</summary>
    [Fact]
    public void UnknownLinkedNameRejectsInventory()
    {
        var program = new ShaderProgram { PassName = "unknownproducer", ProgramId = 19 };
        Assert.False(SceneColorShaderInventory.IsCompatible([null, program], out var unsupported));
        Assert.Equal("unknownproducer", unsupported);
    }

    /// <summary>Known engine auxiliary programs are accepted only after obtaining an executable handle.</summary>
    [Fact]
    public void EngineAuxiliaryMustBeLinked()
    {
        var program = new ShaderProgramGui { PassName = "gui" };
        Assert.False(SceneColorShaderInventory.IsCompatible([program], out var unsupported));
        Assert.Equal("gui", unsupported);
        program.ProgramId = 20;
        Assert.True(SceneColorShaderInventory.IsCompatible([program], out unsupported));
        Assert.Null(unsupported);
    }

    /// <summary>Third-party subclasses cannot inherit the auxiliary exemption by copying an engine pass name.</summary>
    [Fact]
    public void ThirdPartyAuxiliaryNameRequiresCapability()
    {
        var program = new ThirdPartyGui { PassName = "gui", ProgramId = 21 };
        Assert.False(SceneColorShaderInventory.IsCompatible([program], out var unsupported));
        Assert.Equal("gui", unsupported);
    }

    /// <summary>A builtin class and name cannot exempt memory source or assets from another domain.</summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "thirdparty")]
    public void AuxiliaryRequiresEngineFileSource(bool fromFile, string? domain)
    {
        var program = new ShaderProgramGui
        {
            PassName = "gui", ProgramId = 23, LoadFromFile = fromFile, AssetDomain = domain
        };
        Assert.False(SceneColorShaderInventory.IsCompatible([program], out var unsupported));
        Assert.Equal("gui", unsupported);
    }

    /// <summary>Unused registry slots do not prevent compatible registered programs from being considered.</summary>
    [Fact]
    public void NullSlotsAreIgnored()
    {
        Assert.True(SceneColorShaderInventory.IsCompatible([null, null], out var unsupported));
        Assert.Null(unsupported);
        Assert.True(SceneColorShaderInventory.IsCompatible(
            [null, new ShaderProgramGui { PassName = "gui", ProgramId = 22 }, null], out unsupported));
        Assert.Null(unsupported);
    }
    #endregion

    #region Private
    /// <summary>Models a plugin-owned implementation with an engine auxiliary base class.</summary>
    private sealed class ThirdPartyGui : ShaderProgramGui { }
    #endregion
}
