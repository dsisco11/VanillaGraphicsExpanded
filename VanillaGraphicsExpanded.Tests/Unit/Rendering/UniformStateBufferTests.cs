using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks owned numeric snapshots and exact bit preservation in the migrated block.</summary>
public sealed class UniformStateBufferTests
{
    #region Public API
    /// <summary>Caller mutation and rejected array shapes cannot alter retained shader inputs.</summary>
    [Fact]
    public void ArraysRetainOwnedValuesAndRejectPartialReplacement()
    {
        using var block = new UniformStateBuffer();
        float[] values = [2, 3];
        block.Values = values;
        values[0] = 99;
        var copy = block.Values;
        copy[1] = 99;
        byte[] retained = block.Bytes.ToArray();
        Assert.Throws<ArgumentException>(() => block.Values = [7]);
        Assert.Equal(new float[] { 2, 3 }, block.Values);
        Assert.Equal(retained, block.Bytes.ToArray());
    }

    /// <summary>Adjacent fields retain distinct values, signed zero and a non-symmetric matrix.</summary>
    [Fact]
    public void PackingPreservesBitsAndIndependentFields()
    {
        using var block = new UniformStateBuffer
        {
            Scalar = -0f,
            Values = [BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001)), 3],
            Vector = new(5, 7, 11),
            Transform = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16)
        };
        var words = MemoryMarshal.Cast<byte, int>(block.Bytes);
        Assert.Equal(unchecked((int)0x80000000), words[0]);
        Assert.Equal(unchecked((int)0x7fc00001), words[4]);
        var floats = MemoryMarshal.Cast<byte, float>(block.Bytes);
        Assert.Equal(3, floats[8]);
        Assert.Equal(new float[] { 5, 7, 11 }, floats.Slice(12, 3).ToArray());
        Assert.Equal(Enumerable.Range(1, 16).Select(value => (float)value), floats.Slice(16, 16).ToArray());
        Assert.Equal(new Vector3(5, 7, 11), block.Vector);
    }
    #endregion
}
