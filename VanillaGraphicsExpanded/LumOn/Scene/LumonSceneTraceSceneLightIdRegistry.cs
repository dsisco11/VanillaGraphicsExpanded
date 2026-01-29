using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Thread-safe mapping from quantized RGB keys to compact light ids (v1: 6-bit id, 0..63),
/// plus a LUT payload suitable for uploading to <see cref="LumonSceneOccupancyClipmapGpuResources.LightColorLut"/>.
/// </summary>
internal sealed class LumonSceneTraceSceneLightIdRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<int, byte> lightKeyToId = new();

    private readonly float[] lutData = new float[LumonSceneOccupancyClipmapGpuResources.MaxLightColors * 4];
    private bool lutDirty;

    public LumonSceneTraceSceneLightIdRegistry()
    {
        Reset();
    }

    public void Reset()
    {
        lock (gate)
        {
            lightKeyToId.Clear();

            // id 0: neutral white. Fill all entries to reduce undefined sampling if ids are used before assignment.
            for (int i = 0; i < LumonSceneOccupancyClipmapGpuResources.MaxLightColors; i++)
            {
                int o = i * 4;
                lutData[o + 0] = 1.0f;
                lutData[o + 1] = 1.0f;
                lutData[o + 2] = 1.0f;
                lutData[o + 3] = 1.0f;
            }

            lutDirty = true;
        }
    }

    public bool TryCopyAndClearDirtyLut(float[] dst)
    {
        if (dst is null) throw new ArgumentNullException(nameof(dst));
        if (dst.Length < lutData.Length) throw new ArgumentException("Destination buffer too small.", nameof(dst));

        lock (gate)
        {
            if (!lutDirty)
            {
                return false;
            }

            Array.Copy(lutData, dst, lutData.Length);
            lutDirty = false;
            return true;
        }
    }

    public int GetOrAssignLightId(int rgb)
    {
        // Quantize RGB to reduce churn. 3 bits/channel -> 512 possible keys.
        int r = (rgb >> 16) & 0xFF;
        int g = (rgb >> 8) & 0xFF;
        int b = rgb & 0xFF;

        int rq = r >> 5;
        int gq = g >> 5;
        int bq = b >> 5;

        int key = (rq << 6) | (gq << 3) | bq;

        lock (gate)
        {
            if (lightKeyToId.TryGetValue(key, out byte existing))
            {
                return existing;
            }

            // Reserve 0 as a fallback. Allocate new ids from 1..63.
            int nextId = lightKeyToId.Count + 1;
            if (nextId >= LumonSceneOccupancyClipmapGpuResources.MaxLightColors)
            {
                return 0;
            }

            byte id = (byte)nextId;
            lightKeyToId[key] = id;

            float rf = rq / 7.0f;
            float gf = gq / 7.0f;
            float bf = bq / 7.0f;

            int o = id * 4;
            lutData[o + 0] = rf;
            lutData[o + 1] = gf;
            lutData[o + 2] = bf;
            lutData[o + 3] = 1.0f;

            lutDirty = true;
            return id;
        }
    }
}

