using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using CustomizePlus.Profiles.Data;
using Dalamud.Plugin.Services;
using ECommonsLite.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Interop;
using System;
using System.Linq;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace CustomizePlus.Profiles.Services;

public class ConditionService
{
    private readonly GearDataService _gearDataService;
    private readonly GameObjectService _gameObjectService;
    private readonly IObjectTable _objectTable;

    public ConditionService(GearDataService gearDataService, GameObjectService gameObjectService, IObjectTable objectTable)
    {
        _gearDataService = gearDataService;
        _gameObjectService = gameObjectService;
        _objectTable = objectTable;
    }

    public unsafe bool IsProfileConditionMet(Profile profile, ActorIdentifier actorId)
    {
        if (!profile.ConditionsEnabled || profile.Conditions.Count == 0)
            return true;

        var (trueIdentifier, _) = _gameObjectService.GetTrueActorForSpecialTypeActor(actorId);

        var actor = _gameObjectService
            .FindActorsByIdentifierIgnoringOwnership(trueIdentifier)
            .Select(t => t.Item2)
            .FirstOrDefault(a => !a.Equals(default) && a.Address != IntPtr.Zero);

        if (actor.Address == IntPtr.Zero)
        {
            return false;
        }

        var character = (CharacterStruct*)actor.Address;
        if (character == null)
        {
            return false;
        }

        var human = (Human*)character->DrawObject;
        if (human == null)
        {
            return false;
        }

        var groupedGearConds = profile.Conditions
            .Where(c => c.Enabled)
            .OfType<GearCondition>()
            .GroupBy(c => c.Slot);

        foreach (var group in groupedGearConds)
        {

            if (!GearSlotHelper.ConvertToHumanSlot(group.Key, out var humanSlot))
            {
                continue;
            }

            var index = (int)humanSlot;
            var equipPtr = (EquipmentModelId*)&human->Head;
            var actualModelId = equipPtr[index].Id;

            bool anyMatch = false;
            foreach (var cond in group)
            {
                if (actualModelId == cond.ModelId)
                {
                    anyMatch = true;
                    break;
                }
            }

            if (!anyMatch)
            {
                //PluginLog.Debug($"no matching condition found for slot: {group.Key}");
                return false;
            }
        }

        return true;
    }


}
