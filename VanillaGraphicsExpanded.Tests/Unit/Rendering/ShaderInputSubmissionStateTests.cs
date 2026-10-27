using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks persistent ordinary assignments and the actual publication guard without graphics activation.</summary>
public sealed class ShaderInputSubmissionStateTests
{
    #region Public API
    /// <summary>Every concrete assembly owner has generator-owned state with exactly its non-UBO binding values.</summary>
    [Fact]
    public void EveryConcreteOwnerHasCompleteGeneratedState()
    {
        var owners = typeof(GpuProgram).Assembly.GetTypes().Where(type => !type.IsAbstract && !type.IsNested &&
            (type.IsSubclassOf(typeof(GpuProgram)) || type.IsSubclassOf(typeof(GpuComputeProgram)))).ToArray();
        Assert.NotEmpty(owners);
        foreach (var owner in owners)
        {
            var active = owner.GetField("__activeState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotNull(active);
            Assert.Equal(owner.Name + "State", active.FieldType.Name);
            Assert.True(active.FieldType.IsValueType);
            var expected = owner.GetInterfaces().SelectMany(type => type.GetProperties())
                .Where(property => property.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(ShaderBindingAttribute)))
                .Where(property => !property.PropertyType.Namespace!.EndsWith(".Contracts", StringComparison.Ordinal))
                .Where(property => !typeof(CpuUniformBuffer).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(GpuUniformBuffer))
                .GroupBy(property => property.Name).Select(group => group.First()).OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            var fields = active.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected.Select(property => property.Name), fields.Select(field => field.Name));
            Assert.Equal(expected.Select(property => property.PropertyType), fields.Select(field => field.FieldType));
        }
    }
    /// <summary>Direct assignments retain omitted parameters without requiring an edit scope.</summary>
    [Fact]
    public void OrdinaryAssignmentsRetainOtherParameterValues()
    {
        var shader = new LumOnUpsampleShaderProgram();
        shader.UpsampleDepthSigma = 0.5f;
        shader.UpsampleNormalSigma = 8f;
        var parameters = (LumOnUpsampleParamsUbo)typeof(LumOnUpsampleShaderProgram)
            .GetField("paramsUbo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
        parameters.UpsampleDepthSigma = 2f;
        Assert.Equal(2f, parameters.UpsampleDepthSigma);
        Assert.Equal(8f, parameters.UpsampleNormalSigma);
        Assert.True(parameters.IsDirty);
    }

    #endregion

}
