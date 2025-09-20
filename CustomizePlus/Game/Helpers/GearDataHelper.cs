using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Penumbra.GameData.Enums;
using System;
using System.Collections.Generic;

namespace CustomizePlus.Game.Helpers
{
    public enum GearSlot
    {
        Head,
        Body,
        Hands,
        Legs,
        Feet,
        Ears,
        Neck,
        Wrists,
        LeftRing,
        RightRing,
    }

    public static class GearSlotHelper
    {
        public static ushort GetEquippedModel(Span<EquipmentModelId> equipModels, GearSlot slot)
        {
            if ((int)slot >= equipModels.Length)
                return 0;

            return (ushort)equipModels[(int)slot].Value;
        }

        public static bool ConvertToHumanSlot(GearSlot slot, out HumanSlot result)
        {
            result = slot switch
            {
                GearSlot.Head => HumanSlot.Head,
                GearSlot.Body => HumanSlot.Body,
                GearSlot.Hands => HumanSlot.Hands,
                GearSlot.Legs => HumanSlot.Legs,
                GearSlot.Feet => HumanSlot.Feet,
                GearSlot.Ears => HumanSlot.Ears,
                GearSlot.Neck => HumanSlot.Neck,
                GearSlot.Wrists => HumanSlot.Wrists,
                GearSlot.LeftRing => HumanSlot.LFinger,
                GearSlot.RightRing => HumanSlot.RFinger,
                _ => HumanSlot.Unknown,
            };

            return result != HumanSlot.Unknown;
        }

        public static string DisplayName(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Head: return "Head";
                case GearSlot.Body: return "Body";
                case GearSlot.Hands: return "Gloves";
                case GearSlot.Legs: return "Legs";
                case GearSlot.Feet: return "Feet";
                case GearSlot.Ears: return "Earrings";
                case GearSlot.Neck: return "Necklace";
                case GearSlot.Wrists: return "Wrist";
                case GearSlot.LeftRing: return "Left Ring";
                case GearSlot.RightRing: return "Right Ring";
                default: return slot.ToString();
            }
        }

        public static IEnumerable<GearSlot> AllSlots
        {
            get
            {
                yield return GearSlot.Head;
                yield return GearSlot.Body;
                yield return GearSlot.Hands;
                yield return GearSlot.Legs;
                yield return GearSlot.Feet;
                yield return GearSlot.Ears;
                yield return GearSlot.Neck;
                yield return GearSlot.Wrists;
                yield return GearSlot.LeftRing;
                yield return GearSlot.RightRing;
            }
        }
    }
}
