using System;

namespace VanillaGraphicsExpanded.Noise;

public static class PmjTextureLayouts
{
    public static (int Width, int Height) Layout1D(int sampleCount)
    {
        if (sampleCount <= 0) throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "Sample count must be > 0.");
        return (sampleCount, 1);
    }

    public static (int Width, int Height) LayoutAtlas(int sampleCount, int width)
    {
        if (sampleCount <= 0) throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "Sample count must be > 0.");
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be > 0.");
        if ((sampleCount % width) != 0)
        {
            throw new ArgumentException($"Width must divide sampleCount (width={width}, sampleCount={sampleCount}).", nameof(width));
        }

        return (width, sampleCount / width);
    }

    public static int GetLinearIndex(int x, int y, int width, int height)
    {
        if ((uint)x >= (uint)width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)height) throw new ArgumentOutOfRangeException(nameof(y));
        return (y * width) + x;
    }

    public static int GetTiledFrameIndex(int frameIndex, int cycleLength)
    {
        if (cycleLength <= 0) throw new ArgumentOutOfRangeException(nameof(cycleLength), cycleLength, "Cycle length must be > 0.");
        int m = frameIndex % cycleLength;
        return m < 0 ? m + cycleLength : m;
    }
}
