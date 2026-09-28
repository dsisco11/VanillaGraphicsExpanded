using Newtonsoft.Json.Linq;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Materializes missing current settings so ConfigLib can replace their nested JSON paths.</summary>
internal static class ConfigDocumentDefaults
{
    #region Document preparation
    /// <summary>Adds absent defaults without overwriting saved values or interpreting obsolete keys.</summary>
    internal static bool FillMissing(JObject document, JObject defaults)
    {
        bool changed = false;
        foreach (var property in defaults.Properties())
        {
            var existing = document.Property(property.Name);
            if (existing is null || (existing.Value.Type == JTokenType.Null && property.Value.Type != JTokenType.Null))
            {
                document[property.Name] = property.Value.DeepClone();
                changed = true;
            }
            else if (existing.Value is JObject child && property.Value is JObject childDefaults)
            {
                // Walk object members only; arrays and scalar settings remain owned by their saved value.
                changed |= FillMissing(child, childDefaults);
            }
        }
        return changed;
    }
    #endregion
}
