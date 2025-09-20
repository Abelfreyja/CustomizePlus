using CustomizePlus.Core.Services;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles
{
    public class ModSelector
    {
        private readonly ModService _modService;
        private List<string> _mods = new();
        private string _search = "";
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

        public void Draw(float rowHeight = 28f)
        {
            ImGui.InputTextWithHint("##ModSearch", "Search mods...", ref _search, 64);

            var filtered = string.IsNullOrWhiteSpace(_search)
                ? _mods
                : _mods.Where(name => name.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

            ImGui.BeginChild("ModList", new Vector2(0, 200), true);

            for (int i = 0; i < filtered.Count; i++)
            {
                var mod = filtered[i];
                var selected = _selectedMod == mod;

                if (ImGui.Selectable($"##mod_{i}", selected, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, rowHeight)))
                    _selectedMod = mod;

                var cursorPos = ImGui.GetItemRectMin();
                var rectMax = new Vector2(cursorPos.X + ImGui.GetContentRegionAvail().X, cursorPos.Y + rowHeight);
                var drawList = ImGui.GetWindowDrawList();

                var bgColor = ImGui.GetColorU32(ImGuiCol.ChildBg);
                var borderColor = ImGui.GetColorU32(ImGuiCol.Border);

                drawList.AddRectFilled(cursorPos, rectMax, bgColor);
                drawList.AddRect(cursorPos, rectMax, borderColor, 4f);

                float textOffsetY = (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
                ImGui.SetCursorScreenPos(new Vector2(cursorPos.X + 8f, cursorPos.Y + textOffsetY));

                string displayName = mod;
                bool wasTruncated = false;
                float maxTextWidth = ImGui.GetContentRegionAvail().X - 16f;
                float nameWidth = ImGui.CalcTextSize(mod).X;

                if (nameWidth > maxTextWidth)
                {
                    for (int len = mod.Length - 1; len > 0; len--)
                    {
                        string test = mod[..len] + "...";
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
                    ImGui.SetTooltip(mod);

                ImGui.Dummy(new Vector2(1, 4));
            }

            ImGui.EndChild();
        }
    }
}
