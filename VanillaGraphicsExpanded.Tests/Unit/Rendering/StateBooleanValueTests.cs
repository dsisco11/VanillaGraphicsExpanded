using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies independent boolean values and copy semantics across packed state categories.</summary>
public sealed class StateBooleanValueTests
{
    #region Public API
    /// <summary>Every named boolean can change without changing another value or the detached copy.</summary>
    [Theory]
    [InlineData(typeof(DepthState))]
    [InlineData(typeof(BlendState))]
    [InlineData(typeof(RasterizerState))]
    [InlineData(typeof(PrimitiveAssemblyState))]
    [InlineData(typeof(SamplingState))]
    [InlineData(typeof(StencilState))]
    [InlineData(typeof(OutputState))]
    public void BooleanValuesAreIndependent(Type type)
    {
        // Arrange
        var properties = type.GetProperties().Where(p => p.PropertyType == typeof(bool)).ToArray();
        // Assert reflection exposes named boolean properties without raw bool backing fields.
        Assert.NotEmpty(properties);
        Assert.DoesNotContain(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => field.FieldType == typeof(bool));
        // Act and assert a newly constructed value initializes every named boolean to false.
        object state = Activator.CreateInstance(type)!;
        foreach (var property in properties) Assert.Equal(false, property.GetValue(state));
        // Act and assert each pair while the second bit and detached copy remain observable.
        foreach (var first in properties)
        foreach (var second in properties)
        {
            first.SetValue(state, true);
            second.SetValue(state, true);
            object copy = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(state);
            first.SetValue(state, false);
            foreach (var property in properties)
            {
                Assert.Equal(property == second && first != second, property.GetValue(state));
                Assert.Equal(property == first || property == second, property.GetValue(copy));
            }
            second.SetValue(state, false);
            Assert.Equal(Activator.CreateInstance(type), state);
        }
    }

    /// <summary>Packed enables neither encode coverage inversion nor overwrite grouped parameter values.</summary>
    [Fact]
    public void SamplingFlagsPreserveCoverageAndEquality()
    {
        // Arrange
        var original = new SamplingState { SampleCoverage = (.375f, true), MinimumSampleShading = .25f };
        // Act
        var changed = original;
        changed.Multisample = true;
        changed.SampleCoverageEnabled = true;
        changed.SampleMask = true;
        // Assert toggles change only the selected flags and preserve value equality after restoration.
        Assert.Equal(original.SampleCoverage, changed.SampleCoverage);
        Assert.Equal(original.MinimumSampleShading, changed.MinimumSampleShading);
        Assert.NotEqual(original, changed);
        // Act and assert restoring the flags returns the original grouped values and hash.
        changed.Multisample = false;
        changed.SampleCoverageEnabled = false;
        changed.SampleMask = false;
        Assert.Equal(original, changed);
        Assert.Equal(original.GetHashCode(), changed.GetHashCode());
    }
    #endregion
}
