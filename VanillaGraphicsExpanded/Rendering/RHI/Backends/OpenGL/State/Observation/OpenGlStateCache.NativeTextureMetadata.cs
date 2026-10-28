using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes represented texture parameters, image definitions and explicit mip generation.</summary>
internal sealed partial class OpenGlStateCache
{
    /// <summary>Distinguishes image allocation, represented parameters and explicit mip generation.</summary>
    internal enum TextureMetadataEffect { Storage, Parameter, MipGeneration, RenderbufferAssociation }

    #region Internal API
    /// <summary>Resolves bound, named and explicit-unit selectors without registering untracked objects.</summary>
    internal static NativeTextureMetadata BeginNativeTextureMetadata(int selector, int nativeTarget, int nativeUnit,
        bool named, bool explicitUnit, TextureMetadataEffect effect, int argument, int imageLevel = -1, int? parameterValue = null)
    {
        // Only base/max level parameters affect represented texture metadata.
        if (effect == TextureMetadataEffect.Parameter && argument is not ((int)TextureParameterName.TextureBaseLevel or (int)TextureParameterName.TextureMaxLevel)) return default;
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null) return default;
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
        Current.RequireResourceOperationAvailable(GpuResourceKind.Texture, id);
        if (manager?.CanObserveNativeMetadata != true) return default;
        var entry = manager.FindObservedResource(GpuResourceKind.Texture, id)
            as GpuResourceMetadataEntry<GpuTextureMetadata>;
        // Buffer textures only acquire storage through association commands. Image storage,
        // mip generation and represented texture parameters are invalid for this target.
        if (entry?.Metadata.Target == TextureDimension.Buffer) return default;
        if (effect == TextureMetadataEffect.Parameter && entry?.Metadata.Target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray) return default;
        bool generation = effect == TextureMetadataEffect.MipGeneration;
        bool wasAvailable = entry?.IsUsable == true;
        if (entry is not null && imageLevel >= 0
            && (!CanQueryTextureImage(entry.Metadata, imageLevel) || target != 0 && OpenGLImageValues.Dimension(target) != entry.Metadata.Target)) imageLevel = -1;
        bool parameterTargetMatches = target == 0 || entry?.Metadata.Target == OpenGLImageValues.Dimension(target);
        var change = effect == TextureMetadataEffect.Parameter ? GpuMetadataChange.Metadata
            : generation ? GpuMetadataChange.None : GpuMetadataChange.Storage;
        if (entry is not null)
        {
            // Immutable storage already fixes every image descriptor. Mip regeneration can
            // only change its contents, so it must not retire view storage certificates.
            if (generation && wasAvailable && entry.Metadata.ImmutableStorage) return default;
            // A rejected named command may supply a different target. Prefer the established
            // object target so readback certifies actual state without speculative rebinding.
            if (entry.OperationInProgress || entry.CompletionInProgress) throw new System.InvalidOperationException("A resource metadata operation is already in progress.");
            entry.Reader ??= new GpuTextureMetadataReader(entry.Metadata.Target != 0 ? entry.Metadata.Target : target == 0 ? TextureDimension.Undefined : OpenGLImageValues.Dimension(target));
        }
        // A matching established target and a live realized backing prove the simple
        // association command's admission conditions. Untracked backing names remain
        // queryable, but cannot renew a retired same-name dependency without that proof.
        bool renewAssociation = effect == TextureMetadataEffect.RenderbufferAssociation
            && target == (TextureTarget)All.TextureRenderbufferNv && entry?.Metadata.Target == TextureDimension.RenderbufferTexture
            && (argument == 0 || manager.FindObservedResource(GpuResourceKind.Renderbuffer, argument)?.IsUsable == true);
        if (entry is not null)
        {
            entry.OperationInProgress = true;
            entry.BeginRefresh(change);
        }
        return new(entry, change, generation, wasAvailable, renewAssociation,
            effect == TextureMetadataEffect.Parameter ? (TextureParameterName)argument : 0, imageLevel,
            nativeTarget >= (int)TextureTarget.TextureCubeMapPositiveX && nativeTarget <= (int)TextureTarget.TextureCubeMapNegativeZ ? OpenGLImageValues.Face((TextureTarget)nativeTarget) : CubeFace.None, parameterValue, parameterTargetMatches);
    }
    #endregion

    #region Private
    /// <summary>Prevents recovery queries from replaying an invalid external mip index into the driver.</summary>
    private static bool CanQueryTextureImage(GpuTextureMetadata metadata, int level)
    {
        int count = 1;
        if (metadata.Target is not (TextureDimension.Rectangle or TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray))
        {
            var caps = GpuSupport.Graphics;
            int extent = metadata.Target == TextureDimension.Three ? caps.Max3DTextureSize
                : metadata.Target is TextureDimension.Cube or TextureDimension.CubeArray ? caps.MaxCubeMapTextureSize : caps.MaxTextureSize;
            while ((extent >>= 1) > 0) count++;
        }
        if (metadata.View is { } view) count = Math.Min(count, view.LevelCount);
        return level < count;
    }
    #endregion

    /// <summary>Retains one original registration across native image mutation and alias publication.</summary>
    internal readonly struct NativeTextureMetadata(GpuResourceMetadataEntry<GpuTextureMetadata>? entry,
        GpuMetadataChange change, bool generation, bool wasAvailable, bool renewAssociation, TextureParameterName parameter,
        int imageLevel, CubeFace face, int? parameterValue, bool parameterTargetMatches)
    {
        #region Internal API
        /// <summary>Publishes real allocated images without interpreting texel upload contents.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is not null) entry.OperationInProgress = false;
            if (entry is null || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            if (!nativeReturned) { entry.NotifyImageDependents(suppressErrors: true); return; }
            if (parameter != 0 && wasAvailable && parameterValue.HasValue)
            {
                if (!entry.Manager.CanObserveNativeMetadata)
                    throw new InvalidOperationException("Texture parameter completion requires its originating context.");
                int value = parameterValue.Value;
                var candidate = entry.Metadata;
                // Negative mip parameters and mismatched named targets are defined rejections.
                // Rectangle base level must remain zero. No new allocation can fail here.
                bool accepts = value >= 0 && parameterTargetMatches
                    && !(parameter == TextureParameterName.TextureBaseLevel && candidate.Target == TextureDimension.Rectangle && value != 0);
                if (accepts)
                    candidate = parameter == TextureParameterName.TextureBaseLevel
                        ? candidate with { BaseLevel = value } : candidate with { MaxLevel = value };
                entry.Manager.PublishMetadata(entry, candidate, GpuMetadataChange.Metadata);
                return;
            }
            bool available = parameter != 0 && wasAvailable
                    ? entry.Manager.RefreshTextureParameter(entry, OpenGLResourceValues.FromNative((GetTextureParameter)parameter))
                    : imageLevel >= 0 && wasAvailable
                    ? entry.Manager.RefreshTextureImages(entry, imageLevel, imageLevel, face, false, true)
                    : generation ? entry.Manager.RefreshGeneratedTextureMetadata(entry, wasAvailable)
                    : entry.Manager.RefreshMetadata(entry, change, renewTextureAssociation: nativeReturned && renewAssociation);
            if (!available)
                throw new InvalidOperationException("Native texture metadata is unavailable after image mutation.");
        }
        #endregion
    }
}
