using Dalamud.Interface;
using Dalamud.Utility;

namespace CustomizePlus.Core.Helpers;

public static class CtrlHelper
{
    /// <summary>
    /// Gets the width of an icon button, checkbox, etc...
    /// </summary>
    /// per https://github.com/ocornut/imgui/issues/3714#issuecomment-759319268
    public static float IconButtonWidth => Im.Style.FrameHeight + 2 * Im.Style.ItemInnerSpacing.X;

    public static bool TextBox(string label, ref string value)
    {
        Im.Item.SetNextWidthFull();
        return Im.Input.Text(label, ref value, maxLength: 1024);
    }

    public static bool TextPropertyBox(string label, Func<string> get, Action<string> set)
    {
        var temp = get();
        var result = TextBox(label, ref temp);
        if (result)
        {
            set(temp);
        }

        return result;
    }

    public static bool Checkbox(string label, ref bool value)
    {
        return Im.Checkbox(label, ref value);
    }

    public static bool CheckboxWithTextAndHelp(string label, string text, string helpText, ref bool value)
    {
        var checkBoxState = Im.Checkbox(label, ref value);
        LunaStyle.DrawHelpMarkerLabel(text, helpText);

        return checkBoxState;
    }

    public static bool CheckboxToggle(string label, in bool shown, Action<bool> toggle)
    {
        var temp = shown;
        var toggled = Im.Checkbox(label, ref temp);

        if (toggled)
        {
            toggle(temp);
        }

        return toggled;
    }

    public static bool ArrowToggle(string label, ref bool value)
    {
        if (Im.ArrowButton(label, value ? Direction.Down : Direction.Right))
            value = !value;

        return value;
    }


    public static void AddHoverText(string text)
    {
        Im.Tooltip.OnHover(text);
    }

    public enum TextAlignment { Left, Center, Right };
    public static void StaticLabel(string? text, TextAlignment align = TextAlignment.Left, string tooltip = "")
    {
        if (text != null)
        {
            if (align == TextAlignment.Center)
            {
                Im.Dummy((Im.ContentRegion.Available.X - Im.Font.CalculateSize(text).X) / 2, 0);
                Im.Line.Same();
            }
            else if (align == TextAlignment.Right)
            {
                Im.Dummy(Im.ContentRegion.Available.X - Im.Font.CalculateSize(text).X, 0);
                Im.Line.Same();
            }

            Im.Text(text);
            if (!tooltip.IsNullOrWhitespace())
            {
                AddHoverText(tooltip);
            }
        }
    }

    public static void LabelWithIcon(FontAwesomeIcon icon, string text, bool isSameLine = true)
    {
        if (isSameLine)
            Im.Line.Same();
        icon.Draw();
        Im.Line.Same();
        Im.TextWrapped(text);
    }
}