using System;
using System.Collections.Generic;
using CustomizePlus.Game.Helpers;
using CustomizePlus.Profiles.Enums;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Enums;

namespace CustomizePlus.Profiles.Data
{
    public abstract class ProfileCondition
    {
        public ConditionType Type { get; }
        public bool Enabled { get; set; } = true;

        protected ProfileCondition(ConditionType type)
        {
            Type = type;
        }

        public static JArray SerializeConditions(IEnumerable<ProfileCondition> conditions)
        {
            var arr = new JArray();
            foreach (var cond in conditions)
            {
                var obj = new JObject
                {
                    ["Type"] = cond.Type.ToString(),
                    ["Enabled"] = cond.Enabled
                };
                switch (cond)
                {
                    case ModCondition mod:
                        obj["ModName"] = mod.ModName;
                        break;
                    case GearCondition gear:
                        obj["Slot"] = gear.Slot.ToString();
                        obj["ModelId"] = gear.ModelId;
                        break;
                    case RaceCondition race:
                        obj["Race"] = race.Race.ToString();
                        obj["Gender"] = race.Gender.ToString();
                        obj["Clan"] = race.Clan.ToString();
                        break;
                    case EmoteCondition emote:
                        obj["EmoteId"] = emote.EmoteId;
                        break;
                }
                arr.Add(obj);
            }
            return arr;
        }

        public static IEnumerable<ProfileCondition> DeserializeConditions(JArray arr)
        {
            foreach (var token in arr)
            {
                if (token is not JObject obj)
                    continue;

                var typeStr = obj["Type"]?.ToString();
                var enabled = obj["Enabled"]?.ToObject<bool>() ?? true;

                if (Enum.TryParse<ConditionType>(typeStr, out var type))
                {
                    switch (type)
                    {
                        case ConditionType.Mod:
                            var modName = obj["ModName"]?.ToString() ?? "";
                            var modCond = new ModCondition(modName) { Enabled = enabled };
                            yield return modCond;
                            break;
                        case ConditionType.Gear:
                            var slotStr = obj["Slot"]?.ToString() ?? GearSlot.Head.ToString();
                            var modelId = obj["ModelId"]?.ToObject<ushort>() ?? 0;
                            if (Enum.TryParse<GearSlot>(slotStr, out var slot))
                            {
                                var gearCond = new GearCondition(slot, modelId) { Enabled = enabled };
                                yield return gearCond;
                            }
                            break;
                        case ConditionType.Race:
                            if (!Enum.TryParse<Race>(obj["Race"]?.ToString(), out var raceValue))
                                break;
                            if (!Enum.TryParse<Gender>(obj["Gender"]?.ToString(), out var genderValue))
                                break;
                            if (!Enum.TryParse<SubRace>(obj["Clan"]?.ToString(), out var clanValue))
                                break;

                            var raceCond = new RaceCondition(raceValue, clanValue, genderValue)
                            {
                                Enabled = enabled
                            };
                            yield return raceCond;
                            break;
                        case ConditionType.Emote:
                            var emoteIdToken = obj["EmoteId"];
                            if (emoteIdToken == null)
                                break;

                            var emoteId = emoteIdToken.ToObject<ushort?>();
                            if (emoteId is null)
                                break;

                            var emoteCond = new EmoteCondition(emoteId.Value)
                            {
                                Enabled = enabled
                            };
                            yield return emoteCond;
                            break;
                    }
                }
            }
        }
    }

    public class ModCondition : ProfileCondition
    {
        public string ModName { get; set; }
        public ModCondition(string modName) : base(ConditionType.Mod)
        {
            ModName = modName;
        }
    }

    public class GearCondition : ProfileCondition
    {
        public GearSlot Slot { get; set; }
        public ushort ModelId { get; set; }
        public GearCondition(GearSlot slot, ushort modelId) : base(ConditionType.Gear)
        {
            Slot = slot;
            ModelId = modelId;
        }
    }

    public class RaceCondition : ProfileCondition
    {
        public Race Race { get; set; }
        public SubRace Clan { get; set; }
        public Gender Gender { get; set; }

        public RaceCondition(Race race, SubRace clan, Gender gender)
            : base(ConditionType.Race)
        {
            Race = race;
            Clan = clan;
            Gender = gender;
        }
    }

    public class EmoteCondition : ProfileCondition
    {
        public ushort EmoteId { get; set; }
        public EmoteCondition(ushort emoteId)
            : base(ConditionType.Emote)
        {
            EmoteId = emoteId;
        }
    }
}
