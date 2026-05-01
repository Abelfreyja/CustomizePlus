using Dalamud.Interface.Utility;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using ImSharp;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs;

public static class HeaderDrawer
{
    public struct Button
    {
        public static readonly Button Invisible = new()
        {
            Visible = false,
            Width = 0,
        };

        public Action? OnClick;
        public string Description = string.Empty;
        public float Width;
        public uint BorderColor;
        public uint TextColor;
        public FontAwesomeIcon Icon;
        public bool Disabled;
        public bool Visible;

        public Button()
        {
            Visible = true;
            Width = ImGui.GetFrameHeightWithSpacing();
            BorderColor = ColorId.HeaderButtons.Value();
            TextColor = ColorId.HeaderButtons.Value();
            Disabled = false;
        }

        public readonly void Draw()
        {
            if (!Visible)
                return;

            using var color = ImGuiColor.Border.Push(BorderColor)
                .Push(ImGuiColor.Text, TextColor, TextColor != 0);
            if (UiHelpers.DrawIconButton(Icon, new Vector2(Width, ImGui.GetFrameHeight()), string.Empty, Disabled))
                OnClick?.Invoke();
            color.Pop();
            UiHelpers.DrawHoverTooltip(Description);
        }

        public static Button IncognitoButton(bool current, Action<bool> setter)
            => current
                ? new Button
                {
                    Description = "Toggle incognito mode off.",
                    Icon = FontAwesomeIcon.EyeSlash,
                    OnClick = () => setter(false),
                }
                : new Button
                {
                    Description = "Toggle incognito mode on.",
                    Icon = FontAwesomeIcon.Eye,
                    OnClick = () => setter(true),
                };
    }

    public static void Draw(string text, uint textColor, uint frameColor, int leftButtons, params Button[] buttons)
    {
        using var style = Im.Style.Push(ImStyleDouble.ItemSpacing, Vector2.Zero)
            .Push(ImStyleSingle.FrameRounding, 0)
            .Push(ImStyleSingle.FrameBorderThickness, ImGuiHelpers.GlobalScale);

        var leftButtonSize = 0f;
        foreach (var button in buttons.Take(leftButtons).Where(b => b.Visible))
        {
            button.Draw();
            ImGui.SameLine();
            leftButtonSize += button.Width;
        }

        var rightButtonSize = buttons.Length > leftButtons ? buttons.Skip(leftButtons).Where(b => b.Visible).Select(b => b.Width).Sum() : 0f;
        var midSize = ImGui.GetContentRegionAvail().X - rightButtonSize - ImGuiHelpers.GlobalScale;

        style.Pop();
        style.Push(ImStyleDouble.ButtonTextAlign, new Vector2(0.5f + (rightButtonSize - leftButtonSize) / midSize, 0.5f));
        if (textColor != 0)
            UiHelpers.DrawColoredButton(text, new Vector2(midSize, ImGui.GetFrameHeight()), frameColor, textColor);
        else
            UiHelpers.DrawColoredButton(text, new Vector2(midSize, ImGui.GetFrameHeight()), frameColor);
        style.Pop();
        style.Push(ImStyleSingle.FrameBorderThickness, ImGuiHelpers.GlobalScale);

        foreach (var button in buttons.Skip(leftButtons).Where(b => b.Visible))
        {
            ImGui.SameLine();
            button.Draw();
        }
    }
}
