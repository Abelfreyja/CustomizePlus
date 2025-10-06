using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using CustomizePlus.Interop.Ipc;
using CustomizePlus.Profiles.Data;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.Api.Enums;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Interop;
using System;
using System.Linq;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace CustomizePlus.Profiles.Services;

public class ConditionService : IDisposable
{
    private readonly GearDataService _gearDataService;
    private readonly GameObjectService _gameObjectService;
    private readonly IObjectTable _objectTable;
    private readonly PenumbraIpcHandler _penumbraIpcHandler;

    public event Action? ModConditionStateChanged;

    public ConditionService(
        GearDataService gearDataService,
        GameObjectService gameObjectService,
        IObjectTable objectTable,
        PenumbraIpcHandler penumbraIpcHandler)
    {
        _gearDataService = gearDataService;
        _gameObjectService = gameObjectService;
        _objectTable = objectTable;
        _penumbraIpcHandler = penumbraIpcHandler;

        _penumbraIpcHandler.OnModSettingChanged += OnPenumbraModSettingChanged;
    }

    public void Dispose()
        => _penumbraIpcHandler.OnModSettingChanged -= OnPenumbraModSettingChanged;

    private void OnPenumbraModSettingChanged(ModSettingChange change, Guid collectionId, string modDirectory, bool inherited)
        => ModConditionStateChanged?.Invoke();

    public unsafe bool IsProfileConditionMet(Profile profile, ActorIdentifier actorId)
    {
        if (!profile.ConditionsEnabled || profile.Conditions.Count == 0)
            return true;

        var enabledConditions = profile.Conditions.Where(c => c.Enabled).ToList();
        if (enabledConditions.Count == 0)
            return true;

        var (trueIdentifier, _) = _gameObjectService.GetTrueActorForSpecialTypeActor(actorId);
        if (!trueIdentifier.IsValid)
            return false;

        var actor = _gameObjectService
            .FindActorsByIdentifierIgnoringOwnership(trueIdentifier)
            .Select(t => t.Item2)
            .FirstOrDefault(a => a.Valid);

        if (!actor.Valid)
            return false;

        var modConditions = enabledConditions.OfType<ModCondition>().ToList();
        if (modConditions.Count > 0)
        {
            if (!_penumbraIpcHandler.Available)
                return false;

            var objectIndex = actor.Index.Index;
            if (!_penumbraIpcHandler.TryGetEffectiveCollection(objectIndex, out var collectionId))
                return false;

            foreach (var modCondition in modConditions)
            {
                if (!_penumbraIpcHandler.TryGetModEnabled(collectionId, modCondition.ModName, out var isEnabled) || !isEnabled)
                    return false;
            }
        }

        var gearConditions = enabledConditions.OfType<GearCondition>().ToList();
        if (gearConditions.Count == 0)
            return true;

        var actorAddress = actor.Address;
        if (actorAddress == IntPtr.Zero)
            return false;

        var character = (CharacterStruct*)actorAddress;
        if (character == null)
            return false;

        var human = (Human*)character->DrawObject;
        if (human == null)
            return false;

        var equipPtr = (EquipmentModelId*)&human->Head;

        foreach (var group in gearConditions.GroupBy(c => c.Slot))
        {
            if (!GearSlotHelper.ConvertToHumanSlot(group.Key, out var humanSlot))
                continue;

            var actualModelId = equipPtr[(int)humanSlot].Id;

            if (!group.Any(cond => actualModelId == cond.ModelId))
                return false;
        }

        return true;
    }
}
