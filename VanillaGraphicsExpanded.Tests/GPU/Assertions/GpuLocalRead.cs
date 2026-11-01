using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Restricts existing scalar probes to their nominated texel instead of downloading the enclosing image.</summary>
internal static class GpuLocalRead
{
    #region Internal API
    /// <summary>Returns one explicitly nominated texel and layer, with its represented channel count and original float encoding.</summary>
    internal static float[] Tuple(GpuTexture image,int x,int y,int layer=0)
    {
        using var request=RHICommandContext.Current.EnqueueReadback(new(image,
            new(new(0,layer),x,y,0,1,1,1),RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.Float)));
        if(request.Wait()!=CompletionStatus.Ready)throw new InvalidOperationException("Local tuple readback failed.",request.Error);
        using var mapping=request.MapReadyReadback();
        return MemoryMarshal.Cast<byte,float>(mapping.Bytes).ToArray();
    }

    /// <summary>Reads exactly one normalized RGBA8 texel, retaining byte-exact local metadata expectations and explicit layer identity.</summary>
    internal static byte[] Rgba8(GpuTexture image,int x,int y,int layer=0)
    {
        if(image.InternalFormat!=TextureFormat.Rgba8)throw new ArgumentException("The local byte tuple requires RGBA8 storage.",nameof(image));
        // Retain the original native-byte interpretation; there is no float conversion or image copy.
        using var request=RHICommandContext.Current.EnqueueReadback(new(image,
            new(new(0,layer),x,y,0,1,1,1),RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.UnsignedByte)));
        if(request.Wait()!=CompletionStatus.Ready)throw new InvalidOperationException("Local byte tuple readback failed.",request.Error);
        using var mapping=request.MapReadyReadback();
        return mapping.Bytes.ToArray();
    }

    /// <summary>Reads exactly one unsigned integer cell while retaining its selected layer or depth slice.</summary>
    internal static uint UInt32(GpuTexture image,int x,int y,int layer)
    {
        using var request=RHICommandContext.Current.EnqueueReadback(new(image,
            new(new(0,layer),x,y,0,1,1,1),RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.UnsignedInt)));
        if(request.Wait()!=CompletionStatus.Ready)throw new InvalidOperationException("Local integer readback failed.",request.Error);
        using var mapping=request.MapReadyReadback();
        return MemoryMarshal.Cast<byte,uint>(mapping.Bytes)[0];
    }
    /// <summary>Converts the original tightly packed scalar index into one bounded pixel read, retaining channel interpretation.</summary>
    internal static float Scalar(GpuTexture image, int index)
    {
        var encoding = RHICommandContext.Current.GetTextureEncoding(image, PixelComponentType.Float);
        int channels = encoding.Channels switch
        {
            PixelChannelLayout.Red or PixelChannelLayout.RedInteger or PixelChannelLayout.Depth or PixelChannelLayout.DepthStencil => 1,
            PixelChannelLayout.Rg or PixelChannelLayout.RgInteger => 2,
            PixelChannelLayout.Rgb or PixelChannelLayout.RgbInteger => 3,
            PixelChannelLayout.Rgba or PixelChannelLayout.RgbaInteger => 4,
            _ => throw new NotSupportedException("The probe requires a scalar color or depth interpretation.")
        };
        if (index < 0 || (long)index >= (long)image.Width * image.Height * channels)
            throw new ArgumentOutOfRangeException(nameof(index));
        int pixel = index / channels;
        float[] values = image.ReadPixelsRegion(pixel % image.Width, pixel / image.Width, 1, 1);
        Assert.Equal(channels, values.Length);
        return values[index % channels];
    }
    #endregion
}
