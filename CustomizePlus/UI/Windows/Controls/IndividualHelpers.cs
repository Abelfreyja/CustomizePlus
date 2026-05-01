using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Bindings.ImGui;

namespace CustomizePlus.UI.Windows.Controls;

public static class IndividualHelpers
{
    public static bool DrawObjectKindCombo(float width, ObjectKind current, out ObjectKind result, IEnumerable<ObjectKind> kinds)
    {
        result = current;
        ImGui.SetNextItemWidth(width);
        using var combo = Im.Combo.Begin("##objectKind", current.ToString());
        if (!combo)
            return false;

        var changed = false;
        foreach (var kind in kinds)
        {
            if (!ImGui.Selectable(kind.ToString(), kind == current))
                continue;

            result = kind;
            changed = true;
        }

        return changed;
    }
}



