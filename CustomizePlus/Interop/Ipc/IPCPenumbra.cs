using Dalamud.Plugin;
using Newtonsoft.Json.Linq;
using OtterGui.Log;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Interop.Ipc;

public sealed class PenumbraIpcHandler : IIpcSubscriber
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Logger _log;
    private readonly ApiVersion _version;

    private GetModList _getMods;
    private GetCollectionForObject _getCollectionForObject;
    private GetCurrentModSettings _getCurrentModSettings;
    private GetCurrentModSettingsWithTemp _getCurrentModSettingsWithTemp;

    private readonly EventSubscriber<ModSettingChange, Guid, string, bool> _modSettingChanged;
    private readonly EventSubscriber<JObject, ushort, string> _pcpCreated;
    private readonly EventSubscriber<JObject, string, Guid> _pcpParsed;

    private readonly IDisposable _penumbraInit;
    private readonly IDisposable _penumbraDisp;

    private const int RequiredMajor = 5;
    private const int RequiredMinor = 8;
    private int CurrentMajor = 0;
    private int CurrentMinor = 0;

    private bool _available = false;
    public bool Available => _available;

    private bool _shownVersionWarning = false;


    public PenumbraIpcHandler(IDalamudPluginInterface pi, Logger log)
    {
        _log = log;
        _pluginInterface = pi;
        _version = new ApiVersion(_pluginInterface);

        _getMods = new GetModList(_pluginInterface);
        _getCollectionForObject = new GetCollectionForObject(_pluginInterface);
        _getCurrentModSettings = new GetCurrentModSettings(_pluginInterface);
        _getCurrentModSettingsWithTemp = new GetCurrentModSettingsWithTemp(_pluginInterface);

        _pcpCreated = CreatingPcp.Subscriber(_pluginInterface);
        _pcpParsed = ParsingPcp.Subscriber(_pluginInterface);
        _modSettingChanged = ModSettingChanged.Subscriber(_pluginInterface);

        _penumbraInit = Initialized.Subscriber(_pluginInterface, Initialize);
        _penumbraDisp = Disposed.Subscriber(_pluginInterface, Disable);

        Initialize();
    }

    public event Action<JObject, ushort, string> PcpCreated
    {
        add => _pcpCreated.Event += value;
        remove => _pcpCreated.Event -= value;
    }

    public event Action<JObject, string, Guid> PcpParsed
    {
        add => _pcpParsed.Event += value;
        remove => _pcpParsed.Event -= value;
    }

    public event Action<ModSettingChange, Guid, string, bool> OnModSettingChanged
    {
        add => _modSettingChanged.Event += value;
        remove => _modSettingChanged.Event -= value;
    }

    public IReadOnlyDictionary<string, string> GetModList()
        => _getMods.Invoke();

    public bool CheckApiVersion()
    {
        try
        {
            var (major, minor) = _version.Invoke();
            CurrentMajor = major;
            CurrentMinor = minor;

            var valid = major == RequiredMajor && minor >= RequiredMinor;
            if (!valid && !_shownVersionWarning)
            {
                _shownVersionWarning = true;
                _log.Warning($"Penumbra IPC version is not supported. Required: {RequiredMajor}.{RequiredMinor}+");
            }

            return valid;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetEffectiveCollection(int gameObjectIndex, out Guid collectionId)
    {
        collectionId = Guid.Empty;

        if (!_available || gameObjectIndex < 0)
            return false;

        try
        {
            var (objectValid, _, effectiveCollection) = _getCollectionForObject.Invoke(gameObjectIndex);
            if (!objectValid || effectiveCollection.Id == Guid.Empty)
                return false;

            collectionId = effectiveCollection.Id;
            return true;
        }
        catch (Exception ex)
        {
            _log.Debug($"Failed to get collection for object index {gameObjectIndex}: {ex.Message}");
            return false;
        }
    }

    public bool TryGetModEnabled(Guid collectionId, string modIdentifier, out bool enabled)
    {
        enabled = false;

        if (!_available || collectionId == Guid.Empty || string.IsNullOrWhiteSpace(modIdentifier))
            return false;

        bool? ResolveState(string modDirectory, string modName)
        {
            try
            {
                var (result, data) = _getCurrentModSettingsWithTemp.Invoke(collectionId, modDirectory, modName, false, false, 0);
                if (result != PenumbraApiEc.Success || data == null)
                    return null;

                return data.Value.Item1;
            }
            catch
            {
                return null;
            }
        }

        var state = ResolveState(modIdentifier, string.Empty);
        if (state.HasValue)
        {
            enabled = state.Value;
            return true;
        }

        state = ResolveState(string.Empty, modIdentifier);
        if (state.HasValue)
        {
            enabled = state.Value;
            return true;
        }

        try
        {
            var mods = _getMods.Invoke();
            if (mods.Count == 0)
                return false;

            var directoryMatch = mods.FirstOrDefault(kv => kv.Key.Equals(modIdentifier, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(directoryMatch.Key))
            {
                state = ResolveState(directoryMatch.Key, string.Empty);
                if (state.HasValue)
                {
                    enabled = state.Value;
                    return true;
                }

                if (!string.IsNullOrEmpty(directoryMatch.Value))
                {
                    state = ResolveState(directoryMatch.Key, directoryMatch.Value);
                    if (state.HasValue)
                    {
                        enabled = state.Value;
                        return true;
                    }
                }
            }

            var nameMatch = mods.FirstOrDefault(kv => kv.Value.Equals(modIdentifier, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(nameMatch.Key))
            {
                state = ResolveState(nameMatch.Key, nameMatch.Value);
                if (state.HasValue)
                {
                    enabled = state.Value;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"Failed to resolve mod identifier '{modIdentifier}': {ex.Message}");
        }

        return false;
    }
    public void Initialize()
    {
        Disable();

        if (!CheckApiVersion())
            return;

        _available = true;

        _getMods = new GetModList(_pluginInterface);
        _getCollectionForObject = new GetCollectionForObject(_pluginInterface);
        _getCurrentModSettings = new GetCurrentModSettings(_pluginInterface);
        _getCurrentModSettingsWithTemp = new GetCurrentModSettingsWithTemp(_pluginInterface);

        _pcpCreated.Enable();
        _pcpParsed.Enable();
        _modSettingChanged.Enable();

        _log.Information($"Penumbra IPC initialized. Version {CurrentMajor}.{CurrentMinor}.");
    }
    public void Disable()
    {
        if (!_available)
            return;

        _available = false;

        _getMods = null!;
        _getCollectionForObject = null!;
        _getCurrentModSettings = null!;
        _getCurrentModSettingsWithTemp = null!;

        _pcpCreated.Disable();
        _pcpParsed.Disable();
        _modSettingChanged.Disable();

        _log.Information("Penumbra IPC disabled.");
    }
    public void Dispose()
    {
        Disable();

        _pcpCreated.Dispose();
        _pcpParsed.Dispose();
        _modSettingChanged.Dispose();

        _penumbraInit.Dispose();
        _penumbraDisp.Dispose();

        _log.Information("Penumbra IPC disposed.");
    }
}
