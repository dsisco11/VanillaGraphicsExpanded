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

    private readonly RHIResourceDisposalQueue targetDisposals = RHIResourceManagerSystem.CaptureDisposalQueue();

    #region Private
    /// <summary>Uses published associations on the fast path and commits a new target only after complete candidate realization.</summary>
    private GpuFramebuffer ResolvePassTarget(RHIRenderPassDescription description, OpenGLPassTargetKey key)
    {
        if (!description.Surface.IsEmpty) return description.Surface.RequireSurface();
        if (passTargets.TryGetValue(key, out var reused) && reused.IsReusable)
        {

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

            if (passTargets.Remove(key, out var obsolete)) targetDisposals.Enqueue(obsolete);

            // Publish the owned target last: weak-key bookkeeping may fail, but must never
            // leave a dictionary entry pointing at the candidate queued by failure cleanup.
            passTargets.Add(key, candidate);
            ObserveTargetDependencies(key, candidate);
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
        passTargets.Clear();
    }

    /// <summary>Retires only an affected original target at its delivered change boundary.</summary>
    private void ObserveTargetDependencies(OpenGLPassTargetKey key, OpenGLPassTarget target)
    {
        void Withdraw()
        {
            if (passTargets.TryGetValue(key, out var current) && ReferenceEquals(current, target))
            {
                passTargets.Remove(key);
                targetDisposals.Enqueue(target);
            }
        }
        target.ObservePreparation(key, Withdraw);
        foreach (var color in key.Colors) color.Image.Resource.ObservePreparation(key, Withdraw);
        if (key.DepthStencil is { } depth) depth.Resource.ObservePreparation(key, Withdraw);
    }
    #endregion
}
