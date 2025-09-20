using CustomizePlus.Interop.Ipc;
using OtterGui.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Services;

public sealed class ModService : IRequiredService
{
    private readonly PenumbraIpcHandler _penumbra;
    private List<string> _modNames = new();

    public ModService(PenumbraIpcHandler penumbra)
    {
        _penumbra = penumbra;

        if (_penumbra.Available)
        {
            _penumbra.OnModSettingChanged += OnModSettingChanged;
            RefreshModList();
        }
    }

    public void Dispose()
    {
        if (_penumbra.Available)
        {
            _penumbra.OnModSettingChanged -= OnModSettingChanged;
        }
    }

    private void OnModSettingChanged(Penumbra.Api.Enums.ModSettingChange _, Guid __, string ___, bool ____)
    {
        RefreshModList();
    }

    private void RefreshModList()
    {
        try
        {
            var mods = _penumbra.GetModList();
            _modNames = mods.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            _modNames.Clear();
        }
    }

    public IReadOnlyList<string> GetAvailableMods()
        => _modNames;

    public bool IsValidMod(string modName)
        => _modNames.Contains(modName, StringComparer.OrdinalIgnoreCase);
}
