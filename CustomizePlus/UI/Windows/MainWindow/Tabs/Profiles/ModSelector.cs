using CustomizePlus.Core.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class ModSelector
{
    private readonly ModService _modService;

    private List<string> _mods = new();
    private string _search = string.Empty;
    private string? _selectedMod;

    public string? SelectedMod => _selectedMod;

    public ModSelector(ModService modService)
    {
        _modService = modService;
        Reload();
    }

    public void Reload()
    {
        _mods = _modService.GetAvailableMods()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selectedMod = null;
    }

    public void Draw(float rowHeight = 24f)
    {
        var style = ImGui.GetStyle();
        var regionAvail = ImGui.GetContentRegionAvail();
        var frameHeight = ImGui.GetFrameHeight();
        var searchWidth = Math.Max(140f, regionAvail.X - frameHeight - style.ItemSpacing.X - 80f);

        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##ModSearch", "Search mods...", ref _search, 128);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt))
            Reload();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reload mod list");

        var filtered = string.IsNullOrWhiteSpace(_search)
            ? _mods
            : _mods.Where(name => name.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

        ImGui.SameLine();
        ImGui.TextDisabled($"{filtered.Count:#,0}/{_mods.Count:#,0}");

        ImGui.BeginChild("ModList", new Vector2(0f, 220f), true, ImGuiWindowFlags.HorizontalScrollbar);

        if (filtered.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.68f, 0.70f, 0.76f, 1f));
            ImGui.TextWrapped("No mods match your search.");
            ImGui.PopStyleColor();
            ImGui.EndChild();
            return;
        }

        var textPaddingX = 6f;
        var textPaddingY = 3f;
        var itemHeight = Math.Max(rowHeight, ImGui.GetTextLineHeight() + textPaddingY * 2f);

        var childBg = style.Colors[(int)ImGuiCol.ChildBg];
        var baseBg = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
        var hoverBg = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
        var selectedBg = ImGui.GetColorU32(new Vector4(0.55f, 0.80f, 1f, 0.5f));
        var selectedBorder = ImGui.GetColorU32(ImGuiCol.Border);
        var separatorColor = ImGui.GetColorU32(ImGuiCol.Border);
        var searchHighlightBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.56f, 0.4f, 0.8f));
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);

        for (int i = 0; i < filtered.Count; i++)
        {
            var mod = filtered[i];
            var selected = _selectedMod == mod;

            ImGui.PushID(i);
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var selectableSize = new Vector2(rowWidth, itemHeight);

            if (ImGui.Selectable("##ModSelectable", selected, ImGuiSelectableFlags.None, selectableSize))
                _selectedMod = mod;

            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();

            var fillColor = baseBg;
            if (selected)
                fillColor = selectedBg;
            else if (ImGui.IsItemHovered())
                fillColor = hoverBg;

            drawList.AddRectFilled(itemMin, itemMax, fillColor);

            if (selected)
                drawList.AddRect(itemMin, itemMax, selectedBorder);

            var textY = itemMin.Y + textPaddingY;
            var textPos = new Vector2(itemMin.X + textPaddingX, textY);

            drawList.PushClipRect(itemMin, itemMax, true);
            DrawHighlightedText(drawList, textPos, mod, _search, textColor, searchHighlightBg, textColor);
            drawList.PopClipRect();

            var availableTextWidth = itemMax.X - textPos.X - textPaddingX;
            var fullWidth = ImGui.CalcTextSize(mod).X;
            if (ImGui.IsItemHovered() && fullWidth > availableTextWidth + 1f)
                ImGui.SetTooltip(mod);

            drawList.AddLine(new Vector2(itemMin.X, itemMax.Y - 1f), new Vector2(itemMax.X, itemMax.Y - 1f), separatorColor, 1f);

            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private static void DrawHighlightedText(ImDrawListPtr drawList, Vector2 startPos, string text, string? search, uint baseColor, uint highlightBg, uint highlightTextColor)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            drawList.AddText(startPos, baseColor, text);
            return;
        }

        var matchIndex = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
        {
            drawList.AddText(startPos, baseColor, text);
            return;
        }

        var before = text[..matchIndex];
        var match = text.Substring(matchIndex, search.Length);
        var after = text[(matchIndex + search.Length)..];

        float lineHeight = ImGui.GetTextLineHeight();
        float beforeWidth = before.Length > 0 ? ImGui.CalcTextSize(before).X : 0f;
        float matchWidth = ImGui.CalcTextSize(match).X;

        var pos = startPos;
        if (before.Length > 0)
        {
            drawList.AddText(pos, baseColor, before);
            pos.X += beforeWidth;
        }

        var matchMin = new Vector2(pos.X - 2f, pos.Y - 1f);
        var matchMax = new Vector2(pos.X + matchWidth + 2f, pos.Y + lineHeight + 1f);
        drawList.AddRectFilled(matchMin, matchMax, highlightBg, 3f);
        drawList.AddText(pos, highlightTextColor, match);
        pos.X += matchWidth;

        if (after.Length > 0)
            drawList.AddText(pos, baseColor, after);
    }
}
