using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Authors and reads compact payloads through owned texture, framebuffer and staging abstractions.</summary>
internal static class CloudPayloadReadback
{
    #region Public API
    /// <summary>Stages all three layers into one buffer and maps once, preserving inherited framebuffer bindings.</summary>
    internal static float[][] Read(CloudVolumeSurface surface)
    {
        int count=checked(surface.Target.Width*surface.Target.Height*4);
        using var framebuffer=DynamicGpuFramebuffer.CreateEmpty("Tests.Cloud.Readback");
        using var draw=StateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer);
        using var read=StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer);
        using var staging=GpuPixelPackBuffer.Create(debugName:"Tests.Cloud.PayloadPixels");
        staging.AllocateOrphan(checked(count*3*sizeof(float)));
        // Every source is routed to color zero; changing the borrowed image needs no raw read-buffer state.
        for(int layer=0;layer<3;layer++)
        {
            using var image=GpuFramebufferAttachment.FromTexture(surface.View,layer:layer);
            framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0,image);framebuffer.Bind();
            staging.ReadPixels(0,0,surface.Target.Width,surface.Target.Height,PixelFormat.Rgba,PixelType.Float,layer*count*sizeof(float));
            framebuffer.RemoveAttachment(FramebufferAttachment.ColorAttachment0);
        }
        using var mapped=staging.MapRange<float>(0,count*3,MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        var result=new float[3][];
        for(int layer=0;layer<3;layer++)result[layer]=mapped.Span.Slice(layer*count,count).ToArray();
        return result;
    }

    /// <summary>Checks the immutable attachment declarations owned by the production surface.</summary>
    internal static void AssertAttachmentLayout(CloudVolumeSurface surface)
    {
        for(int layer=0;layer<3;layer++)
        {
            var image=surface.Target.GetAttachment(FramebufferAttachment.ColorAttachment0+layer);
            Assert.NotNull(image);Assert.Same(surface.View,image.Resource);
            Assert.Equal(surface.View.TextureId,image.TextureId);Assert.Equal(layer,image.Layer);
        }
    }

    /// <summary>Uploads the selected local layer through its managed source allocation.</summary>
    internal static void Upload(CloudVolumeSurface surface,int layer,float[] values)
    {
        Assert.InRange(layer,0,surface.View.LayerCount-1);
        var source=Assert.IsAssignableFrom<GpuTexture>(surface.View.Source);
        source.UploadDataImmediate(values,0,0,surface.View.BaseLayer+layer,surface.Target.Width,surface.Target.Height,1);
    }
    #endregion
}
