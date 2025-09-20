using System;
using System.Collections.Generic;
using CustomizePlus.Profiles.Enums;
using CustomizePlus.Game.Helpers;
using Newtonsoft.Json.Linq;

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
}

