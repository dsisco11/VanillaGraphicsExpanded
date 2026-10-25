using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks initialization intent independently of native allocation and output metadata.</summary>
public sealed class GpuInitializationIntentTests
{
    #region Public API
    /// <summary>Omitted required inputs cannot borrow meaning from their zero-valued backing fields.</summary>
    [Fact]
    public void BufferOmissionDiffersFromExplicitZero()
    {
        var omitted = new GpuBufferInitialization();
        Assert.Throws<ArgumentException>(() => omitted.Validate());
        var realization = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Realization };
        realization.Validate();
        realization.Capacity = 0;
        Assert.Throws<ArgumentException>(() => realization.Validate());
        var mutable = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Mutable, Usage = BufferUsageHint.StaticDraw };
        Assert.Throws<ArgumentException>(() => mutable.Validate());
        mutable.Capacity = 0;
        mutable.Validate();
        var immutable = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Immutable, Capacity = 16 };
        Assert.Throws<ArgumentException>(() => immutable.Validate());
        immutable.StorageFlags = 0;
        immutable.Validate();
    }

    /// <summary>Copying a request preserves assigned inputs while later setters affect only the copy.</summary>
    [Fact]
    public void BufferCopiesRetainIndependentIntent()
    {
        var original = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Mutable, Capacity = 0, Usage = BufferUsageHint.StaticDraw };
        var copy = original;
        copy.StorageFlags = 0;
        Assert.Throws<ArgumentException>(() => copy.Validate());
        original.Validate();
        original.Capacity = 64;
        original.Capacity = 0;
        original.Validate();
        Assert.Equal((nint)0, copy.Capacity);
    }

    /// <summary>Immutable mapping combinations preserve the native dependency rules even when flags are explicitly assigned.</summary>
    [Theory]
    [InlineData(BufferStorageFlags.MapPersistentBit, false)]
    [InlineData(BufferStorageFlags.MapCoherentBit, false)]
    [InlineData(BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapReadBit, true)]
    [InlineData(BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapCoherentBit, true)]
    public void ImmutableFlagsRespectDependencies(BufferStorageFlags flags, bool valid)
    {
        var input = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Immutable, Capacity = 16, StorageFlags = flags };
        if (valid) input.Validate();
        else Assert.Throws<ArgumentException>(() => input.Validate());
    }

    /// <summary>Zero, false, and repeated assignments remain explicit input independently of value equality.</summary>
    [Fact]
    public void TextureAssignmentsRetainZeroFalseAndCopyMasks()
    {
        var original = new GpuTextureInitialization();
        Assert.False(original.IsAssigned(GpuTextureInitialization.Inputs.Samples));
        Assert.False(original.IsAssigned(GpuTextureInitialization.Inputs.FixedSampleLocations));
        original.Samples = 0;
        original.FixedSampleLocations = false;
        Assert.True(original.IsAssigned(GpuTextureInitialization.Inputs.Samples | GpuTextureInitialization.Inputs.FixedSampleLocations));
        var copy = original;
        copy.Width = 16;
        copy.Width = 0;
        Assert.True(copy.IsAssigned(GpuTextureInitialization.Inputs.Width));
        Assert.False(original.IsAssigned(GpuTextureInitialization.Inputs.Width));
        Assert.Equal(0, copy.Width);
        Assert.False(copy.FixedSampleLocations);
        Assert.True(copy.IsAssigned(GpuTextureInitialization.Inputs.Samples));
        Assert.False(copy.IsAssigned(GpuTextureInitialization.Inputs.InternalFormat));
    }
    #endregion
}
