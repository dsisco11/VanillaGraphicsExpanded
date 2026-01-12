using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.DebugView;

public static class VgeDebugViewManager
{
    private static ICoreClientAPI? capi;
    private static LumOnConfig? lumOnConfig;

    private static LumOnDebugMode currentMode = LumOnDebugMode.Off;

    private static GuiDialogVgeDebugView? dialog;

    public static void Initialize(
        ICoreClientAPI capi,
        LumOnConfig? lumOnConfig)
    {
        VgeDebugViewManager.capi = capi;
        VgeDebugViewManager.lumOnConfig = lumOnConfig;

        ApplyModeToRuntime();
    }

    public static void Dispose()
    {
        try
        {
            dialog?.TryClose();
        }
        catch
        {
            // Ignore during shutdown.
        }
        finally
        {
            dialog = null;
            capi = null;
            lumOnConfig = null;
        }
    }

    public static bool ToggleDialog(KeyCombination _)
    {
        if (capi is null)
        {
            return false;
        }

        dialog ??= new GuiDialogVgeDebugView(capi);

        if (dialog.IsOpened())
        {
            dialog.TryClose();
        }
        else
        {
            dialog.TryOpen();
        }

        return true;
    }

    public static LumOnDebugMode GetMode() => currentMode;

    public static void SetMode(LumOnDebugMode mode)
    {
        currentMode = mode;
        ApplyModeToRuntime();
    }

    private static void ApplyModeToRuntime()
    {
        if (lumOnConfig != null)
        {
            lumOnConfig.DebugMode = currentMode;
        }
    }
}
