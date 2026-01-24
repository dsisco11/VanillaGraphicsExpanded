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
    private const string AllCategory = "All";

    private const string CategoryDropDownKey = "category";
    private const string ViewListMenuKey = "viewlist";
    private const string ViewListScrollBarKey = "viewscroll";
    private const string ViewTitleTextKey = "viewtitle";
    private const string ViewDescriptionTextKey = "viewdesc";
    private const string ViewStatusTextKey = "viewstatus";
    private const string ViewErrorTextKey = "viewerror";
    private const string ActivateButtonKey = "activate";

    private const double DialogWidth = 920;
    private const double DialogHeight = 620;

    private const double LeftColumnWidth = 320;
    private const double ColumnGap = 20;

    private const double RowH = 30;
    private const double GapY = 10;

    private readonly DebugViewRegistry registry;
    private readonly DebugViewController controller;

    private string selectedCategory = AllCategory;
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

        double rightX = LeftColumnWidth + ColumnGap;
        double rightW = Math.Max(1, DialogWidth - rightX);

        string[] categories = BuildCategoryList(registry.GetAll(), AllCategory);
        int selectedCategoryIndex = Math.Max(0, Array.IndexOf(categories, selectedCategory));

        DebugViewDefinition[] filteredViews = GetFilteredViews(selectedCategory);
        int selectedViewIndex = GetSelectedViewIndex(filteredViews);
        DebugViewDefinition? selectedView = selectedViewIndex >= 0 && selectedViewIndex < filteredViews.Length ? filteredViews[selectedViewIndex] : null;

        EnsurePanelForSelection(selectedView);

        // Main layout
        const double titleBarH = 30;
        ElementBounds catLabelBounds = ElementBounds.Fixed(0, titleBarH, LeftColumnWidth, RowH);
        ElementBounds catDropBounds = ElementBounds.Fixed(0, titleBarH + RowH, LeftColumnWidth, RowH);

        const double scrollBarW = 20;
        const double scrollBarGap = 7;
        double listY = titleBarH + RowH * 2 + GapY;
        double listH = Math.Max(100, DialogHeight - listY);
        double listViewportW = Math.Max(1, LeftColumnWidth - scrollBarW - scrollBarGap);
        ElementBounds listClipBounds = ElementBounds.Fixed(0, listY, listViewportW, listH);
        ElementBounds listBounds = ElementBounds.Fixed(0, 0, listViewportW, listH);
        ElementBounds listScrollBarBounds = ElementBounds.Fixed(listViewportW + scrollBarGap, listY, scrollBarW, listH);

        ElementBounds titleBounds = ElementBounds.Fixed(rightX, titleBarH, rightW, RowH);
        ElementBounds statusBounds = ElementBounds.Fixed(rightX, titleBarH + RowH, rightW, RowH);
        ElementBounds descBounds = ElementBounds.Fixed(rightX, titleBarH + RowH * 2, rightW, 120);

        ElementBounds buttonBounds = ElementBounds.Fixed(rightX, titleBarH + RowH * 2 + 120 + GapY, 160, RowH);
        ElementBounds errorBounds = ElementBounds.Fixed(rightX, titleBarH + RowH * 2 + 120 + GapY + RowH + GapY, rightW, 60);

        double panelY = titleBarH + RowH * 2 + 120 + GapY + RowH + GapY + 60 + GapY;
        double panelH = Math.Max(0, DialogHeight - panelY);
        ElementBounds panelTitleBounds = ElementBounds.Fixed(rightX, panelY, rightW, RowH);
        ElementBounds panelParentBounds = ElementBounds.Fixed(rightX, panelY + RowH + GapY, rightW, Math.Max(0, panelH - RowH - GapY));
        ElementBounds panelContentBounds = ElementBounds.Fixed(0, 0, panelParentBounds.fixedWidth, panelParentBounds.fixedHeight);

        string activateText = selectedView is null
            ? "Activate"
            : BuildActivateButtonText(selectedView, controller.IsActive(selectedView.Id));
        bool activateEnabled = selectedView is not null && selectedView.GetAvailability(CreateContext()).IsAvailable;

        SingleComposer?.Dispose();
        var composer = capi.Gui
            .CreateCompo("vge-debug-viewer", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar("VGE Debug Viewer", OnTitleBarClose)
            .BeginChildElements(bgBounds)
                // Left column: category + view list
                .AddStaticText("Category", fontLabel, catLabelBounds)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        categories,
                        categories,
                        selectedCategoryIndex,
                        OnCategoryChanged,
                        catDropBounds,
                        fontSmall),
                    CategoryDropDownKey)
                .BeginClip(listClipBounds)
                    .AddCellList(
                        listBounds,
                        OnRequireViewCell,
                        filteredViews,
                        ViewListMenuKey)
                .EndClip()
                .AddVerticalScrollbar(OnViewListScroll, listScrollBarBounds, ViewListScrollBarKey)

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

        try
        {
            var list = SingleComposer.GetCellList<DebugViewDefinition>(ViewListMenuKey);
            list.UnscaledCellVerPadding = 0;
            list.unscaledCellSpacing = 5;
            list.BeforeCalcBounds();

            var sb = SingleComposer.GetScrollbar(ViewListScrollBarKey);
            sb.SetHeights((float)listClipBounds.fixedHeight, (float)list.Bounds.fixedHeight);
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

        DebugViewDefinition[] filtered = GetFilteredViews(selectedCategory);
        int idx = GetSelectedViewIndex(filtered);
        DebugViewDefinition? selected = idx >= 0 && idx < filtered.Length ? filtered[idx] : null;

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

    internal static string[] BuildCategoryList(DebugViewDefinition[] all, string allCategory)
    {
        if (all.Length == 0)
        {
            return [allCategory];
        }

        var unique = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [allCategory] = allCategory
        };

        foreach (var v in all)
        {
            if (string.IsNullOrWhiteSpace(v.Category))
            {
                continue;
            }

            if (!unique.ContainsKey(v.Category))
            {
                unique[v.Category] = v.Category;
            }
        }

        string[] cats = unique.Values.ToArray();
        Array.Sort(cats, (a, b) =>
        {
            if (string.Equals(a, allCategory, StringComparison.OrdinalIgnoreCase)) return -1;
            if (string.Equals(b, allCategory, StringComparison.OrdinalIgnoreCase)) return 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        return cats;
    }

    private DebugViewDefinition[] GetFilteredViews(string category)
    {
        DebugViewDefinition[] all = registry.GetAll();
        if (all.Length == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(category) || string.Equals(category, AllCategory, StringComparison.OrdinalIgnoreCase))
        {
            return all;
        }

        return all.Where(v => string.Equals(v.Category, category, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private int GetSelectedViewIndex(DebugViewDefinition[] filtered)
    {
        if (filtered.Length == 0)
        {
            selectedViewId = null;
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(selectedViewId))
        {
            for (int i = 0; i < filtered.Length; i++)
            {
                if (string.Equals(filtered[i].Id, selectedViewId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        selectedViewId = filtered[0].Id;
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

    private void OnCategoryChanged(string code, bool selected)
    {
        if (!selected)
        {
            return;
        }

        selectedCategory = string.IsNullOrWhiteSpace(code) ? AllCategory : code;
        lastError = null;
        Compose();
    }

    private IGuiElementCell OnRequireViewCell(DebugViewDefinition view, ElementBounds bounds)
    {
        string label = BuildListEntryName(view);
        bool isSelected = string.Equals(view.Id, selectedViewId, StringComparison.Ordinal);
        return new DebugViewCellEntry(
            capi,
            bounds,
            label,
            isSelected,
            () =>
            {
                selectedViewId = view.Id;
                lastError = null;
                Compose();
            });
    }

    private void OnViewListScroll(float value)
    {
        if (SingleComposer is null)
        {
            return;
        }

        try
        {
            var list = SingleComposer.GetCellList<DebugViewDefinition>(ViewListMenuKey);
            list.Bounds.fixedY = 0 - value;
            list.Bounds.CalcWorldBounds();
        }
        catch
        {
            // Ignore.
        }
    }

    private bool OnActivateClicked()
    {
        if (string.IsNullOrWhiteSpace(selectedViewId))
        {
            return true;
        }

        DebugViewDefinition[] filtered = GetFilteredViews(selectedCategory);
        DebugViewDefinition? selected = filtered.FirstOrDefault(v => string.Equals(v.Id, selectedViewId, StringComparison.Ordinal));

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

    private sealed class DebugViewCellEntry : GuiElement, IGuiElementCell
    {
        private readonly Action onClicked;
        private readonly GuiElementRichtext labelElem;
        private LoadedTexture hoverTexture;

        private bool composed;
        private bool selected;

        ElementBounds IGuiElementCell.Bounds => Bounds;

        public bool Selected
        {
            get => selected;
            set => selected = value;
        }

        public DebugViewCellEntry(
            ICoreClientAPI capi,
            ElementBounds bounds,
            string label,
            bool selected,
            Action onClicked)
            : base(capi, bounds)
        {
            this.onClicked = onClicked;
            this.selected = selected;

            var font = CairoFont.WhiteSmallText();
            double offY = Math.Max(0, (RowH - font.UnscaledFontsize) / 2);
            ElementBounds labelBounds = ElementBounds.Fixed(0, offY, bounds.fixedWidth, RowH).WithParent(Bounds);
            labelElem = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, label, font), labelBounds);
            hoverTexture = new LoadedTexture(capi);

            MouseOverCursor = "hand";
        }

        public void Recompose()
        {
            composed = true;
            labelElem.Compose();

            using var surface = new ImageSurface(Cairo.Format.Argb32, 2, 2);
            using var ctx = genContext(surface);
            ctx.NewPath();
            ctx.LineTo(0, 0);
            ctx.LineTo(2, 0);
            ctx.LineTo(2, 2);
            ctx.LineTo(0, 2);
            ctx.ClosePath();
            ctx.SetSourceRGBA(0, 0, 0, 0.15);
            ctx.Fill();
            generateTexture(surface, ref hoverTexture);
        }

        public void OnRenderInteractiveElements(ICoreClientAPI api, float deltaTime)
        {
            if (!composed)
            {
                Recompose();
            }

            labelElem.RenderInteractiveElements(deltaTime);

            bool hover = Bounds.PositionInside(api.Input.MouseX, api.Input.MouseY) != null && IsPositionInside(api.Input.MouseX, api.Input.MouseY);
            if (selected || hover)
            {
                api.Render.Render2DTexturePremultipliedAlpha(hoverTexture.TextureId, Bounds.absX, Bounds.absY, Bounds.OuterWidth, Bounds.OuterHeight);
            }
        }

        public void UpdateCellHeight()
        {
            Bounds.CalcWorldBounds();
            labelElem.BeforeCalcBounds();
            Bounds.fixedHeight = RowH;
        }

        public void OnMouseUpOnElement(MouseEvent args, int elementIndex)
        {
            if (!args.Handled)
            {
                onClicked();
            }
        }

        public void OnMouseDownOnElement(MouseEvent args, int elementIndex) { }

        public void OnMouseMoveOnElement(MouseEvent args, int elementIndex) { }

        public override void Dispose()
        {
            labelElem.Dispose();
            hoverTexture.Dispose();
            base.Dispose();
        }
    }
}
