using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Converts fixture images on the GPU with integer nearest selection and zero channel expansion.</summary>
internal sealed class GpuLayerCopy : IDisposable
{
    private readonly GpuFixtureProgram program = new(Source);

    #region Public API
    /// <summary>Retires the conversion executable on its original context.</summary>
    public void Dispose() => program.Dispose();
    #endregion

    #region Internal API
    /// <summary>Copies a complete 2D fixture into one RGBA32F layer without downloading pixels.</summary>
    internal void Copy(GpuTexture source, Texture3D destination, int layer)
    {
        program.RequireContext(); source.RequireOriginalContext(); destination.RequireOriginalContext();
        using var input = new GpuImageSnapshot(source);
        if (input.Texture.Depth != 1 || input.Kind != 0) throw new ArgumentException("Layer conversion requires one floating/normalized image.");
        using var state = new GpuAssertionState();
        int id = program.Id;
        GL.ProgramUniform2(id, 0, destination.Width, destination.Height);
        GL.ProgramUniform1(id, 1, input.Channels); GL.ProgramUniform1(id, 2, layer);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2DArray, input.Texture.TextureId); GL.BindSampler(0, 0);
        GL.BindImageTexture(0, destination.TextureId, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);
        GL.UseProgram(id);
        GL.DispatchCompute((destination.Width + 7) / 8, (destination.Height + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
    }
    #endregion

    #region Private
    private const string Source = """
        #version 450 core
        layout(local_size_x=8,local_size_y=8)in;
        layout(binding=0)uniform sampler2DArray sourceImage;
        layout(rgba32f,binding=0)writeonly uniform image2DArray destination;
        layout(location=0)uniform ivec2 extent;
        layout(location=1)uniform int channels;
        layout(location=2)uniform int layer;
        /// Maps integer nearest texels and expands only authored channels, including zero alpha.
        void main(){
            ivec2 p=ivec2(gl_GlobalInvocationID.xy);
            if(any(greaterThanEqual(p,extent)))return;
            ivec2 from=p*textureSize(sourceImage,0).xy/extent;
            vec4 original=texelFetch(sourceImage,ivec3(from,0),0),expanded=vec4(0);
            for(int c=0;c<channels;c++)expanded[c]=original[c];
            imageStore(destination,ivec3(p,layer),expanded);
        }
        """;
    #endregion
}
