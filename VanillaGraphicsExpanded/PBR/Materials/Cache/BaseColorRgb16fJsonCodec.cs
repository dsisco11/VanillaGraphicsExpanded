using System;
using System.Text.Json;

using VanillaGraphicsExpanded.Cache;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal sealed class BaseColorRgb16fJsonCodec : ICacheCodec<BaseColorRgb16f>
{
    public int SchemaVersion => 1;

    public bool TryEncode(in BaseColorRgb16f payload, out byte[] bytes)
    {
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(new Dto
            {
                SchemaVersion = SchemaVersion,
                R = payload.R,
                G = payload.G,
                B = payload.B,
            });
            return true;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    public bool TryDecode(ReadOnlySpan<byte> bytes, out BaseColorRgb16f payload)
    {
        payload = default;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes.ToArray());
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            int schema = 0;
            if (root.TryGetProperty("schemaVersion", out JsonElement sv) && sv.ValueKind == JsonValueKind.Number)
            {
                schema = sv.GetInt32();
            }

            // If missing, treat as v1 for forward-compat with early caches.
            if (schema != 0 && schema != SchemaVersion)
            {
                return false;
            }

            if (!root.TryGetProperty("r", out JsonElement r) || r.ValueKind != JsonValueKind.Number) return false;
            if (!root.TryGetProperty("g", out JsonElement g) || g.ValueKind != JsonValueKind.Number) return false;
            if (!root.TryGetProperty("b", out JsonElement b) || b.ValueKind != JsonValueKind.Number) return false;

            int ri = r.GetInt32();
            int gi = g.GetInt32();
            int bi = b.GetInt32();

            if ((uint)ri > ushort.MaxValue || (uint)gi > ushort.MaxValue || (uint)bi > ushort.MaxValue)
            {
                return false;
            }

            payload = new BaseColorRgb16f((ushort)ri, (ushort)gi, (ushort)bi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Dto
    {
        public int SchemaVersion { get; set; }

        public int R { get; set; }

        public int G { get; set; }

        public int B { get; set; }
    }
}
