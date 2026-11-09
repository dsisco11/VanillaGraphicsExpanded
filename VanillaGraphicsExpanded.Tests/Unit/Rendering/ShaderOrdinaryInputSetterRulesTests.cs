using System.Reflection;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks ordinary shader setters retain untouched parameter values without an edit scope.</summary>
public sealed class ShaderOrdinaryInputSetterRulesTests
{
    #region Public API
    /// <summary>Assigning one value preserves other parameters and marks the uniform buffer dirty.</summary>
    [Fact]
    public void OrdinaryAssignmentsRetainOtherParameterValues()
    {
        // Arrange a shader and initialize two independent ordinary input properties.
        var shader = new LumOnUpsampleShaderProgram();
        shader.UpsampleDepthSigma = 0.5f;
        shader.UpsampleNormalSigma = 8f;

        // Act by observing the parameters object after assigning a new depth value.
        var parameters = (LumOnUpsampleParamsUbo)typeof(LumOnUpsampleShaderProgram)
            .GetField("paramsUbo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
        parameters.UpsampleDepthSigma = 2f;

        // Assert the updated value, untouched sibling value, and dirty state.
        Assert.Equal(2f, parameters.UpsampleDepthSigma);
        Assert.Equal(8f, parameters.UpsampleNormalSigma);
        Assert.True(parameters.IsDirty);
    }
    #endregion
}
