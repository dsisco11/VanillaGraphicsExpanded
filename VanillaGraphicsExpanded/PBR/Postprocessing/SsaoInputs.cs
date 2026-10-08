using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Packs the retained SSAO projection, quality and engine sample kernel.</summary>
internal sealed class SsaoInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Reserves a matrix, a vector and 64 std140 sample vectors.</summary>
    internal SsaoInputs() : base(1104) { }
    /// <summary>Preserves the engine kernel while padding vec3 samples to std140 alignment.</summary>
    internal void Capture(float[] projection, int width, int height, int quality, float[] kernel)
    {
        if(projection.Length != 16 || kernel.Length < 192) throw new ArgumentException("Invalid SSAO projection/kernel.");
        WriteMatrix4(0, projection);
        WriteVector4(64, new(width,height,quality,0));
        for(int i=0;i<64;i++) WriteVector4(80+i*16,new(kernel[i*3],kernel[i*3+1],kernel[i*3+2],0));
    }
    #endregion
}
