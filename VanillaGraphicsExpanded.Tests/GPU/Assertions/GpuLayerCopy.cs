using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Converts fixture images on the GPU with integer nearest selection and zero channel expansion.</summary>
internal sealed class GpuLayerCopy(GpuObservationPrograms programs)
{


    #region Internal API
    /// <summary>Copies a complete 2D fixture into one RGBA32F layer without downloading pixels.</summary>
    internal void Copy(GpuTexture source, Texture3D destination, int layer)
    {
        var program = programs.Get("layer");
        program.RequireContext(); source.RequireOriginalContext(); destination.RequireOriginalContext();
        using var input = new GpuImageSnapshot(source);
        if (input.Texture.Depth != 1 || input.Kind != 0) throw new ArgumentException("Layer conversion requires one floating/normalized image.");
        using var state = new GpuAssertionState(program.SamplerUnits, program.ImageUnits);
        byte[] bytes=new byte[160];
        GpuAssertionInputs.Integer(bytes,0,(uint)destination.Width);GpuAssertionInputs.Integer(bytes,4,(uint)destination.Height);
        GpuAssertionInputs.Integer(bytes,80,(uint)input.Channels);GpuAssertionInputs.Integer(bytes,84,(uint)layer);
        program.Execute(bytes,[("sourceImage",input.Texture)],[("destination",destination)],(destination.Width+7)/8,(destination.Height+7)/8);
        program.RequireContext();
        RHICommandContext.Current.Barrier(new(ResourceAccess.TextureFetch | ResourceAccess.TextureUpdate));
    }
    #endregion

}
