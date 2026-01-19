using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

internal sealed class GuiElementDropDownCycleOnArrow : GuiElementDropDown
{
    public GuiElementDropDownCycleOnArrow(
        ICoreClientAPI capi,
        string[] values,
        string[] names,
        int selectedIndex,
        SelectionChangedDelegate onSelectionChanged,
        ElementBounds bounds,
        CairoFont font)
        : base(capi, values, names, selectedIndex, onSelectionChanged, bounds, font, multiSelect: false)
    {
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus) return;

        var menu = listMenu;

        if (menu is not null && (args.KeyCode == (int)GlKeys.Up || args.KeyCode == (int)GlKeys.Down) && !menu.IsOpened)
        {
            int valuesLength = menu.Values?.Length ?? 0;
            if (valuesLength > 0)
            {
                int delta = args.KeyCode == (int)GlKeys.Up ? -1 : 1;
                int next = GameMath.Mod(menu.SelectedIndex + delta, valuesLength);
                SetSelectedIndex(next);
                args.Handled = true;
                onSelectionChanged?.Invoke(SelectedValue, true);
                return;
            }
        }

        base.OnKeyDown(api, args);
    }
}
