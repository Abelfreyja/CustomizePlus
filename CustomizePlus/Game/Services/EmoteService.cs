using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using OtterGui.Log;
using Penumbra.GameData.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Game.Services;

public class EmoteService
{
    public sealed record EmoteEntry(ushort Id, string Name, uint IconId);

    private readonly Logger _logger;
    private readonly List<EmoteEntry> _emotes = new();
    private readonly Dictionary<ushort, EmoteEntry> _emotesById = new();

    public EmoteService(Logger logger, IDataManager dataManager)
    {
        _logger = logger;

        var sheet = dataManager.GetExcelSheet<Emote>()!;
        foreach (var emote in sheet)
        {
            if (emote.RowId == 0)
                continue;

            var name = emote.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var entry = new EmoteEntry((ushort)emote.RowId, name, emote.Icon);
            _emotes.Add(entry);
            _emotesById[entry.Id] = entry;
        }

        _emotes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly ushort[] ChairSitEmotes = { 50, 95, 96, 254, 255 }; // not groundsit

    public IReadOnlyList<EmoteEntry> GetEmotes() // gets the list of emotes
        => _emotes;

    public EmoteEntry? FindById(ushort emoteId) // finds an emote by its Id, mostly to get the name
        => _emotesById.TryGetValue(emoteId, out var entry) ? entry : null;

    public unsafe bool IsSitting(Actor actor) // for root shenanigans mostly
    {
        if (!actor.IsCharacter || actor.AsCharacter == null)
            return false;

        var emoteId = actor.AsCharacter->EmoteController.EmoteId;
        var isSitting = ChairSitEmotes.Contains(emoteId);

        _logger.Debug($"Actor {actor.Utf8Name} EmoteId: {emoteId} | Sitting: {isSitting}");
        return isSitting;
    }
}
