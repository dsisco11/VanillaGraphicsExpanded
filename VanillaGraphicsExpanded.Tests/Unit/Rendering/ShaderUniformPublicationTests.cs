using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Proves exact retained equality and transactional history without depending on driver error behavior.</summary>
public sealed class ShaderUniformPublicationTests
{
    #region Public API
    /// <summary>Defaults, signed zero, replacements, switches and value changes each require the appropriate upload.</summary>
    [Fact]
    public void SuccessfulHistoryIsScopedToGenerationAndDesiredValues()
    {
        var history = new ShaderUniformPublication<float>();
        object first = new(), second = new();
        int writes = 0;
        void Write(int location, float value) => writes++;
        history.Publish(first, 12, 0f, Write);
        history.Publish(first, 12, 0f, Write);
        Assert.Equal(1, writes);
        history.Publish(first, 12, -0f, Write);
        Assert.Equal(2, writes);
        Assert.Throws<InvalidOperationException>(() => history.Publish(first, 12, 4f,
            (location, value) => throw new InvalidOperationException("Controlled failed writer.")));
        Assert.Equal(2, history.Uploads);
        history.Publish(first, 12, 4f, Write);
        history.Publish(second, 12, 4f, Write);
        history.Publish(first, 12, 4f, Write);
        history.Publish(first, 12, 6f, Write);
        Assert.Equal(6, writes);
        Assert.Equal(6, history.Uploads);
    }

    /// <summary>In-place mutable edits trigger an upload and a failed edit does not replace successful history.</summary>
    [Fact]
    public void MutableInputsOwnSuccessfulSnapshotsAndRetryFailedWrites()
    {
        var history = new ShaderUniformPublication<float[]>();
        object generation = new();
        float[] values = [1, 2];
        int writes = 0;
        void Write(int location, float[] input) => writes++;
        history.Publish(generation, 3, values, Write);
        history.Publish(generation, 3, new float[] { 1, 2 }, Write);
        Assert.Equal(1, writes);
        values[1] = 4;
        Assert.Throws<InvalidOperationException>(() => history.Publish(generation, 3, values,
            (location, input) => throw new InvalidOperationException("Controlled failed writer.")));
        history.Publish(generation, 3, values, Write);
        Assert.Equal(2, writes);
        values[1] = 2;
        history.Publish(generation, 3, values, Write);
        Assert.Equal(3, writes);
    }

    /// <summary>Vector, matrix and array equality compares upload bits rather than approximate or reference equality.</summary>
    [Fact]
    public void ExactEqualityPreservesEverySupportedRepresentation()
    {
        Assert.True(ShaderUniformValue.Equal(2, 2));
        Assert.False(ShaderUniformValue.Equal(false, true));
        Assert.False(ShaderUniformValue.Equal(new Vector2(0f, 1), new Vector2(-0f, 1)));
        Assert.True(ShaderUniformValue.Equal(new Vector3(1, 2, 3), new Vector3(1, 2, 3)));
        Assert.False(ShaderUniformValue.Equal(Vector4.One, Vector4.Zero));
        var changed = Matrix4x4.Identity; changed.M14 = float.Epsilon;
        Assert.False(ShaderUniformValue.Equal(Matrix4x4.Identity, changed));
        float a = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001));
        float b = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00002));
        Assert.True(ShaderUniformValue.Equal(a, a));
        Assert.False(ShaderUniformValue.Equal(a, b));
        Assert.True(ShaderUniformValue.Equal(new[] { Matrix4x4.Identity }, new[] { Matrix4x4.Identity }));
        Assert.False(ShaderUniformValue.Equal(new[] { Vector3.One }, new[] { Vector3.Zero }));
    }
    #endregion
}
