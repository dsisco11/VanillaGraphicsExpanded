using System;
using System.Linq;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Builds final pass intentions from fixture-owned images and provides explicit native engine setup.</summary>
internal static class FramebufferTestSetup
{
    #region Internal API
    /// <summary>Selects an engine fixture destination and viewport without changing production rendering APIs.</summary>
    internal static void BindForEngineRendering(GpuFramebuffer target)
    {
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, target.FboId);
        GL.Viewport(0, 0, target.Width, target.Height);
        OpenGlStateCache.Current.Invalidate(EPipelineState.Viewport);
    }
    /// <summary>Declares known attachment selections; external targets retain their original surface identity.</summary>
    internal static RHIRenderPassDescription DescribeAttachments(GpuFramebuffer target,
        IEnumerable<RHIColorAttachment>? colors = null, RHIDepthStencilActions? actions = null, RenderArea? area = null)
    {
        var outputs = colors?.ToArray() ?? [new(AttachmentIndex: 0)];
        if (!target.OwnsNativeFramebuffer) return new(target.SurfaceReference, outputs, actions: actions, area: area);
        var selected = outputs.Select(output => !output.DiscardOutput && output.AttachmentIndex is { } index
            ? output with { Image = Select(target.GetAttachment(FramebufferAttachment.ColorAttachment0 + index)
                ?? throw new InvalidOperationException("Fixture color image is unavailable.")), AttachmentIndex = null }
            : output).ToArray();
        var depth = target.GetAttachment(FramebufferAttachment.DepthAttachment);
        var stencil = target.GetAttachment(FramebufferAttachment.StencilAttachment);
        return new(selected, depthStencil: depth is not null ? Select(depth) : stencil is not null ? Select(stencil) : null,
            actions: actions, area: area);
    }
    /// <summary>Retains the exact known backing recipient and relative layer rather than reborrowing a managed texture by ID.</summary>
    internal static RHIAttachment Select(GpuFramebufferAttachment image)
    {
        if (image.Resource is GpuResource<GpuTextureMetadata> texture)
            return RHIAttachment.FromTexture(new GpuTextureRef(texture), new(image.MipLevel,
                image.Layer ?? (image.CubeFace is { } face ? (int)face - (int)TextureTarget.TextureCubeMapPositiveX : 0)), image.MetadataImageSelection.Layered);
        if (image.Resource is GpuRenderbuffer renderbuffer) return RHIAttachment.FromRenderbuffer(renderbuffer);
        throw new InvalidOperationException("Use an explicitly imported surface for an external framebuffer image.");
    }
    /// <summary>Clears fixture-owned images through final pass load actions, restoring engine state around the operation.</summary>
    internal static void ClearAttachments(GpuFramebuffer target, float r = 0, float g = 0, float b = 0, float a = 0)
    {
        var colors = Enumerable.Range(0, target.ColorAttachmentCount).Select(index => new RHIColorAttachment(AttachmentIndex: index, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(r,g,b,a)));
        var pass = DescribeAttachments(target, colors);
        var context = RHICommandContext.Current;
        using var boundary = context.EnterBoundary(new("Fixture.Clear", true, operations: BoundaryOperations.RenderPasses));
        boundary.Run(() => { context.BeginRenderPass(pass); context.EndRenderPass(); });
    }
    /// <summary>Builds explicit copy inputs from fixture-owned image metadata without retaining framebuffer ownership.</summary>
    internal static RHIImageBlit DescribeCopy(GpuFramebuffer source, GpuFramebuffer destination, ImageAspects aspect=ImageAspects.Color)
    {
        var slot=aspect==ImageAspects.Color?FramebufferAttachment.ColorAttachment0:FramebufferAttachment.DepthAttachment;
        var from=source.GetAttachment(slot)??throw new InvalidOperationException("The selected source image is unavailable.");
        var to=destination.GetAttachment(slot)??throw new InvalidOperationException("The selected destination image is unavailable.");
        return new(Select(from),new(0,0,from.Width,from.Height),Select(to),new(0,0,to.Width,to.Height),aspect);
    }

    /// <summary>Authors clear loads for every supplied color image and executes them under final restoration authority.</summary>
    internal static void ClearPass(RHIRenderPassDescription description, float value)
    {
        var pass = new RHIRenderPassDescription(description.Colors.Select(color => color with
            { Load = AttachmentLoad.Clear, Clear = ColorClearValue.Float(value,value,value,value) }),
            description.DepthStencil, description.Actions, description.Area);
        var context = RHICommandContext.Current;
        using var boundary = context.EnterBoundary(new("Fixture.ClearPass", true, operations: BoundaryOperations.RenderPasses));
        boundary.Run(() => { context.BeginRenderPass(pass); context.EndRenderPass(); });
    }
    #endregion
}
