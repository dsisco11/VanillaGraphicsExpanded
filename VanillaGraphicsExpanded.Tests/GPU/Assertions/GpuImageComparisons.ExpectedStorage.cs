using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Packs independent expectations as scalar words without RGBA padding or separate double bands.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Internal API
    /// <summary>Counts uploaded independent reference bytes, excluding subjects and result storage.</summary>
    internal long UploadedExpectationBytes { get; private set; }
    #endregion

    #region Private
    /// <summary>Interleaves each component's exact double bounds, retaining all IEEE bits.</summary>
    private Texture3D CreateDoubleInput(int channels, IReadOnlyList<double[]> bands, uint? channelMask)
    {
        uint mask = channelMask ?? (1u << channels) - 1;
        if (mask == 0 || (mask & ~((1u << channels) - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(channelMask));
        int components = checked(bands[0].Length / channels * BitOperations.PopCount(mask));
        uint[] words = new uint[checked(components * bands.Count * 2)];
        int selected = 0;
        for (int scalar = 0; scalar < bands[0].Length; scalar++)
        {
            if ((mask & (1u << (scalar % channels))) == 0) continue;
            for (int band = 0; band < bands.Count; band++)
            {
                ulong bits = BitConverter.DoubleToUInt64Bits(bands[band][scalar]);
                int address = (selected * bands.Count + band) * 2;
                words[address] = (uint)bits;
                words[address + 1] = (uint)(bits >> 32);
            }
            selected++;
        }
        return CreateWords(words);
    }

    /// <summary>Removes unrepresented lanes while preserving every selected component and interleaved field.</summary>
    private static uint[] SelectedWords(uint[] values, int channels, uint? channelMask, int fields = 1)
    {
        uint mask = channelMask ?? (1u << channels) - 1;
        if (mask == 0 || (mask & ~((1u << channels) - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(channelMask));
        if (mask == (1u << channels) - 1) return values;
        uint[] result = new uint[checked(values.Length / (channels * fields) * BitOperations.PopCount(mask) * fields)];
        int destination = 0;
        for (int scalar = 0; scalar < values.Length / fields; scalar++)
        {
            if ((mask & (1u << (scalar % channels))) == 0) continue;
            Array.Copy(values, scalar * fields, result, destination, fields);
            destination += fields;
        }
        return result;
    }

    /// <summary>Uploads scalar records using only a final partial row of bounded padding.</summary>
    private Texture3D CreateWords(uint[] words)
    {
        int width = Math.Min(words.Length, 4096), height = checked((words.Length + width - 1) / width);
        uint[] packed = words.Length == width * height ? words : new uint[checked(width * height)];
        if (!ReferenceEquals(packed, words)) words.CopyTo(packed, 0);
        var input = CreateArray(TextureFormat.R32ui, width, height, 1);
        try
        {
            input.UploadDataImmediate(packed, 0, 0, 0, width, height, 1);
            UploadedExpectationBytes += checked((long)packed.Length * sizeof(uint));
            return input;
        }
        catch { input.Dispose(); throw; }
    }
    #endregion
}
