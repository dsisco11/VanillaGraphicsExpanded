using System.Buffers.Binary;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks independent production setters sharing the probe parameter float vector.</summary>
public sealed class ProbeParameterPackingTests
{
    #region Packed components
    /// <summary>Distinct sentinels expose component swaps and neighboring-value clobbering in either write order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporalAndFilterValuesOccupyIndependentComponents(bool reverse)
    {
        var parameters = new LumOnProbeParamsUbo();
        // These values exercise the actual setters; getter round-trips could share the same wrong offset.
        Action[] writes = [
            () => parameters.TemporalAlpha = 0.125f,
            () => parameters.HitDistanceRejectThreshold = 2.5f,
            () => parameters.HitDistanceSigma = 7.25f,
            () => parameters.LeakThreshold = 0.75f
        ];
        foreach (var write in reverse ? writes.Reverse() : writes) write();
        float[] expected = [0.125f, 2.5f, 7.25f, 0.75f];
        for (int component = 0; component < expected.Length; component++)
            Assert.Equal(expected[component], BinaryPrimitives.ReadSingleLittleEndian(parameters.Bytes.Slice(16 + (component << 2), 4)));
    }
    #endregion
}
