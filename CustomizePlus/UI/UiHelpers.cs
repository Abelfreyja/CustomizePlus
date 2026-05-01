using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace CustomizePlus.UI;

public static class UiHelpers
{
    /// <summary> Vertical spacing between groups. </summary>
    public static Vector2 DefaultSpace;

    /// <summary> Multiples of the current Global Scale </summary>
    public static float Scale;

    /// <summary> Draw default vertical space. </summary>
    public static void DefaultLineSpace()
        => ImGui.Dummy(DefaultSpace);

    public static void SetupCommonSizes()
    {
        if (ImGuiHelpers.GlobalScale != Scale)
        {
            Scale = ImGuiHelpers.GlobalScale;
            DefaultSpace = new Vector2(0, 10 * Scale);
        }
    }

    public static bool DrawDisabledButton(string label, Vector2 size, string tooltip, bool disabled)
        => ImEx.Button(label, size, tooltip, disabled);

    public static bool DrawIconButton(FontAwesomeIcon icon, Vector2 size, string tooltip, bool disabled)
        => ImEx.Icon.Button(icon.Icon(), tooltip, disabled, size);

    public static void DrawColoredButton(string label, Vector2 size, uint frameColor, uint textColor = 0)
        => ImEx.Button(label, frameColor, textColor, size);

    public static void DrawPropertyLabel(string text)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
    }

    public static bool DrawNamePopup(string label, ref string name)
    {
        using var popup = Im.Popup.Begin(label);
        if (!popup)
            return false;

        ImGui.InputTextWithHint("##name", "Name...", ref name, 128);
        if (!ImGui.Button("Create") || name.Length == 0)
            return false;

        Im.Popup.CloseCurrent();
        return true;
    }

    public static void DrawIcon(FontAwesomeIcon icon)
        => icon.Draw();

    public static void DrawHoverTooltip(string text)
    {
        if (text.Length > 0)
            Im.Tooltip.OnHover(text);
    }

    public static unsafe bool IsDragDropPayload(string label)
        => ImGui.AcceptDragDropPayload(label).Handle != null;
}
