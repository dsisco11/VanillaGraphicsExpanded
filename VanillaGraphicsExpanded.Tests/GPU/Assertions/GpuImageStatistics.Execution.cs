using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns the explicit multi-stage moment and permutation completion protocols.</summary>
internal sealed partial class GpuImageStatistics
{
    #region Private
    /// <summary>Checks the source domain, reduces double partials, then verifies final completion and transport.</summary>
    private GpuObservationData Moments(GpuImageSnapshot actual, byte[] inputs, int expression,
        (string Name, GpuTexture? Texture)[] sources)
    {
        Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, OpenTK.Graphics.OpenGL4.GL.GetError());
        var inspection = Programs.Get("inspection", actual.Kind);
        var statistics = expression == 1 ? Programs.Get("statistics-poisson") : Programs.Get("statistics", actual.Kind, expression);
        using var state = new GpuAssertionState(inspection.SamplerUnits.Concat(statistics.SamplerUnits).ToArray(), inspection.ImageUnits.Concat(statistics.ImageUnits).ToArray());
        using var output = Programs.Dispatch.CreateResult();
        using var partials = Programs.Dispatch.CreateResult(256 * 16);
        GpuObservationDispatch.Initialize(output);
        partials.UploadDataImmediate(new uint[256 * 16]);
        int texels = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        int groups = (texels + 63) / 64, x = Math.Min(groups, 65535);
        GpuAssertionInputs.Integer(inputs, 12, 0);
        inspection.Execute(inputs, [("actualImage", actual.Texture)], [("resultImage", output)], x, (groups + x - 1) / x);
        inspection.RequireContext();
        GpuObservationDispatch.Barrier();
        GpuAssertionInputs.Integer(inputs, 12, 11);
        statistics.Execute(inputs, sources, [("resultImage", output), ("histogram", partials)], 256);
        statistics.RequireContext();
        GpuObservationDispatch.Barrier();
        GpuAssertionInputs.Integer(inputs, 12, 12);
        statistics.Execute(inputs, sources, [("resultImage", output), ("histogram", partials)], 1);
        statistics.RequireContext();
        GpuObservationDispatch.Barrier();
        statistics.RequireContext();
        return GpuObservationDispatch.Read(actual, output);
    }

    /// <summary>Counts original ranks and then independently visits every expected histogram entry.</summary>
    private GpuObservationData Ranks(GpuImageSnapshot actual, byte[] inputs, int side, int slices)
    {
        Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, OpenTK.Graphics.OpenGL4.GL.GetError());
        var program = Programs.Get("ranks", actual.Kind);
        using var state = new GpuAssertionState(program.SamplerUnits, program.ImageUnits);
        using var output = Programs.Dispatch.CreateResult();
        using var histogram = Programs.Dispatch.CreateResult(checked(side * side), slices);
        GpuObservationDispatch.Initialize(output);
        histogram.UploadDataImmediate(new uint[checked(side * side * slices)]);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        int groups = (count + 63) / 64, x = Math.Min(groups, 65535);
        program.Execute(inputs, [("actualImage", actual.Texture)], [("resultImage", output), ("histogram", histogram)], x, (groups + x - 1) / x);
        program.RequireContext();
        GpuObservationDispatch.Barrier();
        GpuAssertionInputs.Integer(inputs, 12, 4);
        int rankGroups = (side * side * slices + 63) / 64, rankX = Math.Min(rankGroups, 65535);
        program.Execute(inputs, [("actualImage", actual.Texture)], [("resultImage", output), ("histogram", histogram)], rankX, (rankGroups + rankX - 1) / rankX);
        program.RequireContext();
        GpuObservationDispatch.Barrier();
        program.RequireContext();
        var data = GpuObservationDispatch.Read(actual, output);
        if (data.Counters[12] != 0x48495354 || data.Counters[11] != (uint)checked(side * side * slices))
            throw new InvalidOperationException($"GPU rank validation did not complete: magic={data.Counters[12]:x}, ranks={data.Counters[11]}/{side * side * slices}.");
        return data;
    }
    #endregion
}
