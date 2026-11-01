using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Converts fixture images on the GPU with integer nearest selection and zero channel expansion.</summary>
internal sealed class GpuLayerCopy : IDisposable
{


    #region Public API
    /// <summary>Completes this stateless conversion consumer; the fixture owns the production executable.</summary>
    public void Dispose() { }
    #endregion

    #region Internal API
    /// <summary>Copies a complete 2D fixture into one RGBA32F layer without downloading pixels.</summary>
    internal void Copy(GpuTexture source, Texture3D destination, int layer)
    {
        var program = Fixtures.HeadlessGLFixture.Current.LayerProgram;
        program.RequireContext(); source.RequireOriginalContext(); destination.RequireOriginalContext();
        using var input = new GpuImageSnapshot(source);
        if (input.Texture.Depth != 1 || input.Kind != 0) throw new ArgumentException("Layer conversion requires one floating/normalized image.");
        using var state = new GpuAssertionState();
        byte[] bytes=new byte[160];
        GpuAssertionInputs.Integer(bytes,0,(uint)destination.Width);GpuAssertionInputs.Integer(bytes,4,(uint)destination.Height);
        GpuAssertionInputs.Integer(bytes,80,(uint)input.Channels);GpuAssertionInputs.Integer(bytes,84,(uint)layer);
        program.Execute(bytes,[input.Texture,input.Texture],[destination],(destination.Width+7)/8,(destination.Height+7)/8);
        RHICommandContext.Current.Barrier(new(ResourceAccess.TextureFetch | ResourceAccess.TextureUpdate));
    }
    #endregion

}
