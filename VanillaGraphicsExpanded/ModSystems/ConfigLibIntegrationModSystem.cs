using System;
using System.Linq;
using System.Reflection;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>
/// Registers VGE's managed configuration with ConfigLib when ConfigLib is installed.
/// ConfigLib remains an optional dependency.
/// </summary>
internal sealed class ConfigLibIntegrationModSystem : ModSystem
{
    private ICoreAPI? api;
    private bool registered;

    public override double ExecuteOrder() => 0.0;

    public override void StartPre(ICoreAPI api)
    {
        this.api = api;
        ConfigModSystem.EnsureConfigLoaded(api);
        TryRegisterConfigWithConfigLib();
    }

    private void TryRegisterConfigWithConfigLib()
    {
        if (registered || api is null || !api.ModLoader.IsModEnabled("configlib"))
        {
            return;
        }

        ModSystem? configLib = api.ModLoader.GetModSystem("ConfigLib.ConfigLibModSystem");
        if (configLib is null)
        {
            api.Logger.Debug("[VGE] ConfigLib is enabled but its mod system was not found.");
            return;
        }

        MethodInfo? registerMethod = configLib.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method =>
            {
                if (method.Name != "RegisterCustomManagedConfig") return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length >= 2
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(object);
            });

        if (registerMethod is null)
        {
            api.Logger.Debug("[VGE] ConfigLib registration API was not found.");
            return;
        }

        try
        {
            ParameterInfo[] parameters = registerMethod.GetParameters();
            object?[] arguments = new object?[parameters.Length];
            arguments[0] = Constants.ModId;
            arguments[1] = ConfigModSystem.Config;

            if (parameters.Length >= 3) arguments[2] = Constants.ConfigFileName;
            if (parameters.Length >= 4) arguments[3] = null;
            if (parameters.Length >= 5) arguments[4] = null;
            if (parameters.Length >= 6)
            {
                arguments[5] = (Action)(() =>
                {
                    if (api is null) return;
                    ConfigModSystem.Config.Sanitize();
                    LiveConfigReload.NotifyAll(api);
                    ConfigModSystem.PersistConfigAndNotifyReloadRequired(api, "ConfigLib");
                });
            }

            registerMethod.Invoke(configLib, arguments);
            registered = true;
            api.Logger.Debug("[VGE] Registered managed configuration with optional ConfigLib.");
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VGE] Failed to register managed configuration with ConfigLib: {0}", ex);
        }
    }
}