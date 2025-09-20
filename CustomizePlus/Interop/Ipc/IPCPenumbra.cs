using Dalamud.Plugin;
using Newtonsoft.Json.Linq;
using OtterGui.Log;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using System;
using System.Collections.Generic;

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
