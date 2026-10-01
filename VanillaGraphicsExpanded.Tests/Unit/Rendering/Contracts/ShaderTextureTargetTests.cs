using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks the contract values used to cast texture targets at the graphics boundary.</summary>
public sealed class ShaderTextureTargetTests
{
    #region Public API
    /// <summary>Every declared target must preserve its graphics API identity without a mapping table.</summary>
    [Fact]
    public void AllContractTargetsMatchOpenTkValues()
    {
        foreach (ShaderTextureTarget target in Enum.GetValues<ShaderTextureTarget>())
        {
            Assert.True(Enum.TryParse<TextureTarget>(target.ToString(), out var graphicsTarget));
            Assert.Equal((int)graphicsTarget, (int)target);
        }
    }
    #endregion
}
