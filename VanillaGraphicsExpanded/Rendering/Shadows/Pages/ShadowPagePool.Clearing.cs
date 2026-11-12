using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Initializes GPU-selected replacement tiles without selection readback or attachment clears.</summary>
internal sealed partial class ShadowPagePool
{
    #region Public API
    /// <summary>Selects an exact pending assignment from the frozen batch without reading its physical location.</summary>
    internal bool SelectReplacement(ShadowPageRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || !writesFrozen || OperationsRemaining == 0) return false;
        return Execute(request with { Operation = ShadowPageOperation.SelectReplacement });
    }

    /// <summary>Queues an exact-token clear for the latest successful allocation or frozen replacement selection.</summary>
    internal bool ClearReplacement()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || OperationsRemaining < 2) return false;
        if (!clearShader.EnsureReady() || clearShader.Shader != clearExecutable)
        { Withdraw(); throw new InvalidOperationException("Clear shader replacement withdraws page storage."); }
        operations += 2;
        var bindings = (IShadowTileClearBindings)clearShader;
        bindings.Policy = commands; bindings.Entries = Entries; bindings.Results = Results;
        bindings.Bank0 = new(DepthBank(0), ImageAccess.WriteOnly, Format: TextureFormat.R32ui);
        bindings.Bank1 = new(DepthBank(Math.Min(1, ResidentBanks - 1)), ImageAccess.WriteOnly, Format: TextureFormat.R32ui);
        bindings.Bank2 = new(DepthBank(Math.Min(2, ResidentBanks - 1)), ImageAccess.WriteOnly, Format: TextureFormat.R32ui);
        bindings.Bank3 = new(DepthBank(Math.Min(3, ResidentBanks - 1)), ImageAccess.WriteOnly, Format: TextureFormat.R32ui);
        var rhi = RHICommandContext.Current;
        using var boundary = rhi.EnterBoundary(new("Shadows.Clear", true, compute: [clearShader.Pipeline]));
        boundary.Run(() =>
        {
            commands.UploadSubData<uint>([(uint)PageSize, 0u], 40);
            clearShader.Dispatch((PageSize + 7) / 8, (PageSize + 7) / 8);
            rhi.Barrier(new(ResourceAccess.Image | ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate));
            commands.UploadSubData<uint>([1u], 44);
            clearShader.Dispatch(1);
            rhi.Barrier(new(ResourceAccess.Image | ResourceAccess.ShaderStorage | ResourceAccess.TextureFetch));
        });
        return true;
    }
    #endregion
}
