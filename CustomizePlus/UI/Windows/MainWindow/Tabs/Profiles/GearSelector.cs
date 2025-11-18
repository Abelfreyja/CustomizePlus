using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;


namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles
{
    public class GearSelector
    {
        private const float CellPaddingX = 4f;
        private const float CellPaddingY = 2f;
        private const float IconColumnExtraWidth = 8f;

        private readonly GearDataService _gearDataService;
        private readonly ITextureProvider _textureProvider;
        private readonly GearSlot _slot;
        private List<Item>? _items;
        private string _search = "";
        private Item? _selectedItem;

        public Item? SelectedItem => _selectedItem;

        public GearSelector(GearDataService gearDataService, ITextureProvider textureProvider, GearSlot slot)
        {
            _gearDataService = gearDataService;
            _textureProvider = textureProvider;
            _slot = slot;
            Reload();
        }

        public void Reload()
        {
            _items = _gearDataService.GetItemsForSlot(_slot)
                .OrderBy(i => i.Name.ToString())
                .ToList();
            _selectedItem = null;
        }

        public void Draw(float iconSize = 40f)
        {
            if (_items == null)
                return;

            var style = ImGui.GetStyle();
            var regionAvail = ImGui.GetContentRegionAvail();
            var searchWidth = Math.Max(160f, regionAvail.X - 110f);

            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputTextWithHint("##GearSearch", "Search gear...", ref _search, 64);

            var filtered = string.IsNullOrWhiteSpace(_search)
                ? _items
                : _items.Where(i => i.Name.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

            ImGui.SameLine();
            ImGui.TextDisabled($"{filtered.Count:#,0}/{_items.Count:#,0}");

            ImGui.BeginChild("GearList", new Vector2(0f, 320f), true, ImGuiWindowFlags.HorizontalScrollbar);

            if (filtered.Count == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                ImGui.TextWrapped("No gear matches your search.");
                ImGui.PopStyleColor();
                ImGui.EndChild();
                return;
            }

            var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit;

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(CellPaddingX, CellPaddingY));

            if (ImGui.BeginTable("GearSelectorTable", 2, tableFlags, new Vector2(-1f, -1f)))
            {
                ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, iconSize + IconColumnExtraWidth);
                ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);

                var cellPadding = ImGui.GetStyle().CellPadding;
                var lineHeight = ImGui.GetTextLineHeight();
                var textSpacing = style.ItemSpacing.Y;
                var textBlockHeight = lineHeight * 2f + textSpacing;
                var rowHeight = MathF.Max(iconSize, textBlockHeight) + cellPadding.Y * 2f;

                for (int i = 0; i < filtered.Count; i++)
                {
                    var item = filtered[i];
                    var selected = _selectedItem != null && _selectedItem.Value.RowId == item.RowId;

                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                    ImGui.TableSetColumnIndex(0);
                    ImGui.PushID(item.RowId);

                    if (ImGui.Selectable("##GearRow", selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight)))
                        _selectedItem = item;

                    var rowMin = ImGui.GetItemRectMin();
                    var rowMax = ImGui.GetItemRectMax();
                    var contentHeight = (rowMax.Y - rowMin.Y) - cellPadding.Y * 2f;
                    var contentTop = rowMin.Y + cellPadding.Y;
                    var iconTop = contentTop + MathF.Max(0f, (contentHeight - iconSize) * 0.5f);
                    var iconPos = new Vector2(rowMin.X + cellPadding.X, iconTop);
                    var iconMax = iconPos + new Vector2(iconSize);

                    var iconTexture = _textureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
                    var drawList = ImGui.GetWindowDrawList();

                    if (iconTexture.TryGetWrap(out var wrap, out _))
                        drawList.AddImage(wrap.Handle, iconPos, iconMax);
                    else
                        drawList.AddRectFilled(iconPos, iconMax, ImGui.GetColorU32(ImGuiCol.FrameBg), 4f);

                    ImGui.TableSetColumnIndex(1);
                    var columnCursor = ImGui.GetCursorScreenPos();
                    var textCursorY = columnCursor.Y + MathF.Max(0f, (contentHeight - textBlockHeight) * 0.5f);
                    ImGui.SetCursorScreenPos(new Vector2(columnCursor.X, textCursorY));

                    string name = item.Name.ToString();
                    ImGui.TextUnformatted(name);

                    var modelId = item.ModelMain;
                    var modelBase = (modelId >> 16) & 0xFFFF;
                    var modelVariant = modelId & 0xFFFF;
                    string slotName = GearSlotHelper.DisplayName(_slot);
                    string infoText = $"Model {modelBase}, {modelVariant} | {slotName}";


                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    ImGui.TextUnformatted(infoText);
                    ImGui.PopStyleColor();

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            ImGui.PopStyleVar();
            ImGui.EndChild();
        }

    }
}
