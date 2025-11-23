using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.Internal;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class EmoteSelector
{
    private const float IconColumnExtraWidth = 8f;

    private readonly EmoteService _emoteService;
    private readonly ITextureProvider _textureProvider;
    private readonly List<EmoteService.EmoteEntry> _emotes;

    private string _search = string.Empty;
    private EmoteService.EmoteEntry? _selectedEmote;

    public EmoteService.EmoteEntry? SelectedEmote => _selectedEmote;

    public EmoteSelector(EmoteService emoteService, ITextureProvider textureProvider)
    {
        _emoteService = emoteService;
        _textureProvider = textureProvider;
        _emotes = _emoteService.GetEmotes().ToList();
    }

    public void Draw(float iconSize = 32f)
    {
        try
        {
            DrawInternal(iconSize);
        }
        catch (ObjectDisposedException)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
            ImGui.TextUnformatted("Emote icons unavailable (textures disposed).");
            ImGui.PopStyleColor();
        }
    }

    private void DrawInternal(float iconSize)
    {
        if (_emotes.Count == 0)
        {
            ImGui.TextUnformatted("No emotes available.");
            return;
        }

        var regionAvail = ImGui.GetContentRegionAvail();
        var searchWidth = Math.Max(160f, regionAvail.X - 110f);

        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##EmoteSearch", "Search emotes...", ref _search, 80);

        List<EmoteService.EmoteEntry> filtered = string.IsNullOrWhiteSpace(_search)
            ? _emotes
            : _emotes
                .Where(e =>
                    e.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                    e.Id.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ImGui.SameLine();
        ImGui.TextDisabled($"{filtered.Count:#,0}/{_emotes.Count:#,0}");

        ImGui.BeginChild("EmoteList", new Vector2(0f, 220f), true, ImGuiWindowFlags.HorizontalScrollbar);

        if (filtered.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.TextWrapped("No emotes match your search.");
            ImGui.PopStyleColor();
            ImGui.EndChild();
            return;
        }

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit;
        var cellPadding = new Vector2(4f, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, cellPadding);

        if (ImGui.BeginTable("EmoteSelectorTable", 2, tableFlags, new Vector2(-1f, -1f)))
        {
            ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, iconSize + IconColumnExtraWidth);
            ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);

            var style = ImGui.GetStyle();
            var lineHeight = ImGui.GetTextLineHeight();
            var rowPadding = MathF.Max(2f, style.ItemSpacing.Y * 0.75f);
            var rowHeight = MathF.Max(iconSize, lineHeight) + rowPadding * 2f;
            var rowSelectedColor = new Vector4(0.45f * 0.75f, 0.85f * 0.75f, 0.65f * 0.75f, 0.5f);
            ImGui.PushStyleColor(ImGuiCol.Header, rowSelectedColor);

            for (int i = 0; i < filtered.Count; i++)
            {
                var entry = filtered[i];
                bool selected = _selectedEmote?.Id == entry.Id;

                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                ImGui.TableSetColumnIndex(0);
                ImGui.PushID(entry.Id);

                if (ImGui.Selectable("##EmoteRow", selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight)))
                    _selectedEmote = entry;

                var rowMin = ImGui.GetItemRectMin();
                var rowMax = ImGui.GetItemRectMax();
                var contentHeight = rowMax.Y - rowMin.Y;
                var iconTop = rowMin.Y + MathF.Max(0f, (contentHeight - iconSize) * 0.5f);
                var iconPos = new Vector2(rowMin.X + cellPadding.X, iconTop);
                var iconMax = iconPos + new Vector2(iconSize);

                var iconLookup = new GameIconLookup(entry.IconId);
                ISharedImmediateTexture? iconTexture = null;
                var hasIcon = entry.IconId > 0 && TryGetGameIcon(iconLookup, out iconTexture);
                var drawList = ImGui.GetWindowDrawList();

                if (hasIcon && iconTexture!.TryGetWrap(out var wrap, out _))
                    drawList.AddImage(wrap.Handle, iconPos, iconMax);
                else
                    drawList.AddRectFilled(iconPos, iconMax, ImGui.GetColorU32(ImGuiCol.FrameBg), 4f);

                ImGui.TableSetColumnIndex(1);
                var infoCursor = ImGui.GetCursorScreenPos();
                var idLabel = $"ID: {entry.Id}";
                var rowCenterY = rowMin.Y + contentHeight * 0.5f;
                var textTop = rowCenterY - lineHeight * 0.5f;
                var textPos = new Vector2(infoCursor.X, textTop);

                ImGui.SetCursorScreenPos(textPos);
                ImGui.TextUnformatted(entry.Name);

                var nameWidth = ImGui.CalcTextSize(entry.Name).X;
                var spacing = MathF.Max(6f, style.ItemSpacing.X);
                var idPos = new Vector2(textPos.X + nameWidth + spacing, textTop);
                ImGui.SetCursorScreenPos(idPos);
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                ImGui.TextUnformatted(idLabel);
                ImGui.PopStyleColor();

                ImGui.PopID();
            }

            ImGui.PopStyleColor(1);
            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
        ImGui.EndChild();
    }

    private bool TryGetGameIcon(GameIconLookup lookup, out ISharedImmediateTexture? texture)
    {
        texture = null;

        try
        {
            texture = _textureProvider.GetFromGameIcon(lookup);
            return true;
        }
        catch (IconNotFoundException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

}
