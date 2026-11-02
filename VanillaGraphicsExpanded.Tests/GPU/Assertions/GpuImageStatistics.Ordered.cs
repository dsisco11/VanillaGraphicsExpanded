using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Executes serial floating oracles without a full-domain output or unrelated moment reductions.</summary>
internal sealed partial class GpuImageStatistics
{
    #region Internal API
    /// <summary>Preserves the authored expression, traversal and each float rounding, returning one completed scalar.</summary>
    internal float OrderedFloatSum(GpuTexture image, GpuImageProjection term)
    {
        Programs.RequireContext();
        if (!term.IsOrderedScalar)
            throw new ArgumentException("An ordered reduction requires a declared serial scalar expression.", nameof(term));
        using var source = new GpuObservationImage(image);
        int count = GpuObservationLimits.RequireDomain(source.Texture.Width, source.Texture.Height, source.Texture.Depth);
        var program = Programs.Get("projection-" + term.Domain, source.Kind);
        using var state = new GpuAssertionState(program.SamplerUnits, program.ImageUnits);
        using var scalar = CreateArray(TextureFormat.Rgba32f, 1, 1, 1);
        scalar.UploadDataImmediate(new float[] { float.NaN, 0, 0, 0 }, 0, 0, 0, 1, 1, 1);
        var inputs = DomainInputs(source, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, 30);
        GpuAssertionInputs.Integer(inputs, 92, (uint)term.Choice);
        // The existing domain-owned policy performs its original z/y/x loop at invocation zero.
        program.Execute(inputs, [("sourceImage", source.Texture)], [("destinationImage", scalar)], 1);
        program.RequireContext();
        GpuObservationDispatch.Barrier();
        float[] values = new float[4];
        using (OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, 0))
        using (OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelPackState(4)))
            GL.GetTextureImage(scalar.TextureId, 0, PixelFormat.Rgba, PixelType.Float, 16, values);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        if (BitConverter.SingleToUInt32Bits(values[1]) != 0x47505541 ||
            ((uint)values[2] | ((uint)values[3] << 16)) != (uint)count)
            throw new InvalidOperationException("Ordered scalar observation did not complete its selected domain.");
        Assert.True(float.IsFinite(values[0]), "Ordered scalar observation produced a nonfinite result.");
        return values[0];
    }
    #endregion
}
