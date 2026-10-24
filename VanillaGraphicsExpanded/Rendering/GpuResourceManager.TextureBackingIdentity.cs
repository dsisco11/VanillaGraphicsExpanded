using System;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves original backing registrations when native associations retain deleted numeric names.</summary>
internal sealed partial class GpuResourceManager
{
    private readonly ConditionalWeakTable<GpuResourceRegistration, GpuResourceRegistration> textureBackingIdentities = new();

    #region Private
    /// <summary>Rejects implicit adoption of a replacement allocation under a retained association's old name.</summary>
    private void RequireOriginalTextureBacking(GpuResourceRegistration texture, GpuResourceKind kind, nint id,
        bool renewAssociation)
    {
        if (!textureBackingIdentities.TryGetValue(texture, out var original)
            || original.Kind != kind || original.Id != id || renewAssociation) return;
        // Native texture association queries can retain a deleted backing name. Reusing
        // that number does not reconnect the texture to the new allocation; only a
        // separately certified association command may establish that relationship.
        if (!IsRegistered(original) || original.State != GpuResourceRegistrationState.Live)
            throw new InvalidOperationException("The texture's original backing allocation has retired.");
    }

    /// <summary>Records one backing identity for the original texture entry without retaining wrappers.</summary>
    private void RecordTextureBacking(GpuResourceRegistration texture, GpuResourceRegistration backing)
    {
        if (textureBackingIdentities.TryGetValue(texture, out var previous) && ReferenceEquals(previous, backing)) return;
        textureBackingIdentities.Remove(texture);
        textureBackingIdentities.Add(texture, backing);
    }

    /// <summary>Checks the captured allocation independently of any resource subsequently using its numeric name.</summary>
    private bool IsOriginalTextureBackingAvailable(GpuResourceRegistration texture)
    {
        return textureBackingIdentities.TryGetValue(texture, out var backing)
            && IsRegistered(backing) && backing.IsUsable;
    }
    #endregion
}
