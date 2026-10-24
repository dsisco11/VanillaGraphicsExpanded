using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes represented texture parameters, image definitions and explicit mip generation.</summary>
internal sealed partial class StateCache
{
    /// <summary>Distinguishes image allocation, represented parameters and explicit mip generation.</summary>
    internal enum TextureMetadataEffect { Storage, Parameter, MipGeneration, RenderbufferAssociation }

    #region Internal API
    /// <summary>Resolves bound, named and explicit-unit selectors without registering untracked objects.</summary>
    internal static NativeTextureMetadata BeginNativeTextureMetadata(int selector, int nativeTarget, int nativeUnit,
        bool named, bool explicitUnit, TextureMetadataEffect effect, int argument)
    {
        // Only base/max level parameters affect represented texture metadata.
        if (effect == TextureMetadataEffect.Parameter && argument is not ((int)TextureParameterName.TextureBaseLevel or (int)TextureParameterName.TextureMaxLevel)) return default;
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return default;
        var target = (TextureTarget)nativeTarget;
        if (target >= TextureTarget.TextureCubeMapPositiveX && target <= TextureTarget.TextureCubeMapNegativeZ)
            target = TextureTarget.TextureCubeMap;
        // Proxy definitions have no texture-object effect. Invalid targets remain native
        // errors; do not reinterpret them as the texture currently bound to another target.
        if (target != 0 && !TryGetTextureBindingQuery(target, out _)) return default;
        int id = selector;
        if (!named)
        {
            if (target == 0) return default;
            int unit = explicitUnit ? unchecked(nativeUnit - (int)TextureUnit.Texture0) : Current.GetActiveTextureUnit();
            if ((uint)unit >= (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return default;
            id = Current.GetBoundTexture(target, unit);
        }
        var entry = manager.FindObservedResource(GpuResourceKind.Texture, id)
            as GpuResourceMetadataEntry<GpuTextureMetadata>;
        // Buffer textures only acquire storage through association commands. Image storage,
        // mip generation and represented texture parameters are invalid for this target.
        if (entry?.Metadata.Target == TextureTarget.TextureBuffer) return default;
        bool generation = effect == TextureMetadataEffect.MipGeneration;
        bool wasAvailable = entry?.IsUsable == true;
        var change = effect == TextureMetadataEffect.Parameter ? GpuMetadataChange.Metadata
            : generation ? GpuMetadataChange.None : GpuMetadataChange.Storage;
        if (entry is not null)
        {
            // Immutable storage already fixes every image descriptor. Mip regeneration can
            // only change its contents, so it must not retire view storage certificates.
            if (generation && wasAvailable && entry.Metadata.ImmutableStorage) return default;
            // A rejected named command may supply a different target. Prefer the established
            // object target so readback certifies actual state without speculative rebinding.
            entry.Reader ??= new GpuTextureMetadataReader(entry.Metadata.Target != 0 ? entry.Metadata.Target : target);
            entry.BeginRefresh(change);
        }
        // A matching established target and a live realized backing prove the simple
        // association command's admission conditions. Untracked backing names remain
        // queryable, but cannot renew a retired same-name dependency without that proof.
        bool renewAssociation = effect == TextureMetadataEffect.RenderbufferAssociation
            && target == (TextureTarget)All.TextureRenderbufferNv && entry?.Metadata.Target == target
            && (argument == 0 || manager.FindObservedResource(GpuResourceKind.Renderbuffer, argument)?.IsUsable == true);
        return new(entry, change, generation, wasAvailable, renewAssociation);
    }
    #endregion

    /// <summary>Retains one original registration across native image mutation and alias publication.</summary>
    internal readonly struct NativeTextureMetadata(GpuResourceMetadataEntry<GpuTextureMetadata>? entry,
        GpuMetadataChange change, bool generation, bool wasAvailable, bool renewAssociation)
    {
        #region Internal API
        /// <summary>Publishes real allocated images without interpreting texel upload contents.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is null || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            try
            {
                bool available = generation ? entry.Manager.RefreshGeneratedTextureMetadata(entry, wasAvailable)
                    : entry.Manager.RefreshMetadata(entry, change, renewTextureAssociation: nativeReturned && renewAssociation);
                if (!available)
                    throw new InvalidOperationException("Native texture metadata is unavailable after image mutation.");
            }
            catch when (!nativeReturned)
            {
                // Retain the original dispatch exception while unavailable facts remain withdrawn.
            }
        }
        #endregion
    }
}
