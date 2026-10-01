using System.Numerics;
using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks migrated multi-value setters without creating a GL context.</summary>
public sealed class UniformBufferCallerTests
{
    #region Public API
    /// <summary>Repeated light arrays remain clean; a later color change still dirties the block.</summary>
    [Fact]
    public void PointLightArraysDetectChangesAcrossBothArrays()
    {
        using var buffer = new PbrDirectLightingParamsUbo();
        float[] positions = [1, 2, 3, 4, 5, 6];
        float[] colors = [7, 8, 9, 10, 11, 12];
        buffer.SetPointLights(2, positions, colors);
        ResetDirty(buffer);
        buffer.SetPointLights(2, positions, colors);
        Assert.False(buffer.IsDirty);
        colors[5] = 13;
        buffer.SetPointLights(2, positions, colors);
        Assert.True(buffer.IsDirty);
        Assert.Equal(13f, UboPacking.ReadFloat(buffer.Bytes, 1952 + 16 + 8));
    }

    /// <summary>Changes confined to the second atmosphere vector cannot be short-circuited.</summary>
    [Fact]
    public void AtmosphereChecksBothVectors()
    {
        using var buffer = new PbrCompositeParamsUbo();
        var lighting = new AtmosphereLighting(Vector3.UnitY, default, default, default, default, []);
        buffer.SetAtmosphere(lighting);
        ResetDirty(buffer);
        buffer.SetAtmosphere(lighting);
        Assert.False(buffer.IsDirty);
        buffer.SetAtmosphere(lighting with { Sun = Vector3.UnitX });
        Assert.True(buffer.IsDirty);
        Assert.Equal(1f, UboPacking.ReadFloat(buffer.Bytes, 208));
    }

    /// <summary>A shared disabled mapping does not create temporary domain changes on repeated calls.</summary>
    [Fact]
    public void SharedNearFieldMappingStaysCleanWhenUnchanged()
    {
        using var buffer = new LumOnNearFieldParamsUbo();
        buffer.SetShared(null, null);
        ResetDirty(buffer);
        buffer.SetShared(null, null);
        Assert.False(buffer.IsDirty);
    }

    /// <summary>Kernel writes detect changes and reject invalid complete sources before mutation.</summary>
    [Fact]
    public void KernelValidationIsAtomicAndIdenticalWeightsStayClean()
    {
        using var buffer = new PbrHeightBakeParamsUbo();
        float[] weights = Enumerable.Range(1, 8).Select(value => (float)value).ToArray();
        buffer.SetKernelWeights(weights, 8);
        ResetDirty(buffer);
        buffer.SetKernelWeights(weights, 8);
        Assert.False(buffer.IsDirty);
        byte[] stored = buffer.Bytes.ToArray();
        Assert.Throws<ArgumentException>(() => buffer.SetKernelWeights(new float[5], 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetKernelWeights(new float[65], 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetKernelWeights(weights, -1));
        Assert.Throws<ArgumentNullException>(() => buffer.SetKernelWeights(null!, 0));
        Assert.Equal(stored, buffer.Bytes.ToArray());
        Assert.False(buffer.IsDirty);
    }
    #endregion

    #region Private
    /// <summary>Simulates successful dirty-state consumption while leaving GPU binding outside these CPU tests.</summary>
    private static void ResetDirty(CpuUniformBuffer buffer)
    {
        foreach (var (name, value) in new (string, object)[] { ("isDirty", false), ("dirtyStartBytes", int.MaxValue), ("dirtyEndExclusiveBytes", 0) })
            typeof(CpuUniformBuffer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(buffer, value);
    }
    #endregion
}
