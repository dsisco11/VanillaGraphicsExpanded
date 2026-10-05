using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains image views and indexed buffer ranges in the existing context-wide binding cache.</summary>
internal sealed partial class StateCache
{
    private readonly Dictionary<int, ImageBinding> imageBindings = new();
    private readonly Dictionary<(BufferRangeTarget Target, int Slot), IndexedBufferBinding> indexedBufferBindings = new();
    /// <summary>Reads the range alignment limit from the shared capability owner.</summary>
    internal int StorageBufferOffsetAlignment
    {
        get
        {
            GpuSupport.EnsureCurrentContext();
            return System.Math.Max(1, GpuSupport.ShaderStorageBufferOffsetAlignment);
        }
    }
    /// <summary>Counts actual image and indexed-buffer binds, independently of cache comparisons.</summary>
    internal long ResourceSlotBindCount { get; private set; }
    /// <summary>Counts texture cache observations separately from actual driver texture binds.</summary>
    internal long TextureCacheChecks { get; private set; }
    /// <summary>Counts sampler cache observations separately from actual driver sampler binds.</summary>
    internal long SamplerCacheChecks { get; private set; }
    /// <summary>Counts image publication comparisons, including unchanged views.</summary>
    internal long ImageCacheChecks { get; private set; }
    /// <summary>Counts indexed buffer publication comparisons, including unchanged ranges.</summary>
    internal long IndexedBufferCacheChecks { get; private set; }
    /// <summary>Counts successful driver texture binds through this context cache.</summary>
    internal long TextureBindCount { get; private set; }
    /// <summary>Counts successful driver sampler binds through this context cache.</summary>
    internal long SamplerBindCount { get; private set; }
    /// <summary>Includes all parameters that select an image view.</summary>
    private readonly record struct ImageBinding(int Texture, int Level, bool Layered, int Layer, TextureAccess Access, SizedInternalFormat Format);
    /// <summary>Distinguishes whole-buffer bindings from ranges even when their offsets match.</summary>
    private readonly record struct IndexedBufferBinding(int Buffer, nint Offset, nint Size, bool Range);

    #region Public API
    /// <summary>Withdraws only the generic and indexed associations touched by a failed uniform-range bind.</summary>
    internal void InvalidateUniformRange(int slot)
    {
        indexedBufferBindings.Remove((BufferRangeTarget.UniformBuffer, slot));
        bufferBindingByTarget.Remove(BufferTarget.UniformBuffer);
    }
    /// <summary>Publishes an image view only when the current context assignment differs.</summary>
    internal void BindImageTexture(int unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format)
    {
        var binding = new ImageBinding(texture, level, layered, layer, access, format);
        ImageCacheChecks++;
        if (imageBindings.TryGetValue(unit, out var previous) && previous == binding) return;
        GL.BindImageTexture(unit, texture, level, layered, layer, access, format);
        ResourceSlotBindCount++;
        imageBindings[unit] = binding;
    }

    /// <summary>Reports a retained image assignment without querying driver state.</summary>
    internal bool TryGetCachedImageTexture(int unit, out int texture)
    {
        bool found = imageBindings.TryGetValue(unit, out var binding);
        texture = binding.Texture;
        return found;
    }

    /// <summary>Reports the retained indexed allocation and range without querying driver state.</summary>
    internal bool TryGetCachedIndexedBuffer(BufferRangeTarget target, int slot, out int buffer, out nint offset, out nint size)
    {
        bool found = indexedBufferBindings.TryGetValue((target, slot), out var binding);
        buffer = binding.Buffer;
        offset = binding.Offset;
        size = binding.Size;
        return found;
    }

    /// <summary>Invalidates a borrowed engine texture slot when allocation identity cannot be tracked.</summary>
    internal void InvalidateTextureUnit(int unit)
    {
        if (textureBindingsByUnit != null && unit < textureBindingsByUnit.Length) textureBindingsByUnit[unit]?.Clear();
    }

    #endregion

    #region Private
    /// <summary>Forgets image views using deleted texture names so name reuse always restores binding.</summary>
    private void InvalidateImageTexture(int texture)
    {
        foreach (var key in imageBindings.Where(pair => pair.Value.Texture == texture).Select(pair => pair.Key).ToArray())
            imageBindings.Remove(key);
    }
    #endregion
}
