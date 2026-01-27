using System;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            if (TryGetInt(root, "schemaVersion", "SchemaVersion", out int schemaValue))
            {
                schema = schemaValue;
            }

            // If missing, treat as v1 for forward-compat with early caches.
            if (schema != 0 && schema != SchemaVersion)
            {
                return false;
            }

            if (!TryGetInt(root, "r", "R", out int ri)) return false;
            if (!TryGetInt(root, "g", "G", out int gi)) return false;
            if (!TryGetInt(root, "b", "B", out int bi)) return false;

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

    private static bool TryGetInt(JsonElement root, string primaryName, string legacyName, out int value)
    {
        value = default;

        if (root.TryGetProperty(primaryName, out JsonElement elem) && elem.ValueKind == JsonValueKind.Number)
        {
            value = elem.GetInt32();
            return true;
        }

        if (root.TryGetProperty(legacyName, out elem) && elem.ValueKind == JsonValueKind.Number)
        {
            value = elem.GetInt32();
            return true;
        }

        return false;
    }

    private sealed class Dto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("r")]
        public int R { get; set; }

        [JsonPropertyName("g")]
        public int G { get; set; }

        [JsonPropertyName("b")]
        public int B { get; set; }
    }
}
