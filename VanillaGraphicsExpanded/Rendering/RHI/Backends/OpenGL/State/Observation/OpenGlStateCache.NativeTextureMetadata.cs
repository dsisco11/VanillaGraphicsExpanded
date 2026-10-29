using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains represented texture parameters, generated images and backing associations.</summary>
internal sealed partial class OpenGlStateCache
{
    /// <summary>Distinguishes represented parameter, generation and opaque storage effects.</summary>
    internal enum TextureMetadataEffect { Storage, Parameter, MipGeneration, RenderbufferAssociation }

    #region Internal API
    /// <summary>Uses established request facts without ordinary parameter or image readback.</summary>
    internal static void ObserveTextureMetadata(int selector, int nativeTarget, int nativeUnit,
        bool named, bool explicitUnit, TextureMetadataEffect effect, int argument,
        int? parameterValue = null, bool extNamed = false)
    {
        // Eligible EXT named calls realize the object before their parameter-specific checks.
        if (extNamed && OpenGLObservationContext.TryGet(out _, out _, false))
            _ = ResolveObservedTexture(selector, nativeTarget, nativeUnit, named, explicitUnit, true);
        if (effect == TextureMetadataEffect.Parameter
            && argument is not ((int)TextureParameterName.TextureBaseLevel or (int)TextureParameterName.TextureMaxLevel)) return;
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var entry = ResolveObservedTexture(selector, nativeTarget, nativeUnit, named, explicitUnit, extNamed);
        if (entry is null) return;
        var data = entry.Metadata;
        if (data.Target == TextureDimension.Buffer) return;
        if (effect == TextureMetadataEffect.Parameter)
        {
            if (data.Target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray) return;
            if (parameterValue is < 0 || argument == (int)TextureParameterName.TextureBaseLevel
                && data.Target == TextureDimension.Rectangle && parameterValue != 0) return;
            if (!entry.IsUsable || !parameterValue.HasValue) { WithdrawImageDefinition(cache, entry, GpuMetadataChange.Metadata); return; }
            var candidate = argument == (int)TextureParameterName.TextureBaseLevel
                ? data with { BaseLevel = parameterValue.Value } : data with { MaxLevel = parameterValue.Value };
            if (candidate == data) return;
            cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, (int)entry.Id);
            if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Metadata))
                entry.Manager.PublishMetadata(entry, candidate, GpuMetadataChange.Metadata);
        }
        else if (effect == TextureMetadataEffect.MipGeneration)
        {
            if (data.Target is TextureDimension.Rectangle or TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray
                || data.ImmutableStorage) return;
            if (!entry.IsUsable) { WithdrawImageDefinition(cache, entry); return; }
            // An incomplete cube is rejected, and an undefined base or unchanged mip
            // descriptors have no represented effect. Resolve those cases before
            // disturbing an operation that owns the same original texture identity.
            var candidate = GpuResourceManager.BuildGeneratedTextureMetadata(data);
            if (!candidate.HasValue) return;
            cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, (int)entry.Id);
            if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage))
                entry.Manager.PublishMetadata(entry, candidate.Value, GpuMetadataChange.Storage);
        }
        else if (effect == TextureMetadataEffect.RenderbufferAssociation)
        {
            if (data.Target != TextureDimension.RenderbufferTexture) return;
            var backing = entry.Manager.FindObservedResource(GpuResourceKind.Renderbuffer, argument);
            if (argument != 0 && backing?.IsRealized != true) return;
            // A deleted backing withdraws this association alone. A later accepted
            // association replaces it without rediscovering the texture descriptor.
            if (!entry.IsUsable && entry.PendingChange != GpuMetadataChange.Metadata
                || argument != 0 && backing?.IsUsable != true) { WithdrawImageDefinition(cache, entry); return; }
            cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, (int)entry.Id);
            if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage))
                entry.Manager.PublishMetadata(entry, data with { Renderbuffer = argument }, GpuMetadataChange.Storage, true);
        }
        else WithdrawImageDefinition(cache, entry);
    }
    #endregion
}
