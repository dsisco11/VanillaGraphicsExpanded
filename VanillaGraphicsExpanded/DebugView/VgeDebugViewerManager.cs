using System;

using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.DebugView;

public static class VgeDebugViewerManager
{
    private static ICoreClientAPI? capi;

    private static GuiDialogVgeDebugViewer? dialog;

    public static void Initialize(ICoreClientAPI capi)
    {
        VgeDebugViewerManager.capi = capi ?? throw new ArgumentNullException(nameof(capi));

        DebugViewController.Instance.Initialize(new DebugViewActivationContext(capi, ConfigModSystem.Config));
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

            DebugViewController.Instance.Dispose();
        }
    }

    public static bool ToggleDialog(KeyCombination _)
    {
        if (capi is null)
        {
            return false;
        }

        dialog ??= new GuiDialogVgeDebugViewer(capi);

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
}
