using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Authors test history inputs from completed GPU images without downloading and re-uploading their contents.</summary>
internal sealed class GpuImageTransform(GpuObservationPrograms programs)
{

    #region Internal API
    /// <summary>Returns an independently owned RGBA32F projection of every original texel using a, coordinate, index and parameters.</summary>
    internal Texture3D Apply(GpuTexture image,GpuImageProjection expression,Vector4 parameters=default,GpuTexture? second=null)
    {
        programs.RequireContext();
        if (!expression.IsDeclared)
            throw new ArgumentOutOfRangeException(nameof(expression), "Select a declared independent projection.");
        using var source=new GpuImageSnapshot(image);
        using var reference=second is null?null:new GpuImageSnapshot(second);
        if(reference is not null && (reference.Kind!=source.Kind || reference.Texture.Width!=source.Texture.Width ||
            reference.Texture.Height!=source.Texture.Height || reference.Texture.Depth!=source.Texture.Depth))
            throw new ArgumentException("History projection controls must have corresponding floating domains.",nameof(second));
        var program=programs.Get("projection-" + expression.Domain, source.Kind);
        using var state=new GpuAssertionState(program.SamplerUnits, program.ImageUnits);
        int width=source.Texture.Width,height=source.Texture.Height,depth=source.Texture.Depth;
        var output=RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        {
            Dimension=TextureDimension.TwoArray,Format=TextureFormat.Rgba32f,Width=width,Height=height,Depth=depth,
            MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest,Label="Tests.History.Projection"
        }).RequireResource();
        try
        {
            byte[] bytes=new byte[160];
            GpuAssertionInputs.Integer(bytes,0,(uint)width);GpuAssertionInputs.Integer(bytes,4,(uint)height);
            GpuAssertionInputs.Integer(bytes,8,(uint)depth);GpuAssertionInputs.Integer(bytes,92,(uint)expression.Choice);
            GpuAssertionInputs.Vector(bytes,64,parameters);
            GpuAssertionInputs.Integer(bytes,20,expression.Inputs);
            int count=checked(width*height*depth),groups=(count+63)/64,x=Math.Min(groups,65535);
            program.Execute(bytes,[("sourceImage", source.Texture),("referenceImage",(expression.Inputs & 2) != 0 ? reference?.Texture??source.Texture : null)],[("destinationImage", output)],x,(groups+x-1)/x);
            program.RequireContext();
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image|ResourceAccess.TextureUpdate|ResourceAccess.TextureFetch));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            return output;
        }
        catch { output.Dispose();throw; }
    }
    #endregion

}
