using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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

        public void Draw(float iconSize = 60f)
        {
            if (_items == null)
                return;

            ImGui.InputTextWithHint("##GearSearch", "Search gear...", ref _search, 64);

            var filtered = string.IsNullOrWhiteSpace(_search)
                ? _items
                : _items.Where(i => i.Name.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

            ImGui.BeginChild("GearList", new Vector2(0, 300), true);

            for (int i = 0; i < filtered.Count; i++)
            {
                var item = filtered[i];
                var selected = _selectedItem != null && _selectedItem.Value.RowId == item.RowId;

                float spacingY = 4f;
                float rowHeight = iconSize + spacingY * 2;
                float rowWidth = ImGui.GetContentRegionAvail().X;

                if (ImGui.Selectable($"##gear_{i}", selected, ImGuiSelectableFlags.None, new Vector2(rowWidth, rowHeight)))
                    _selectedItem = item;

                var cursorPos = ImGui.GetItemRectMin();

                var rectMin = cursorPos;
                var rectMax = new Vector2(cursorPos.X + rowWidth, cursorPos.Y + rowHeight);
                var drawList = ImGui.GetWindowDrawList();
                var borderColor = ImGui.GetColorU32(ImGuiCol.Border);
                var bgColor = ImGui.GetColorU32(ImGuiCol.ChildBg);

                drawList.AddRectFilled(rectMin, rectMax, bgColor);
                drawList.AddRect(rectMin, rectMax, borderColor, 4f);

                float textHeight = ImGui.GetTextLineHeightWithSpacing() * 3;
                float textOffsetY = (rowHeight - textHeight) * 0.5f;
                float iconOffsetY = (rowHeight - iconSize) * 0.5f;

                ImGui.SetCursorScreenPos(new Vector2(cursorPos.X + 4, cursorPos.Y + iconOffsetY));
                var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
                if (icon.TryGetWrap(out var wrap, out _))
                    ImGui.Image(wrap.Handle, new Vector2(iconSize));
                else
                    ImGui.Dummy(new Vector2(iconSize));

                ImGui.SetCursorScreenPos(new Vector2(cursorPos.X + iconSize + 12, cursorPos.Y + textOffsetY));

                var modelId = item.ModelMain;
                var modelBase = (modelId >> 16) & 0xFFFF;
                var modelVariant = modelId & 0xFFFF;

                ImGui.BeginGroup();

                string name = item.Name.ToString();
                float maxTextWidth = rowWidth - (iconSize + 24);
                float nameWidth = ImGui.CalcTextSize(name).X;

                string displayName = name;
                bool wasTruncated = false;

                if (nameWidth > maxTextWidth)
                {
                    for (int len = name.Length - 1; len > 0; len--)
                    {
                        string test = name[..len] + "...";
                        if (ImGui.CalcTextSize(test).X <= maxTextWidth)
                        {
                            displayName = test;
                            wasTruncated = true;
                            break;
                        }
                    }

                    if (!wasTruncated)
                        displayName = "...";
                }

                ImGui.TextUnformatted(displayName);
                if (wasTruncated && ImGui.IsItemHovered())
                    ImGui.SetTooltip(name);

                ImGui.TextUnformatted($"{modelBase}, {modelVariant}");
                ImGui.TextUnformatted(_slot.ToString());

                ImGui.EndGroup();

                ImGui.Dummy(new Vector2(1, spacingY));
            }

            ImGui.EndChild();
        }

    }
}
