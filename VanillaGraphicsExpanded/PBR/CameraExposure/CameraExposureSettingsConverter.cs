using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Consumes legacy multiplier-stop field identities once and writes only calibrated camera settings.</summary>
internal sealed class CameraExposureSettingsConverter : JsonConverter<CameraExposureSettings>
{
    #region Public API
    /// <summary>Uses ordinary serialization of the current public camera properties.</summary>
    public override bool CanWrite => false;

    /// <summary>Normalizes persisted identities before populating current fields, preserving explicit new values.</summary>
    public override CameraExposureSettings? ReadJson(JsonReader reader, Type objectType,
        CameraExposureSettings? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var document = JObject.Load(reader);
        Migrate(document);
        var settings = existingValue ?? new CameraExposureSettings();
        // Populate the existing owner rather than recursively invoking its converter.
        using var source = document.CreateReader();
        serializer.Populate(source, settings);
        return settings;
    }

    /// <summary>Serialization is handled by the default object writer because CanWrite is false.</summary>
    public override void WriteJson(JsonWriter writer, CameraExposureSettings? value, JsonSerializer serializer)
        => throw new NotSupportedException();

    /// <summary>Converts old keys before ConfigLib materializes defaults, leaving current-key precedence unambiguous.</summary>
    internal static bool Migrate(JObject document)
    {
        bool legacy = document.Property("ManualEV") is not null || document.Property("MinEV") is not null
            || document.Property("MaxEV") is not null;
        if (legacy)
        {
            float minimum = LegacyValue(document["MinEV"], -20);
            float maximum = Math.Max(minimum, LegacyValue(document["MaxEV"], 2));
            if (document.Property("ManualEV100") is null && document.Property("ManualEV") is not null)
                document["ManualEV100"] = -LegacyValue(document["ManualEV"], 0);
            // Bounds form a pair: validate the old interval before reversing its sign/order.
            if (document.Property("MinEV") is not null || document.Property("MaxEV") is not null)
            {
                if (document.Property("MinEV100") is null) document["MinEV100"] = -maximum;
                if (document.Property("MaxEV100") is null) document["MaxEV100"] = -minimum;
            }
            document.Remove("ManualEV"); document.Remove("MinEV"); document.Remove("MaxEV");
        }
        // Presence wins even when malformed: never resurrect an old value behind an invalid new key.
        bool repaired = RepairCurrent(document, "ManualEV100", 0);
        repaired |= RepairCurrent(document, "MinEV100", -2);
        repaired |= RepairCurrent(document, "MaxEV100", 20);
        return legacy || repaired;
    }
    #endregion

    #region Private
    /// <summary>Replaces an explicitly invalid camera value with its current finite default before JSON assignment.</summary>
    private static bool RepairCurrent(JObject document, string key, float fallback)
    {
        if (document.Property(key) is null) return false;
        if (TryNumber(document[key], out float value)) return false;
        document[key] = fallback;
        return true;
    }

    /// <summary>Reads numerical tokens invariantly without accepting booleans, objects or non-finite values.</summary>
    private static bool TryNumber(JToken? token, out float value)
    {
        value = 0;
        return token is not null && token.Type is JTokenType.Integer or JTokenType.Float or JTokenType.String
            && float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }

    /// <summary>Reproduces the finite legacy snapshot domain before converting a saved gain.</summary>
    private static float LegacyValue(JToken? token, float fallback)
    {
        return Math.Clamp(TryNumber(token, out float value) ? value : fallback, -48, 24);
    }
    #endregion
}
