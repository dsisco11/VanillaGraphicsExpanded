using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns exact CPU-double expectation encoding independently of comparison execution.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Private
    /// <summary>Packs authored double bands into separate low/high integer layers without rounding to floating texture storage.</summary>
    private static Texture3D CreateDoubleInput(GpuImageSnapshot actual, IReadOnlyList<double[]> bands)
    {
        int width = actual.Texture.Width, height = actual.Texture.Height, depth = actual.Texture.Depth;
        int plane = checked(width * height), count = checked(plane * depth);
        uint[] bits = new uint[checked(count * 8 * bands.Count)];
        for (int band = 0; band < bands.Count; band++)
        for (int pixel = 0; pixel < count; pixel++)
        for (int channel = 0; channel < actual.Channels; channel++)
        {
            ulong value = BitConverter.DoubleToUInt64Bits(bands[band][pixel * actual.Channels + channel]);
            int index = ((pixel / plane * bands.Count * 2 + band * 2) * plane + pixel % plane) * 4 + channel;
            bits[index] = (uint)value; bits[index + plane * 4] = (uint)(value >> 32);
        }
        var input = CreateArray(TextureFormat.Rgba32ui, width, height, checked(depth * 2 * bands.Count));
        try { input.UploadDataImmediate(bits, 0, 0, 0, width, height, input.Depth); return input; }
        catch { input.Dispose(); throw; }
    }

    #endregion
}
