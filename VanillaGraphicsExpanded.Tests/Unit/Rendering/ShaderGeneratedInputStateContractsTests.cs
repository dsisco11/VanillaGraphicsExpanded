using System.Reflection;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks generated shader owners expose state for exactly their non-buffer shader bindings.</summary>
public sealed class ShaderGeneratedInputStateContractsTests
{
    #region Public API
    /// <summary>Every concrete assembly owner has a value-type state matching its non-UBO bindings.</summary>
    [Fact]
    public void EveryConcreteOwnerHasCompleteGeneratedState()
    {
        // Arrange the concrete graphics and compute owners in the production assembly.
        Type[] owners = typeof(GpuProgram).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsNested
                && (type.IsSubclassOf(typeof(GpuProgram)) || type.IsSubclassOf(typeof(GpuComputeProgram)))
            ).ToArray();

        // Act by reflecting the generated active-state field and each owner's declared bindings.
        Assert.NotEmpty(owners);
        foreach (Type owner in owners)
        {
            FieldInfo? active = owner.GetField("__activeState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            // Assert the generated state type and its fields correspond to the bindings declared by that owner.
            Assert.NotNull(active);
            Assert.Equal(owner.Name + "State", active.FieldType.Name);
            Assert.True(active.FieldType.IsValueType);
            PropertyInfo[] expected = owner.GetInterfaces().SelectMany(type => type.GetProperties())
                .Where(property => property.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(ShaderBindingAttribute)))
                .Where(property => !property.PropertyType.Namespace!.EndsWith(".Contracts", StringComparison.Ordinal))
                .Where(property => !typeof(CpuUniformBuffer).IsAssignableFrom(property.PropertyType)
                    && property.PropertyType != typeof(GpuUniformBuffer))
                .GroupBy(property => property.Name)
                .Select(group => group.First())
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();
            FieldInfo[] fields = active.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.Select(property => property.Name), fields.Select(field => field.Name));
            Assert.Equal(expected.Select(property => property.PropertyType), fields.Select(field => field.FieldType));
        }
    }
    #endregion
}
