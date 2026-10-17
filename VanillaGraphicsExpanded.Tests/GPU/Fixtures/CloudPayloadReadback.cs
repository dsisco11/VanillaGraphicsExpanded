using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Reads and authors compact payload layers without adding diagnostics to production resources.</summary>
internal static class CloudPayloadReadback
{
    #region Public API
    /// <summary>Reads all three local framebuffer layers while preserving the inherited read binding.</summary>
    internal static float[][] Read(CloudVolumeSurface surface)
    {
        int previous=GL.GetInteger(GetPName.ReadFramebufferBinding);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,surface.Target.FboId);
            var result=new float[3][];
            for(int layer=0;layer<3;layer++)
            {
                GL.ReadBuffer((ReadBufferMode)((int)ReadBufferMode.ColorAttachment0+layer));
                result[layer]=new float[surface.Target.Width*surface.Target.Height*4];
                GL.ReadPixels(0,0,surface.Target.Width,surface.Target.Height,PixelFormat.Rgba,PixelType.Float,result[layer]);
            }
            return result;
        }
        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,previous);StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Checks driver attachment state, including view-local layer selection rather than owner-relative offsets.</summary>
    internal static void AssertAttachmentLayout(CloudVolumeSurface surface)
    {
        int previous=GL.GetInteger(GetPName.DrawFramebufferBinding);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,surface.Target.FboId);
            for(int layer=0;layer<3;layer++)
            {
                var attachment=(FramebufferAttachment)((int)FramebufferAttachment.ColorAttachment0+layer);
                GL.GetFramebufferAttachmentParameter(FramebufferTarget.DrawFramebuffer,attachment,FramebufferParameterName.FramebufferAttachmentObjectName,out int texture);
                GL.GetFramebufferAttachmentParameter(FramebufferTarget.DrawFramebuffer,attachment,FramebufferParameterName.FramebufferAttachmentTextureLayer,out int selectedLayer);
                Assert.Equal(surface.View.TextureId,texture);Assert.Equal(layer,selectedLayer);
            }
#if DEBUG
            GL.GetObjectLabel(ObjectLabelIdentifier.Texture,surface.View.TextureId,512,out _,out string label);
            Assert.False(string.IsNullOrWhiteSpace(label));
#endif
        }
        finally { GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,previous);StateCache.Current.InvalidateAll(); }
    }    /// <summary>Authors one local payload layer through its bounded array view.</summary>
    internal static void Upload(CloudVolumeSurface surface,int layer,float[] values)
    {
        using var binding=StateCache.Current.BindTextureScope(TextureTarget.Texture2DArray,0,surface.View.TextureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray,0,0,0,layer,surface.Target.Width,surface.Target.Height,1,
            PixelFormat.Rgba,PixelType.Float,values);
    }
    #endregion
}

