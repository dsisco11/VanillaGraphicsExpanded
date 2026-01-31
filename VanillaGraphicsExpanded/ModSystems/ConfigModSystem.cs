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

        int applied = 0;
        int failed = 0;
        string[] failSamples = new string[3];
        foreach (ConfigLibSettingUpdate update in updates)
        {
            string path = ResolveConfigLibPath(update.Path, mappingKeyToCodePath);

            if (TrySetByPath(config, path, update.Value, out string? error))
            {
                applied++;
            }
            else
            {
                if (failed < failSamples.Length)
                {
                    failSamples[failed] = $"{update.Path}=>{path} ({error})";
                }

                failed++;
            }
        }

        string preview = string.Join(", ", updates.Take(4).Select(u => $"{u.Path}={u.Value}")) + (updates.Length > 4 ? "…" : string.Empty);
        summary = failed == 0
            ? $"applied {applied}/{updates.Length}: {preview}"
            : $"applied {applied}/{updates.Length}, failed {failed}: {preview}. Fail samples: {string.Join("; ", failSamples.Where(s => !string.IsNullOrWhiteSpace(s)))}";

        return applied;
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

    private static bool TrySetByPath(object root, string path, string rawValue, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "empty path";
            return false;
        }

        string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            error = "empty path";
            return false;
        }

        object current = root;
        Type currentType = current.GetType();

        for (int i = 0; i < segments.Length - 1; i++)
        {
            string seg = segments[i];

            if (!TryGetMember(currentType, seg, out MemberInfo? member))
            {
                error = $"missing member '{seg}' on {currentType.Name}";
                return false;
            }

            object? next = GetMemberValue(current, member!);
            if (next is null)
            {
                Type nextType = GetMemberType(member!);
                if (nextType.IsValueType)
                {
                    error = $"null value-type member '{seg}' on {currentType.Name}";
                    return false;
                }

                ConstructorInfo? ctor = nextType.GetConstructor(Type.EmptyTypes);
                if (ctor is null)
                {
                    error = $"cannot construct '{nextType.Name}' for member '{seg}'";
                    return false;
                }

                next = ctor.Invoke(null);
                SetMemberValue(current, member!, next);
            }

            current = next;
            currentType = current.GetType();
        }

        string leaf = segments[^1];
        if (!TryGetMember(currentType, leaf, out MemberInfo? leafMember))
        {
            error = $"missing member '{leaf}' on {currentType.Name}";
            return false;
        }

        Type leafType = GetMemberType(leafMember!);
        if (!TryConvertString(rawValue, leafType, out object? converted, out string? convertError))
        {
            error = $"failed to convert '{rawValue}' to {leafType.Name} ({convertError})";
            return false;
        }

        SetMemberValue(current, leafMember!, converted);
        return true;
    }

    private static bool TryGetMember(Type type, string name, out MemberInfo? member)
    {
        member = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                 ?? (MemberInfo?)type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        return member is not null;
    }

    private static Type GetMemberType(MemberInfo member)
        => member switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => throw new NotSupportedException(member.GetType().FullName)
        };

    private static object? GetMemberValue(object obj, MemberInfo member)
        => member switch
        {
            PropertyInfo p => p.GetValue(obj),
            FieldInfo f => f.GetValue(obj),
            _ => throw new NotSupportedException(member.GetType().FullName)
        };

    private static void SetMemberValue(object obj, MemberInfo member, object? value)
    {
        switch (member)
        {
            case PropertyInfo p:
                p.SetValue(obj, value);
                return;
            case FieldInfo f:
                f.SetValue(obj, value);
                return;
            default:
                throw new NotSupportedException(member.GetType().FullName);
        }
    }

    private static bool TryConvertString(string raw, Type targetType, out object? converted, out string? error)
    {
        converted = null;
        error = null;

        Type nonNullType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string trimmed = raw.Trim();

        if (nonNullType == typeof(string))
        {
            converted = trimmed;
            return true;
        }

        if (nonNullType == typeof(bool))
        {
            if (bool.TryParse(trimmed, out bool b))
            {
                converted = b;
                return true;
            }

            error = "not a bool";
            return false;
        }

        if (nonNullType == typeof(int))
        {
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
            {
                converted = i;
                return true;
            }

            error = "not an int";
            return false;
        }

        if (nonNullType == typeof(long))
        {
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            {
                converted = l;
                return true;
            }

            error = "not a long";
            return false;
        }

        if (nonNullType == typeof(float))
        {
            if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                converted = f;
                return true;
            }

            error = "not a float";
            return false;
        }

        if (nonNullType == typeof(double))
        {
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                converted = d;
                return true;
            }

            error = "not a double";
            return false;
        }

        if (nonNullType.IsEnum)
        {
            try
            {
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                {
                    converted = Enum.ToObject(nonNullType, i);
                }
                else
                {
                    converted = Enum.Parse(nonNullType, trimmed, ignoreCase: true);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        try
        {
            converted = JsonConvert.DeserializeObject(trimmed, nonNullType);
            return converted is not null || Nullable.GetUnderlyingType(targetType) is not null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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
