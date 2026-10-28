using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains the single executed image-selector store used by the backend and boundary restoration.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Recognizes every target represented by this cache, including explicit optional renderbuffer textures.</summary>
    internal static bool RepresentsTextureBinding(TextureTarget target) => TryGetTextureBindingQuery(target, out _);
    /// <summary>Exposes known active-unit facts without performing native discovery.</summary>
    internal int? ActiveTextureKnowledge { get => activeTextureUnit; set => activeTextureUnit = value; }
    /// <summary>Exposes known renderbuffer facts without another selector cache.</summary>
    internal int? RenderbufferKnowledge { get => currentRenderbuffer; set => currentRenderbuffer = value; }
    /// <summary>Returns the existing unit's target dictionary for backend reconciliation.</summary>
    internal Dictionary<TextureTarget, int> ImageBindings(int unit)
    {
        EnsureTextureUnitCapacity(unit);
        return textureBindingsByUnit![unit] ??= new();
    }
    /// <summary>Returns already executed sampler-slot knowledge.</summary>
    internal int? SamplerKnowledge(int unit)
    {
        EnsureTextureUnitCapacity(unit);
        return samplerBindingByUnit![unit];
    }
    /// <summary>Publishes or withdraws one sampler selector after backend execution.</summary>
    internal void SetSamplerKnowledge(int unit, int? value)
    {
        EnsureTextureUnitCapacity(unit);
        samplerBindingByUnit![unit] = value;
    }
    /// <summary>Withdraws only the potentially changed target/slot after an external bind.</summary>
    internal void ForgetImageBinding(TextureTarget target, int? unit)
    {
        if (unit is int known) { ImageBindings(known).Remove(target); return; }
        if (textureBindingsByUnit is not null)
            foreach (var bindings in textureBindingsByUnit) bindings?.Remove(target);
    }
    /// <summary>Records an actual texture native call only in development builds.</summary>
    [Conditional("DEBUG")]
    internal void RecordTextureBind()
    {
#if DEBUG
        TextureBindCount++;
#endif
    }
    /// <summary>Records an actual sampler native call only in development builds.</summary>
    [Conditional("DEBUG")]
    internal void RecordSamplerBind()
    {
#if DEBUG
        SamplerBindCount++;
#endif
    }
    #endregion
}
