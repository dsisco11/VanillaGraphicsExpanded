using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Detects interruption of a native slot write without expiring unrelated executed knowledge.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, ulong> textureWrites = new();
    private readonly Dictionary<int, ulong> samplerWrites = new();
    private readonly Dictionary<int, ulong> imageWrites = new();
    private readonly Dictionary<(BufferTarget Target, int Slot), ulong> bufferWrites = new();

    #region Internal API
    /// <summary>Changes only when the active texture selector is assigned or withdrawn.</summary>
    internal ulong ActiveTextureWriteStamp { get; private set; }
    /// <summary>Changes only when executable selection knowledge is assigned or withdrawn.</summary>
    internal ulong ProgramWriteStamp { get; private set; }
    /// <summary>Reads the mutation stamp of one texture unit, independently of metadata and sampler slots.</summary>
    internal ulong TextureWriteStamp(int unit) => textureWrites.GetValueOrDefault(unit);
    /// <summary>Reads the mutation stamp of one sampler unit.</summary>
    internal ulong SamplerWriteStamp(int unit) => samplerWrites.GetValueOrDefault(unit);
    /// <summary>Reads the mutation stamp of one image unit, including all its view selectors.</summary>
    internal ulong ImageWriteStamp(int unit) => imageWrites.GetValueOrDefault(unit);
    /// <summary>Reads both indexed and generic-selector effects of an indexed buffer write.</summary>
    internal (ulong Indexed, ulong Generic) BufferWriteStamp(BufferTarget target, int slot) =>
        (bufferWrites.GetValueOrDefault((target, slot)), bufferWrites.GetValueOrDefault((target, -1)));
    #endregion

    #region Private
    /// <summary>Interrupts writes only in explicitly discarded domains, including a reset during native callback reentry.</summary>
    private void InterruptBindingWrites(EPipelineState states)
    {
        if (states.HasFlag(EPipelineState.ActiveTextureUnit)) ActiveTextureWriteStamp++;
        if (states.HasFlag(EPipelineState.Program)) ProgramWriteStamp++;
        if (states.HasFlag(EPipelineState.TextureBindings))
            foreach (var key in new List<int>(textureWrites.Keys)) ChangeTextureWrite(key);
        if (states.HasFlag(EPipelineState.SamplerBindings))
            foreach (var key in new List<int>(samplerWrites.Keys)) ChangeSamplerWrite(key);
        if (states.HasFlag(EPipelineState.ImageBindings))
            foreach (var key in new List<int>(imageWrites.Keys)) ChangeImageWrite(key);
        if (states.HasFlag(EPipelineState.BufferBindings))
            foreach (var key in new List<(BufferTarget Target, int Slot)>(bufferWrites.Keys)) bufferWrites[key]++;
    }
    /// <summary>Records an actual affected texture unit while keeping other units independent.</summary>
    private void ChangeTextureWrite(int unit) => textureWrites[unit] = TextureWriteStamp(unit) + 1;
    /// <summary>Records an actual affected sampler unit.</summary>
    private void ChangeSamplerWrite(int unit) => samplerWrites[unit] = SamplerWriteStamp(unit) + 1;
    /// <summary>Records an actual affected image view.</summary>
    private void ChangeImageWrite(int unit) => imageWrites[unit] = ImageWriteStamp(unit) + 1;
    /// <summary>Records a generic selector and, where supplied, its indexed side effect.</summary>
    private void ChangeBufferWrite(BufferTarget target, int? slot = null)
    {
        bufferWrites[(target, -1)] = bufferWrites.GetValueOrDefault((target, -1)) + 1;
        if (slot is { } selected) bufferWrites[(target, selected)] = bufferWrites.GetValueOrDefault((target, selected)) + 1;
    }
    #endregion
}
