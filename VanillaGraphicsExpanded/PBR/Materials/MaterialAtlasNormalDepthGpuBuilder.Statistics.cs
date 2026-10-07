using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Measures solver readbacks for stable atlas height normalization.</summary>
internal static partial class MaterialAtlasNormalDepthGpuBuilder
{
    #region Private
    /// <summary>Reads the solved field and measures its distribution for height normalization.</summary>
    private static (float mean, float center, float min, float max) ComputeStatsR32f(DynamicTexture2D tex)
    {
        using var framebuffer = StateCache.Current.BindFramebufferScope();
        using var pack = StateCache.Current.SetPixelPackScope(new(1));
        using var transfer = StateCache.Current.BindBufferScope(BufferTarget.PixelPackBuffer, 0);
        float[] data = tex.ReadPixels();
        if (data.Length == 0) return (0f, 0f, 0f, 0f);

        // R32F: one float per pixel.
        // Use SIMD for sum/min/max (single pass) where supported.
        int i = 0;
        int len = data.Length;

        Vector<float> vSum = Vector<float>.Zero;
        Vector<float> vMin = new(float.PositiveInfinity);
        Vector<float> vMax = new(float.NegativeInfinity);

        int vecCount = Vector<float>.Count;
        int lastVec = len - (len % vecCount);
        for (; i < lastVec; i += vecCount)
        {
            var v = new Vector<float>(data, i);
            vSum += v;
            vMin = Vector.Min(vMin, v);
            vMax = Vector.Max(vMax, v);
        }

        double sum = 0;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        for (int lane = 0; lane < vecCount; lane++)
        {
            sum += vSum[lane];
            float mn = vMin[lane];
            float mx = vMax[lane];
            if (mn < min) min = mn;
            if (mx > max) max = mx;
        }

        for (; i < len; i++)
        {
            float v = data[i];
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        int count = data.Length;
        float mean = (float)(sum / count);
        if (float.IsPositiveInfinity(min) || float.IsNegativeInfinity(max))
        {
            min = mean;
            max = mean;
        }

        // Robust center: median (computed in-place via quickselect).
        float center = SelectMedianInPlace(data);
        return (mean, center, min, max);
    }

    /// <summary>Selects the middle value without fully sorting the readback array.</summary>
    private static float SelectMedianInPlace(float[] data)
    {
        int n = data.Length;
        if (n == 0) return 0f;

        int k = n / 2;
        int left = 0;
        int right = n - 1;

        while (true)
        {
            if (left == right) return data[left];

            int pivotIndex = (left + right) >>> 1;
            pivotIndex = Partition(data, left, right, pivotIndex);

            if (k == pivotIndex) return data[k];
            if (k < pivotIndex) right = pivotIndex - 1;
            else left = pivotIndex + 1;
        }
    }

    /// <summary>Partitions a range around its selected pivot for median selection.</summary>
    private static int Partition(float[] data, int left, int right, int pivotIndex)
    {
        float pivotValue = data[pivotIndex];
        (data[pivotIndex], data[right]) = (data[right], data[pivotIndex]);

        int storeIndex = left;
        for (int i = left; i < right; i++)
        {
            if (data[i] < pivotValue)
            {
                (data[storeIndex], data[i]) = (data[i], data[storeIndex]);
                storeIndex++;
            }
        }

        (data[right], data[storeIndex]) = (data[storeIndex], data[right]);
        return storeIndex;
    }

    #endregion
}
