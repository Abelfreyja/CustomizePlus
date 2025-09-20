using CustomizePlus.Game.Helpers;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace CustomizePlus.Game.Services;

public unsafe class GearDataService
{
    private readonly ExcelSheet<Item> _itemSheet;
    private readonly ExcelSheet<EquipSlotCategory> _equipSlotSheet;

    public GearDataService(IDataManager dataManager)
    {
        _itemSheet = dataManager.GetExcelSheet<Item>()!;
        _equipSlotSheet = dataManager.GetExcelSheet<EquipSlotCategory>()!;
    }

    public List<Item> GetItemsForSlot(GearSlot slot)
    {
        var results = new List<Item>();

        foreach (var item in _itemSheet)
        {
            if (item.RowId == 0 || item.ModelMain == 0)
                continue;

            var slotCategory = _equipSlotSheet.GetRow(item.EquipSlotCategory.RowId);
            if (slotCategory.RowId == 0)
                continue;

            if (SlotMatches(slotCategory, slot))
                results.Add(item);
        }

        return results;
    }

    public Item? GetItemByModelId(GearSlot slot, ushort modelId)
    {
        foreach (var item in _itemSheet)
        {
            if (item.RowId == 0 || item.ModelMain == 0)
                continue;

            var slotCategory = _equipSlotSheet.GetRow(item.EquipSlotCategory.RowId);
            if (slotCategory.RowId == 0)
                continue;

            if (!SlotMatches(slotCategory, slot))
                continue;

            if ((ushort)item.ModelMain == modelId)
                return item;
        }

        return null;
    }

    public bool IsWearing(Span<EquipmentModelId> equipModels, GearSlot slot, ushort expectedModelId)
    {
        if (expectedModelId == 0)
            return false;

        var actualModelId = GearSlotHelper.GetEquippedModel(equipModels, slot);
        return actualModelId == expectedModelId;
    }

    private static bool SlotMatches(EquipSlotCategory category, GearSlot slot) => slot switch
    {
        GearSlot.Head => category.Head != 0,
        GearSlot.Body => category.Body != 0,
        GearSlot.Hands => category.Gloves != 0,
        GearSlot.Legs => category.Legs != 0,
        GearSlot.Feet => category.Feet != 0,
        GearSlot.Ears => category.Ears != 0,
        GearSlot.Neck => category.Neck != 0,
        GearSlot.Wrists => category.Wrists != 0,
        GearSlot.LeftRing => category.FingerL != 0,
        GearSlot.RightRing => category.FingerR != 0,
        _ => false
    };
}
