using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Interop.Ipc;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using OtterGui.Log;
using Penumbra.Api.Enums;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;
using System;
using System.Linq;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace CustomizePlus.Profiles.Services;

public class ConditionService : IDisposable
{
    private readonly GameObjectService _gameObjectService;
    private readonly IObjectTable _objectTable;
    private readonly PenumbraIpcHandler _penumbraIpcHandler;
    private readonly Logger _logger;

    public event Action? ModConditionStateChanged;

    public ConditionService(
        GameObjectService gameObjectService,
        IObjectTable objectTable,
        PenumbraIpcHandler penumbraIpcHandler,
        Logger logger)
    {
        _gameObjectService = gameObjectService;
        _objectTable = objectTable;
        _penumbraIpcHandler = penumbraIpcHandler;
        _logger = logger;

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
        {
            _logger.Debug($"Condition check aborted: could not resolve true identifier for {actorId.IncognitoDebug()} (profile {profile.UniqueId}).");
            return false;
        }

        var actor = _gameObjectService
            .FindActorsByIdentifierIgnoringOwnership(trueIdentifier)
            .Select(t => t.Item2)
            .FirstOrDefault(a => a.Valid);

        if (!actor.Valid)
        {
            _logger.Debug($"Condition check aborted: actor not found for {trueIdentifier.IncognitoDebug()} (profile {profile.UniqueId}).");
            return false;
        }

        var modConditions = enabledConditions.OfType<ModCondition>().ToList();
        if (modConditions.Count > 0)
        {
            if (!_penumbraIpcHandler.Available)
            {
                _logger.Debug($"Mod condition check failed: Penumbra IPC unavailable for actor {actorId.IncognitoDebug()} (profile {profile.UniqueId}).");
                return false;
            }

            var objectIndex = actor.Index.Index;
            if (!_penumbraIpcHandler.TryGetEffectiveCollection(objectIndex, out var collectionId))
            {
                _logger.Debug($"Mod condition check failed: no effective collection for actor index {objectIndex} (profile {profile.UniqueId}).");
                return false;
            }

            foreach (var modCondition in modConditions)
            {
                if (!_penumbraIpcHandler.TryGetModEnabled(collectionId, modCondition.ModName, out var isEnabled) || !isEnabled)
                {
                    _logger.Debug($"Mod condition not met for profile {profile.UniqueId}: '{modCondition.ModName}' disabled for actor {actorId.IncognitoDebug()}.");
                    return false;
                }
            }
        }

        var gearConditions = enabledConditions.OfType<GearCondition>().ToList();
        var raceConditions = enabledConditions.OfType<RaceCondition>().ToList();

        if (gearConditions.Count == 0 && raceConditions.Count == 0)
            return true;

        var actorAddress = actor.Address;
        if (actorAddress == IntPtr.Zero)
        {
            _logger.Debug($"Condition check failed: actor address zero for {actorId.IncognitoDebug()} (profile {profile.UniqueId}).");
            return false;
        }

        var character = (CharacterStruct*)actorAddress;
        if (character == null)
        {
            _logger.Debug($"Condition check failed: character pointer null for {actorId.IncognitoDebug()} (profile {profile.UniqueId}).");
            return false;
        }

        if (gearConditions.Count > 0)
        {
            var human = (Human*)character->DrawObject;
            if (human == null)
            {
                _logger.Debug($"Gear condition check failed: human draw object null for {actorId.IncognitoDebug()} (profile {profile.UniqueId}).");
                return false;
            }

            var equipPtr = (EquipmentModelId*)&human->Head;

            foreach (var group in gearConditions.GroupBy(c => c.Slot))
            {
                if (!GearSlotHelper.ConvertToHumanSlot(group.Key, out var humanSlot))
                    continue;

                var actualModelId = equipPtr[(int)humanSlot].Id;

                if (!group.Any(cond => actualModelId == cond.ModelId))
                {
                    _logger.Debug($"Gear condition not met for profile {profile.UniqueId}: slot {group.Key} expected one of [{string.Join(", ", group.Select(c => c.ModelId))}], actual {actualModelId} on actor {actorId.IncognitoDebug()}.");
                    return false;
                }
            }
        }

        if (raceConditions.Count > 0)
        {
            var model = actor.Model;
            if (!model.IsHuman)
            {
                _logger.Debug($"Race condition check failed: actor model is not human for {actorId.IncognitoDebug()} (profile {profile.UniqueId}).");
                return false;
            }

            var customize = model.GetCustomize();
            var actualRace = customize.Race;
            var actualGender = customize.Gender;
            var actualClan = customize.Clan;

            string Describe(Race race, SubRace clan, Gender gender)
                => $"{race.ToName()} ({clan.ToName()}) - {gender.ToName()}";

            foreach (var raceCondition in raceConditions)
            {
                bool matches = actualRace == raceCondition.Race
                    && actualGender == raceCondition.Gender
                    && actualClan == raceCondition.Clan;

                if (!matches)
                {
                    _logger.Debug($"Race condition not met for profile {profile.UniqueId}: expected {Describe(raceCondition.Race, raceCondition.Clan, raceCondition.Gender)}, actual {Describe(actualRace, actualClan, actualGender)} for actor {actorId.IncognitoDebug()}.");
                    return false;
                }
            }

            _logger.Debug($"Race condition satisfied for profile {profile.UniqueId}: actor {actorId.IncognitoDebug()} is {Describe(actualRace, actualClan, actualGender)}.");
        }

        return true;
    }
}
