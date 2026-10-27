using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes an externally owned texture through the typed texture contract without acquiring its storage.</summary>
/// <remarks>Observed storage changes update canonical facts; deletion retires the original common registration.</remarks>
internal sealed class BorrowedTexture : GpuTexture
{
    #region Public API
    /// <summary>Captures target, dimensions, format and allocated mip levels without changing native state.</summary>
    internal BorrowedTexture(int externalTexture)
    {
        if (externalTexture <= 0)
            throw new ArgumentOutOfRangeException(nameof(externalTexture));

        SetCreationIdentity(externalTexture, CaptureCreationContext(GpuResourceKind.Texture));
        try
        {
            RHICommandContext.Current.AcquireBorrowedTexture(this);
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Texture has no allocated base-level image.", nameof(externalTexture));
        }
        catch
        {
            // Failed admission must release any provisional recipient without deleting the owner.
            Dispose();
            throw;
        }
    }

    /// <summary>Retains a selected external texture while its original registration remains live.</summary>
    /// <remarks>Unregistered tools conservatively reacquire facts; covered managed contexts reuse current snapshots.</remarks>
    internal static BorrowedTexture GetOrReplace(ref BorrowedTexture? selected, int externalTexture)
    {
        // Original association validity distinguishes deletion/name reuse from a surviving object's
        // metadata change. Never retarget an old wrapper to a replacement registration.
        if (selected is null || !((GpuResource)selected).IsValid || selected.TextureId != externalTexture)
        {
            selected?.Dispose();
            selected = null;
            selected = new BorrowedTexture(externalTexture);
        }
        else if (selected.ResourceManager is not { } manager || !manager.IsResourceMetadataAvailable(selected))
        {
            RHICommandContext.Current.AcquireBorrowedTexture(selected);
        }
        if (selected.Width <= 0 || selected.Height <= 0)
            throw new InvalidOperationException("Texture has no allocated base-level image.");
        return selected;
    }

    /// <summary>Labels the managed borrower without modifying the external object.</summary>
    public override void SetDebugName(string? name) { debugName = name; }

    /// <summary>Rejects handle transfer because the external owner retains disposal authority.</summary>
    public override nint Detach() => throw new NotSupportedException("Borrowed textures cannot transfer ownership.");
    #endregion

    #region Protected API
    /// <summary>Retiring this wrapper never deletes the externally owned texture.</summary>
    protected override bool OwnsResource => false;
    #endregion
}
