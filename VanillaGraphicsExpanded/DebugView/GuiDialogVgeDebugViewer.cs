using System;
using System.Collections.Generic;
using System.Linq;

using Cairo;

using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

public sealed class GuiDialogVgeDebugViewer : GuiDialog
{
    private const string ViewDropDownKey = "view";
    private const string ViewTitleTextKey = "viewtitle";
    private const string ViewDescriptionTextKey = "viewdesc";
    private const string ViewStatusTextKey = "viewstatus";
    private const string ViewErrorTextKey = "viewerror";
    private const string ActivateButtonKey = "activate";

    private const double DialogWidth = 520;
    private const double DialogHeight = 680;

    private const double RowH = 30;
    private const double GapY = 10;

    private readonly DebugViewRegistry registry;
    private readonly DebugViewController controller;

    private string? selectedViewId;

    private IDebugViewPanel? panel;
    private string? panelViewId;
    private long panelTickListenerId;

    private string? lastError;

    public GuiDialogVgeDebugViewer(
        ICoreClientAPI capi,
        DebugViewRegistry? registry = null,
        DebugViewController? controller = null)
        : base(capi)
    {
        this.registry = registry ?? DebugViewRegistry.Instance;
        this.controller = controller ?? DebugViewController.Instance;

        Compose();
    }

    public override string ToggleKeyCombinationCode => null!;

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        controller.Initialize(CreateContext());

        registry.Changed += OnRegistryChanged;
        controller.StateChanged += OnControllerStateChanged;

        Compose();

        SingleComposer?.FocusElement(0);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        registry.Changed -= OnRegistryChanged;
        controller.StateChanged -= OnControllerStateChanged;

        StopPanelTick();
        panel?.OnClosed();
    }

    public override void Dispose()
    {
        try
        {
            registry.Changed -= OnRegistryChanged;
            controller.StateChanged -= OnControllerStateChanged;
        }
        catch
        {
            // Ignore during shutdown.
        }

        try
        {
            panel?.Dispose();
        }
        catch
        {
            // Ignore during shutdown.
        }
        finally
        {
            panel = null;
            panelViewId = null;
            StopPanelTick();
        }

        base.Dispose();
    }

    public override void OnKeyDown(KeyEvent args)
    {
        base.OnKeyDown(args);

        if (args.Handled)
        {
            return;
        }

        if (args.KeyCode == (int)GlKeys.Escape)
        {
            TryClose();
            args.Handled = true;
            return;
        }

        if (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
        {
            _ = OnActivateClicked();
            args.Handled = true;
        }
    }

    private DebugViewActivationContext CreateContext()
        => new DebugViewActivationContext(capi, ConfigModSystem.Config);

    private void Compose()
    {
        // Follow Vintage Story dialog pattern: bg fills and sizes to a fixed content panel.
        // This ensures child elements have a stable parent bounds tree (and avoids mis-scaled bounds).
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(contentBounds);

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 0);

        var fontLabel = CairoFont.WhiteDetailText();
        var fontSmall = CairoFont.WhiteSmallText();

        DebugViewDefinition[] views = registry.GetAll();
        RestoreSelectedViewId(views);
        int selectedViewIndex = GetSelectedViewIndex(views);
        DebugViewDefinition? selectedView = selectedViewIndex >= 0 && selectedViewIndex < views.Length ? views[selectedViewIndex] : null;

        EnsurePanelForSelection(selectedView);

        // Main layout (single column)
        const double titleBarH = 30;
        const double labelW = 120;
        const double gapX = 10;

        double contentW = DialogWidth;
        double y = titleBarH;

        ElementBounds catLabelBounds = ElementBounds.Fixed(0, y, labelW, RowH);
        ElementBounds catDropBounds = ElementBounds.Fixed(labelW + gapX, y, Math.Max(1, contentW - labelW - gapX), RowH);
        y += RowH + GapY;

        ElementBounds titleBounds = ElementBounds.Fixed(0, y, contentW, RowH);
        y += RowH;
        ElementBounds statusBounds = ElementBounds.Fixed(0, y, contentW, RowH);
        y += RowH;
        ElementBounds descBounds = ElementBounds.Fixed(0, y, contentW, RowH);
        y += RowH + GapY;

        ElementBounds buttonBounds = ElementBounds.Fixed(0, y, 160, RowH);
        y += RowH + GapY;
        ElementBounds errorBounds = ElementBounds.Fixed(0, y, contentW, 60);
        y += 60 + GapY;

        double panelY = y;
        double panelH = Math.Max(0, DialogHeight - panelY);
        ElementBounds panelTitleBounds = ElementBounds.Fixed(0, panelY, contentW, RowH);
        ElementBounds panelParentBounds = ElementBounds.Fixed(0, panelY + RowH + GapY, contentW, Math.Max(0, panelH - RowH - GapY));
        ElementBounds panelContentBounds = ElementBounds.Fixed(0, 0, panelParentBounds.fixedWidth, panelParentBounds.fixedHeight);

        string activateText = selectedView is null
            ? "Activate"
            : BuildActivateButtonText(selectedView, controller.IsActive(selectedView.Id));
        bool activateEnabled = selectedView is not null && selectedView.GetAvailability(CreateContext()).IsAvailable;

        string[] viewIds = views.Select(v => v.Id).ToArray();
        string[] viewNames = views.Select(BuildListEntryName).ToArray();

        SingleComposer?.Dispose();
        var composer = capi.Gui
            .CreateCompo("vge-debug-viewer", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar("VGE Debug Viewer", OnTitleBarClose)
            .BeginChildElements(bgBounds)
                // Left column: debug view selector
                .AddStaticText("Debug View", fontLabel, catLabelBounds)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        viewIds,
                        viewNames,
                        Math.Max(0, selectedViewIndex),
                        OnViewChanged,
                        catDropBounds,
                        fontSmall),
                    ViewDropDownKey)

                // Right column: selection details
                .AddDynamicText("", fontLabel, titleBounds, ViewTitleTextKey)
                .AddDynamicText("", fontSmall, statusBounds, ViewStatusTextKey)
                .AddDynamicText("", fontSmall, descBounds, ViewDescriptionTextKey)
                .AddSmallButton(activateText, OnActivateClicked, buttonBounds, EnumButtonStyle.Small, ActivateButtonKey)
                .AddDynamicText("", fontSmall, errorBounds, ViewErrorTextKey)
                .AddStaticText("Options", fontLabel, panelTitleBounds)
        ;

        composer
            .BeginChildElements(panelParentBounds)
            .BeginChildElements(panelContentBounds);

        panel?.Compose(composer, panelContentBounds, keyPrefix: "panel");

        composer
            .EndChildElements()
            .EndChildElements();

        SingleComposer = composer
            .EndChildElements()
            .Compose();

        if (IsOpened())
        {
            TryStartPanelTick();
        }

        RefreshFromState();

        try
        {
            var btn = SingleComposer.GetButton(ActivateButtonKey);
            btn.Enabled = activateEnabled;
        }
        catch
        {
            // Ignore.
        }
    }

    private void RefreshFromState()
    {
        if (SingleComposer is null)
        {
            return;
        }

        DebugViewDefinition[] views = registry.GetAll();
        RestoreSelectedViewId(views);
        int idx = GetSelectedViewIndex(views);
        DebugViewDefinition? selected = idx >= 0 && idx < views.Length ? views[idx] : null;

        var title = SingleComposer.GetDynamicText(ViewTitleTextKey);
        var status = SingleComposer.GetDynamicText(ViewStatusTextKey);
        var desc = SingleComposer.GetDynamicText(ViewDescriptionTextKey);
        var err = SingleComposer.GetDynamicText(ViewErrorTextKey);
        var activateBtn = SingleComposer.GetButton(ActivateButtonKey);

        if (selected is null)
        {
            title.SetNewText("(No debug view selected)");
            status.SetNewText(string.Empty);
            desc.SetNewText(string.Empty);
            err.SetNewText(string.Empty);
            activateBtn.Enabled = false;
            return;
        }

        bool isActive = controller.IsActive(selected.Id);
        DebugViewAvailability availability = selected.GetAvailability(CreateContext());

        title.SetNewText(selected.Name);
        desc.SetNewText(string.IsNullOrWhiteSpace(selected.Description) ? "(No description)" : selected.Description);

        string availabilityText = availability.IsAvailable ? "Available" : $"Unavailable: {availability.Reason}";
        string activeText = selected.ActivationMode == DebugViewActivationMode.Toggle
            ? (isActive ? "Enabled" : "Disabled")
            : (isActive ? "Active" : "Inactive");
        status.SetNewText($"{selected.Category} | {activeText} | {availabilityText}");

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            err.SetNewText($"Error: {lastError}");
        }
        else
        {
            err.SetNewText(string.Empty);
        }

        activateBtn.Enabled = availability.IsAvailable;
    }

    private void RestoreSelectedViewId(DebugViewDefinition[] views)
    {
        if (views.Length == 0)
        {
            selectedViewId = null;
            return;
        }

        string? activeExclusive = controller.ActiveExclusiveViewId;
        if (!string.IsNullOrWhiteSpace(activeExclusive) && views.Any(v => string.Equals(v.Id, activeExclusive, StringComparison.Ordinal)))
        {
            selectedViewId = activeExclusive;
            return;
        }

        foreach (string id in controller.GetActiveToggleViewIds())
        {
            if (views.Any(v => string.Equals(v.Id, id, StringComparison.Ordinal)))
            {
                selectedViewId = id;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedViewId) && views.Any(v => string.Equals(v.Id, selectedViewId, StringComparison.Ordinal)))
        {
            return;
        }

        selectedViewId = views[0].Id;
    }

    private int GetSelectedViewIndex(DebugViewDefinition[] views)
    {
        if (views.Length == 0)
        {
            selectedViewId = null;
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(selectedViewId))
        {
            for (int i = 0; i < views.Length; i++)
            {
                if (string.Equals(views[i].Id, selectedViewId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        selectedViewId = views[0].Id;
        return 0;
    }

    private string BuildListEntryName(DebugViewDefinition view)
    {
        bool isActive = controller.IsActive(view.Id);
        DebugViewAvailability availability = view.GetAvailability(CreateContext());

        string activePrefix = isActive ? "[*] " : "    ";
        string unavailableSuffix = availability.IsAvailable ? string.Empty : " (unavailable)";

        return $"{activePrefix}{view.Name}{unavailableSuffix}";
    }

    private static string BuildActivateButtonText(DebugViewDefinition view, bool isActive)
    {
        if (view.ActivationMode == DebugViewActivationMode.Toggle)
        {
            return isActive ? "Disable" : "Enable";
        }

        return isActive ? "Deactivate" : "Activate";
    }

    private void EnsurePanelForSelection(DebugViewDefinition? selectedView)
    {
        string? id = selectedView?.Id;
        if (string.Equals(panelViewId, id, StringComparison.Ordinal))
        {
            return;
        }

        if (panel is not null)
        {
            if (IsOpened())
            {
                panel.OnClosed();
            }

            panel.Dispose();
        }

        panel = null;
        panelViewId = null;
        StopPanelTick();

        if (selectedView?.CreatePanel is null)
        {
            return;
        }

        try
        {
            panel = selectedView.CreatePanel(CreateContext());
            panelViewId = selectedView.Id;

            if (IsOpened() && panel is not null)
            {
                panel.OnOpened();
                TryStartPanelTick();
            }
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            panel = null;
            panelViewId = null;
        }
    }

    private void TryStartPanelTick()
    {
        if (panelTickListenerId != 0)
        {
            return;
        }

        if (panel is null || !panel.WantsGameTick)
        {
            return;
        }

        panelTickListenerId = capi.Event.RegisterGameTickListener(dt =>
        {
            try
            {
                panel?.OnGameTick(dt);
            }
            catch
            {
                // Ignore panel tick exceptions.
            }
        }, 200);
    }

    private void StopPanelTick()
    {
        if (panelTickListenerId == 0)
        {
            return;
        }

        capi.Event.UnregisterGameTickListener(panelTickListenerId);
        panelTickListenerId = 0;
    }

    private void OnRegistryChanged()
    {
        if (!IsOpened())
        {
            return;
        }

        capi.Event.EnqueueMainThreadTask(() =>
        {
            if (IsOpened())
            {
                Compose();
            }
        }, "vge:debugviewer:registry-changed");
    }

    private void OnControllerStateChanged()
    {
        if (!IsOpened())
        {
            return;
        }

        capi.Event.EnqueueMainThreadTask(() =>
        {
            if (IsOpened())
            {
                Compose();
            }
        }, "vge:debugviewer:state-changed");
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private void OnViewChanged(string code, bool selected)
    {
        if (!selected)
        {
            return;
        }

        selectedViewId = string.IsNullOrWhiteSpace(code) ? null : code;
        lastError = null;
        Compose();
    }

    private bool OnActivateClicked()
    {
        if (string.IsNullOrWhiteSpace(selectedViewId))
        {
            return true;
        }

        if (!registry.TryGet(selectedViewId, out DebugViewDefinition? selected) || selected is null)
        {
            return true;
        }

        if (selected is null)
        {
            return true;
        }

        bool isActive = controller.IsActive(selected.Id);

        if (selected.ActivationMode == DebugViewActivationMode.Exclusive && isActive)
        {
            _ = controller.TryDeactivateExclusive(out _);
            lastError = null;
            Compose();
            return true;
        }

        bool ok = controller.TryActivate(selected.Id, out string? err);
        lastError = ok ? null : err;
        Compose();
        return true;
    }
}
