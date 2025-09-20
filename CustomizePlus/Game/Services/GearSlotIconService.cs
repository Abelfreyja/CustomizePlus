using CustomizePlus.Game.Helpers;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using OtterGui.Classes;
using System.Collections.Generic;

namespace CustomizePlus.Game.Services;

public class GearSlotIconService
{
    private readonly Dictionary<GearSlot, IDalamudTextureWrap> _icons = new();

    public IReadOnlyDictionary<GearSlot, IDalamudTextureWrap> Icons => _icons;

    public GearSlotIconService(IPluginLog log, IUiBuilder uiBuilder)
    {
        using var uld = uiBuilder.LoadUld("ui/uld/ArmouryBoard.uld");

        if (!uld.Valid)
        {
            log.Warning("failed to load .uld");
            return;
        }

        TryAdd(GearSlot.Head, 1);
        TryAdd(GearSlot.Body, 2);
        TryAdd(GearSlot.Hands, 3);
        TryAdd(GearSlot.Legs, 5);
        TryAdd(GearSlot.Feet, 6);
        TryAdd(GearSlot.Ears, 8);
        TryAdd(GearSlot.Neck, 9);
        TryAdd(GearSlot.Wrists, 10);
        TryAdd(GearSlot.LeftRing, 11);
        TryAdd(GearSlot.RightRing, 11);

        void TryAdd(GearSlot slot, int partIndex)
        {
            var wrap = uld.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", partIndex);
            if (wrap != null)
                _icons[slot] = wrap;
            else
                log.Warning($"failed to load icon for {slot} (part {partIndex})");
        }
    }
}
