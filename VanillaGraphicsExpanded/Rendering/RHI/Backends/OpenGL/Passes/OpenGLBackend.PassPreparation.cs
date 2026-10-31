using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains pass relationships independently of requested per-pass load/store execution.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<RHIRenderPassDescription, PreparedPass> preparedPasses = new();
#if DEBUG
    internal long PassPreparations { get; private set; }
#endif

    #region Private
    /// <summary>Reuses a complete target generation; a failed refresh never falls back to the old generation.</summary>
    private PreparedPass PreparePass(RHIRenderPassDescription description, bool realize = true)
    {
        var result = preparedPasses.GetValue(description, static supplied => new(supplied));
        if (result.Dirty)
        {
            result.Dirty = false;
            result.Failure = null;
            result.Description = null;
            result.Targets = null;
            result.Signature = null;
            result.Key = null;
#if DEBUG
            PassPreparations++;
#endif
            try
            {
                if (description.Surface.IsEmpty)
                {
                    result.Key = ResolvePassTargetKey(description);
                    ValidatePassIntentions(description, result.Key);
                    result.Signature = PreparePassSignature(description, result.Key);
                }
                else
                {
                    var target = description.Surface.RequireSurface();
                    result.Description = LowerPass(description, target);
                    result.Targets = new(result.Description);
                    result.Signature = result.Targets.Signature;
                    result.ObserveTarget(target);
                }
            }
            catch (Exception error) { result.Failure = error; }
        }
        if (result.Failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(result.Failure).Throw();
        if (realize && result.Description is null)
        {
            try
            {
                var target = ResolvePassTarget(description, result.Key!);
                var lowered = LowerPass(description, target);
                var facts = new RenderPassTargets(lowered);
                result.ObserveTarget(target);
                result.Description = lowered;
                result.Targets = facts;
            }
            catch (Exception error) { result.Failure = error; throw; }
        }
        return result;
    }

    /// <summary>Observes only the selected owners and retains no native ownership or duplicate resource registry.</summary>
    private sealed class PreparedPass
    {
        internal bool Dirty = true;
        internal Exception? Failure;
        internal RenderPassDesc? Description;
        internal RenderPassTargets? Targets;
        internal OpenGLPassTargetKey? Key;
        internal Pipeline.Descriptions.RenderTargetSignature? Signature;

        /// <summary>Subscribes authored dependencies before the first preparation, including unavailable selections.</summary>
        internal PreparedPass(RHIRenderPassDescription description)
        {
            foreach (var color in description.Colors) if (color.Image is { } image) Observe(image);
            if (description.DepthStencil is { } depth) Observe(depth);
            if (!description.Surface.IsEmpty) ObserveTarget(description.Surface.RequireSurface());
        }
        /// <summary>Watches attachment changes while excluding executed routing restoration from structural refresh.</summary>
        internal void ObserveTarget(GpuFramebuffer target)
        {
            ulong revision = target.AttachmentRevision;
            nint identity = target.ResourceId;
            target.ObservePreparation(this, () =>
            {
                if (target.IsDisposed || target.ResourceId != identity || target.AttachmentRevision != revision) Dirty = true;
            });
        }
        /// <summary>Subscribes exactly one texture or renderbuffer owner selected by an attachment.</summary>
        private void Observe(RHIAttachment image)
        {
            var owner = image.Texture.Resource ?? (GpuResource?)(image.Renderbuffer.IsEmpty ? null : image.Renderbuffer.RequireResource());
            owner?.ObservePreparation(this, Invalidate);
        }
        /// <summary>Withdraws relational results without native work or eager recovery.</summary>
        private void Invalidate() { Dirty = true; }
    }
    #endregion
}
