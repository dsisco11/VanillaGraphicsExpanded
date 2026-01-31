using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VanillaGraphicsExpanded.ModSystems;

internal sealed class ConfigModSystem : ModSystem
{
    public const string ConfigSavedEvent = "configlib:{0}:config-saved";
    public const string ConfigChangedEvent = "configlib:{0}:setting-changed";
    public const string ConfigLoadedEvent = "configlib:{0}:setting-loaded";
    public const string ConfigReloadEvent = "configlib:config-reload";

    private ICoreAPI? api;
    private readonly object configLibMappingLock = new();
    private System.Collections.Generic.Dictionary<string, string>? configLibMappingKeyToCodePath;
    private int configLibHasPendingChanges;
    
    /// <summary>
    /// The mod configuration. Loaded on startup.
    /// </summary>
    public static VgeConfig Config { get; private set; } = new();
    private static bool configLoaded = false;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override double ExecuteOrder() => 0.0;

    /// <summary>
    /// Ensures the config is loaded. Can be called from other ModSystems' ShouldLoad().
    /// </summary>
    public static void EnsureConfigLoaded(ICoreAPI api)
    {
        if (configLoaded) return;
        
        try
        {
            var loadedConfig = api.LoadModConfig<VgeConfig>(Constants.ConfigFileName);
            if (loadedConfig is null)
            {
                Config = new VgeConfig();
                api.StoreModConfig(Config, Constants.ConfigFileName);
                api.Logger.Notification("[VGE] Created default LumOn config");
            }
            else
            {
                Config = loadedConfig;
            }

            Config.Sanitize();
        }
        catch (Exception ex)
        {
            api.Logger.Error("[VGE] Failed to load configuration: {0}", ex.Message);
            Config = new VgeConfig();
            Config.Sanitize();
        }
        
        configLoaded = true;
    }

    public override void StartPre(ICoreAPI api)
    {
        this.api = api;

        EnsureConfigLoaded(api);
    }

    public override void Start(ICoreAPI api)
    {
        this.api = api;

        // ConfigLib emits events on the VS event bus when settings change / config is saved.
        // We intentionally avoid reflection against ConfigLib internals. Instead, we treat ConfigLib as the
        // source of truth for its managed settings, apply those onto our mod config, then persist our mod config.
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: string.Format(ConfigSavedEvent, Constants.ModId));
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: string.Format(ConfigChangedEvent, Constants.ModId));
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: string.Format(ConfigLoadedEvent, Constants.ModId));
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: ConfigReloadEvent);
    }

    private void OnConfigLibEvent(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (api is null) return;

        api.Logger.Debug("[VGE] ConfigLib event: {0}. data={1}", eventName, SafeAttributeDump(data));

        bool isSaved = string.Equals(eventName, string.Format(ConfigSavedEvent, Constants.ModId), StringComparison.OrdinalIgnoreCase);
        bool isReload = string.Equals(eventName, ConfigReloadEvent, StringComparison.OrdinalIgnoreCase);

        var mappingKeyToCodePath = GetConfigLibMappingKeyToCodePath(api);
        int applied = ApplyConfigLibSettingsToModConfig(Config, data, mappingKeyToCodePath, out string? applySummary);
        if (applied > 0)
        {
            Interlocked.Exchange(ref configLibHasPendingChanges, 1);

            Config.Sanitize();
            api.Logger.Debug("[VGE] Applied {0} ConfigLib setting update(s) ({1}).", applied, applySummary ?? "n/a");
            api.Logger.Debug("[VGE] Config (after ConfigLib apply): {0}", SafeDebugJson(Config));
            LiveConfigReload.NotifyAll(api);
        }
        else
        {
            api.Logger.Debug("[VGE] ConfigLib event produced no applicable setting updates. event={0}", eventName);
        }

        if (isSaved || isReload)
        {
            // ConfigLib emits multiple events; only persist our mod config when we actually applied any setting changes.
            // Otherwise we risk overwriting the file/UI state with stale values.
            if (Interlocked.Exchange(ref configLibHasPendingChanges, 0) != 0)
            {
                Config.Sanitize();
                PersistConfigAndNotifyReloadRequired(api, source: "ConfigLib");
            }
            else
            {
                api.Logger.Debug("[VGE] ConfigLib {0} event with no pending applied changes; skipping mod config persistence.", eventName);
            }
        }
    }

    internal static void PersistConfigAndNotifyReloadRequired(ICoreAPI api, string source)
    {
        api.Logger.Debug("[VGE] Persisting config after update via {0}: {1}", source, SafeDebugJson(Config));
        api.StoreModConfig(Config, Constants.ConfigFileName);

        api.Logger.Notification(
            "[VanillaExpanded] Configuration updated via {0}. Some changes apply immediately; others apply after re-entering the world (and may require a restart).",
            source
        );
    }

    public override void Dispose()
    {
        base.Dispose();

        api = null;
    }

    private readonly record struct ConfigLibSettingUpdate(string Path, string Value);

    private static int ApplyConfigLibSettingsToModConfig(
        VgeConfig config,
        IAttribute data,
        System.Collections.Generic.IReadOnlyDictionary<string, string> mappingKeyToCodePath,
        out string? summary)
    {
        ConfigLibSettingUpdate[] updates = ExtractSettingUpdates(data);
        if (updates.Length == 0)
        {
            summary = null;
            return 0;
        }

        var patch = new JObject();

        int applicable = 0;
        int skipped = 0;
        string[] skipSamples = new string[3];
        foreach (ConfigLibSettingUpdate update in updates)
        {
            string path = ResolveConfigLibPath(update.Path, mappingKeyToCodePath);

            if (!IsConfigPathValid(typeof(VgeConfig), path))
            {
                if (skipped < skipSamples.Length)
                {
                    skipSamples[skipped] = $"{update.Path}=>{path}";
                }

                skipped++;
                continue;
            }


            SetJObjectByDotPath(patch, path, ParseConfigLibValue(update.Value));
            applicable++;
        }

        string preview = string.Join(", ", updates.Take(4).Select(u => $"{u.Path}={u.Value}")) + (updates.Length > 4 ? "…" : string.Empty);
        summary = skipped == 0
            ? $"applicable {applicable}/{updates.Length}: {preview}"
            : $"applicable {applicable}/{updates.Length}, skipped {skipped}: {preview}. Skip samples: {string.Join("; ", skipSamples.Where(s => !string.IsNullOrWhiteSpace(s)))}";

        if (applicable == 0)
        {
            return 0;
        }

        // Let Json.NET do the actual assignment and type conversion against our config model.
        // This avoids manual reflection setters and handles enums/arrays/etc.
        try
        {
            JsonConvert.PopulateObject(
                patch.ToString(Formatting.None),
                config,
                new JsonSerializerSettings
                {
                    // Keep existing object instances where possible; replace arrays as needed.
                    ObjectCreationHandling = ObjectCreationHandling.Auto,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Include,
                    Error = (_, args) => { args.ErrorContext.Handled = true; }
                });
        }
        catch
        {
            // If population fails for some unexpected reason, treat as "no changes applied".
            return 0;
        }

        return applicable;
    }

    private static string ResolveConfigLibPath(string pathOrKey, System.Collections.Generic.IReadOnlyDictionary<string, string> mappingKeyToCodePath)
    {
        // If ConfigLib sends "MappingKey" (e.g. ENABLED), map it to the actual JSON/property path ("LumOn.Enabled")
        // using our configlib-patches.json definitions. If it already looks like a path, keep it.
        if (pathOrKey.IndexOf('.') >= 0)
        {
            return pathOrKey;
        }

        return mappingKeyToCodePath.TryGetValue(pathOrKey, out string? path) ? path : pathOrKey;
    }

    private static JToken ParseConfigLibValue(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0) return JValue.CreateString(string.Empty);

        // ConfigLib values are string-serialized; try interpreting them as JSON first so
        // numbers/bools/arrays/objects flow through correctly.
        try
        {
            return JToken.Parse(trimmed);
        }
        catch
        {
            return JValue.CreateString(raw);
        }
    }

    private static void SetJObjectByDotPath(JObject root, string dotPath, JToken value)
    {
        string[] segments = dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return;

        JObject current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            string seg = segments[i];
            if (current[seg] is JObject child)
            {
                current = child;
                continue;
            }

            child = new JObject();
            current[seg] = child;
            current = child;
        }

        current[segments[^1]] = value;
    }

    private static bool IsConfigPathValid(Type rootType, string dotPath)
    {
        if (string.IsNullOrWhiteSpace(dotPath)) return false;

        // Minimal validation so we don't write the mod config on "save" when we didn't actually apply anything.
        // This keeps the integration safe while still relying on Json.NET for the heavy lifting.
        string[] segments = dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return false;

        Type current = rootType;
        for (int i = 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            MemberInfo? member = current.GetProperty(seg, BindingFlags.Instance | BindingFlags.Public)
                             ?? (MemberInfo?)current.GetField(seg, BindingFlags.Instance | BindingFlags.Public);
            if (member is null) return false;

            if (i == segments.Length - 1) return true;

            Type next;
            switch (member)
            {
                case PropertyInfo p:
                    next = p.PropertyType;
                    break;
                case FieldInfo f:
                    next = f.FieldType;
                    break;
                default:
                    return false;
            }

            Type nn = Nullable.GetUnderlyingType(next) ?? next;
            if (nn.IsValueType) return false;

            current = nn;
        }

        return false;
    }

    private System.Collections.Generic.IReadOnlyDictionary<string, string> GetConfigLibMappingKeyToCodePath(ICoreAPI api)
    {
        if (configLibMappingKeyToCodePath is not null)
        {
            return configLibMappingKeyToCodePath;
        }

        lock (configLibMappingLock)
        {
            if (configLibMappingKeyToCodePath is not null)
            {
                return configLibMappingKeyToCodePath;
            }

            try
            {
                // This is our ConfigLib patch definition file. ConfigLib uses the "code" property as the path into our config.
                List<IAsset> assets = api.Assets.GetManyInCategory(
                    AssetCategory.config.Code,
                    "configlib-patches.json",
                    domain: Constants.ModId,
                    loadAsset: true);

                if (assets.Count == 0)
                {
                    api.Logger.Warning("[VGE] Could not load configlib-patches.json from assets; MappingKey->path translation will be unavailable.");
                    configLibMappingKeyToCodePath = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return configLibMappingKeyToCodePath;
                }

                string text = assets[0].ToText();
                var root = JObject.Parse(text);
                var settings = root["settings"] as JObject;

                var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (settings is not null)
                {
                    foreach (JProperty category in settings.Properties())
                    {
                        if (category.Value is not JObject categoryObj) continue;
                        foreach (JProperty setting in categoryObj.Properties())
                        {
                            if (setting.Value is not JObject settingObj) continue;
                            string? code = settingObj.Value<string>("code");
                            if (string.IsNullOrWhiteSpace(code)) continue;
                            map[setting.Name] = code;
                        }
                    }
                }

                configLibMappingKeyToCodePath = map;
                api.Logger.Debug("[VGE] Loaded ConfigLib mapping table: {0} entries.", map.Count);
                return configLibMappingKeyToCodePath;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[VGE] Failed to parse configlib-patches.json for MappingKey->path translation: {0}", ex);
                configLibMappingKeyToCodePath = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return configLibMappingKeyToCodePath;
            }
        }
    }

    private static ConfigLibSettingUpdate[] ExtractSettingUpdates(IAttribute data)
    {
        if (data is TreeAttribute tree)
        {
            var list = new System.Collections.Generic.List<ConfigLibSettingUpdate>(32);

            if (TryExtractSingleSettingFromTree(tree, out ConfigLibSettingUpdate single))
            {
                list.Add(single);
            }

            if (TryGetTree(tree, "Settings", out TreeAttribute? settingsTree))
            {
                foreach (string key in settingsTree!.Keys)
                {
                    if (settingsTree![key] is not TreeAttribute entry) continue;
                    if (TryExtractSingleSettingFromTree(entry, out ConfigLibSettingUpdate entrySetting))
                    {
                        list.Add(entrySetting);
                    }
                }
            }

            return list
                .Where(s => !string.IsNullOrWhiteSpace(s.Path))
                .ToArray();
        }

        if (data is StringAttribute str && !string.IsNullOrWhiteSpace(str.value))
        {
            try
            {
                var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(str.value);
                if (dict is null || dict.Count == 0) return Array.Empty<ConfigLibSettingUpdate>();
                return dict.Select(kv => new ConfigLibSettingUpdate(kv.Key, kv.Value)).ToArray();
            }
            catch
            {
                return Array.Empty<ConfigLibSettingUpdate>();
            }
        }

        return Array.Empty<ConfigLibSettingUpdate>();
    }

    private static bool TryExtractSingleSettingFromTree(TreeAttribute tree, out ConfigLibSettingUpdate update)
    {
        update = default;

        string? mappingKey = TryGetString(tree, "MappingKey")
            ?? TryGetString(tree, "Code")
            ?? TryGetString(tree, "Key");

        string? value = TryGetString(tree, "Value")
            ?? TryGetString(tree, "NewValue");

        if (string.IsNullOrWhiteSpace(mappingKey) || value is null)
        {
            return false;
        }

        update = new ConfigLibSettingUpdate(mappingKey, value);
        return true;
    }

    private static bool TryGetTree(TreeAttribute tree, string key, out TreeAttribute? value)
    {
        value = null;
        foreach (string k in tree.Keys)
        {
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = tree[k] as TreeAttribute;
            return value is not null;
        }

        return false;
    }

    private static string? TryGetString(TreeAttribute tree, string key)
    {
        foreach (string k in tree.Keys)
        {
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;

            return tree[k] switch
            {
                StringAttribute s => s.value,
                IntAttribute i => i.value.ToString(CultureInfo.InvariantCulture),
                LongAttribute l => l.value.ToString(CultureInfo.InvariantCulture),
                FloatAttribute f => f.value.ToString("R", CultureInfo.InvariantCulture),
                DoubleAttribute d => d.value.ToString("R", CultureInfo.InvariantCulture),
                BoolAttribute b => b.value ? "true" : "false",
                _ => tree[k].ToString()
            };
        }

        return null;
    }

    private static string SafeDebugJson(object? value, int maxChars = 12_000)
    {
        if (value is null) return "<null>";

        string json;
        try
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Error = (_, args) => { args.ErrorContext.Handled = true; }
            };

            json = JsonConvert.SerializeObject(value, settings);
        }
        catch (Exception ex)
        {
            return $"<failed-to-serialize: {ex.GetType().Name}: {ex.Message}>";
        }

        if (json.Length <= maxChars) return json;

        int fullLength = json.Length;
        return json.Substring(0, maxChars) + $"... <truncated; {fullLength} chars total>";
    }

    private static string SafeAttributeDump(IAttribute? value, int maxChars = 2_000, int maxDepth = 4)
    {
        if (value is null) return "<null>";

        try
        {
            string s = DumpAttribute(value, depth: 0, maxDepth);
            if (s.Length <= maxChars) return s;
            int fullLength = s.Length;
            return s.Substring(0, maxChars) + $"... <truncated; {fullLength} chars total>";
        }
        catch (Exception ex)
        {
            return $"<failed-to-dump-attribute: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    private static string DumpAttribute(IAttribute value, int depth, int maxDepth)
    {
        if (depth >= maxDepth)
        {
            return $"<{value.GetType().Name}>";
        }

        return value switch
        {
            StringAttribute s => $"\"{s.value}\"",
            BoolAttribute b => b.value ? "true" : "false",
            IntAttribute i => i.value.ToString(),
            LongAttribute l => l.value.ToString(),
            FloatAttribute f => f.value.ToString("R"),
            DoubleAttribute d => d.value.ToString("R"),
            TreeAttribute t => DumpTree(t, depth, maxDepth),
            _ => $"<{value.GetType().Name}:{value}>"
        };
    }

    private static string DumpTree(TreeAttribute tree, int depth, int maxDepth)
    {
        if (tree.Count == 0) return "{}";

        // TreeAttribute has Keys/this[] accessors.
        string[] keys = tree.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);

        return "{" + string.Join(
            ",",
            keys.Select(k => $"\"{k}\":{DumpAttribute(tree[k], depth + 1, maxDepth)}")) + "}";
    }
}
