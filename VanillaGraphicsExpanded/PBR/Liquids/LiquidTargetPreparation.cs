using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Certifies the published target facts used by a liquid pipeline description.</summary>
internal sealed class LiquidTargetPreparation
{
    private readonly RHIRenderPassDescription pass;
    private bool dirty;

    #region Public API
    /// <summary>Captures facts only after the RHI has successfully described the pass.</summary>
    internal LiquidTargetPreparation(RHIRenderPassDescription pass)
    {
        this.pass = pass;
        if (!pass.Surface.IsEmpty) pass.Surface.RequireSurface().ObservePreparation(this, Withdraw);
        foreach (var color in pass.Colors) Observe(color.Image);
        Observe(pass.DepthStencil);
    }
    /// <summary>Checks current availability and authored routing without describing or realizing native targets again.</summary>
    internal bool Matches(RHIRenderPassDescription candidate)
    {
        if (dirty) return false;
        if (ReferenceEquals(candidate, pass)) return true;
        if (candidate.Surface != pass.Surface || candidate.DepthStencil != pass.DepthStencil
            || candidate.Actions != pass.Actions || candidate.Colors.Length != pass.Colors.Length) return false;
        for (int i = 0; i < pass.Colors.Length; i++)
            if (candidate.Colors[i] != pass.Colors[i]) return false;
        return true;
        // Area and label do not affect fixed pipeline compatibility; execution retains their intentions.
    }
    #endregion

    #region Private
    /// <summary>Observes actual authored dependencies without retaining another canonical metadata snapshot.</summary>
    private void Observe(RHIAttachment? attachment)
    {
        if (attachment is not { } image) return;
        GpuResource? owner = image.Texture.Resource;
        if (owner is null && !image.Renderbuffer.IsEmpty) owner = image.Renderbuffer.RequireResource();
        owner?.ObservePreparation(this, Withdraw);
    }
    /// <summary>Withdraws pipeline-target compatibility until its owner explicitly prepares a replacement.</summary>
    private void Withdraw() { dirty = true; }
    #endregion
}