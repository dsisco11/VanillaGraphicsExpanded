using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reuses backend-owned framebuffer realizations while leaving canonical metadata and resource ownership with existing services.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly Dictionary<OpenGLPassTargetKey, OpenGLPassTarget> passTargets = new();
    private readonly ConditionalWeakTable<RHIRenderPassDescription, OpenGLPassTargetKey> passTargetKeys = new();
    private readonly RHIResourceDisposalQueue targetDisposals = RHIResourceManagerSystem.CaptureDisposalQueue();

    #region Internal API


    /// <summary>Withdraws dependent realizations on confirmed deletion without ever adopting a reused numeric name.</summary>
    internal void InvalidatePassTargets(GpuResourceKind kind, int id)
    {
        if (kind is not (GpuResourceKind.Texture or GpuResourceKind.Renderbuffer or GpuResourceKind.Framebuffer)) return;
        List<OpenGLPassTargetKey>? retiredKeys = null;
        foreach (var pair in passTargets)
        {
            bool affected = kind == GpuResourceKind.Framebuffer && pair.Value.FboId == id;
            foreach (var image in pair.Key.Colors) affected |= image.Image.Resource.Kind == kind && image.Image.Name == id;
            if (pair.Key.DepthStencil is { } depth) affected |= depth.Resource.Kind == kind && depth.Name == id;
            if (!affected) continue;
            (retiredKeys ??= []).Add(pair.Key);
            targetDisposals.Enqueue(pair.Value);
        }
        if (retiredKeys is not null) foreach (var key in retiredKeys) passTargets.Remove(key);
    }
    #endregion

    #region Private
    /// <summary>Uses published associations on the fast path and commits a new target only after complete candidate realization.</summary>
    private GpuFramebuffer ResolvePassTarget(RHIRenderPassDescription description)
    {
        if (!description.Surface.IsEmpty) return description.Surface.RequireSurface();
        if (passTargetKeys.TryGetValue(description, out var existing) && existing.Matches(description)
            && passTargets.TryGetValue(existing, out var reused) && reused.IsReusable)
        {
            ValidatePassIntentions(description, existing);
            return reused;
        }
        var key = ResolvePassTargetKey(description);
        ValidatePassIntentions(description, key);
        if (passTargets.TryGetValue(key, out reused) && reused.IsReusable)
        {
            passTargetKeys.Remove(description); passTargetKeys.Add(description, key);
            return reused;
        }
        var images = ImmutableDictionary.CreateBuilder<FramebufferSlot, GpuFramebufferAttachment>();
        OpenGLPassTarget? candidate = null;
        try
        {
            foreach (var image in key.Colors)
                images.Add(FramebufferSlot.Color0 + image.Slot, image.Image.CreateImage());
            if (key.DepthStencil is { } depth)
            {
                var selected = depth.CreateImage();
                images.Add(selected.DepthStencilSlot, selected);
            }
            var route = new Legacy.DrawBuffersEnum[key.Outputs.Length];
            for (int i = 0; i < route.Length; i++) route[i] = key.Outputs[i] < 0
                ? Legacy.DrawBuffersEnum.None : Legacy.DrawBuffersEnum.ColorAttachment0 + key.Outputs[i];
            candidate = new(this, new GpuFramebufferInitialization { Attachments = images.ToImmutable() }, route);
            // Replacement becomes visible only now. Failed setup leaves any old usable realization intact.
            RetireObsoletePassTargets();
            if (passTargets.Remove(key, out var obsolete)) targetDisposals.Enqueue(obsolete);
            passTargetKeys.Remove(description); passTargetKeys.Add(description, key);
            // Publish the owned target last: weak-key bookkeeping may fail, but must never
            // leave a dictionary entry pointing at the candidate queued by failure cleanup.
            passTargets.Add(key, candidate);
            return candidate;
        }
        catch
        {
            if (candidate is not null) targetDisposals.Enqueue(candidate);
            else foreach (var image in images.Values) image.Dispose();
            throw;
        }
    }




    /// <summary>Queues all backend-owned targets on context teardown while preserving their original retirement identities.</summary>
    private void RetirePassTargets()
    {
        foreach (var target in passTargets.Values)
        {
            target.WithdrawConfiguration();
            targetDisposals.Enqueue(target);
        }
        passTargets.Clear(); passTargetKeys.Clear();
    }

    /// <summary>Releases superseded allocations on cache misses without performing registry-wide work on every pass.</summary>
    private void RetireObsoletePassTargets()
    {
        List<OpenGLPassTargetKey>? obsolete = null;
        foreach (var pair in passTargets)
            if (!pair.Value.IsReusable || !pair.Key.IsCurrent) (obsolete ??= []).Add(pair.Key);
        if (obsolete is null) return;
        foreach (var key in obsolete)
            if (passTargets.Remove(key, out var target)) targetDisposals.Enqueue(target);
    }
    #endregion
}
