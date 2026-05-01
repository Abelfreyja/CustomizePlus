using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using Dalamud.Interface.ImGuiNotification;
using Newtonsoft.Json;
using ErrorEventArgs = Newtonsoft.Json.Serialization.ErrorEventArgs;

namespace CustomizePlus.Configuration.Services;

public sealed class ConfigurationService : IDisposable
{
    private readonly SaveService _saveService;
    private readonly MessageService _messageService;
    private readonly ConfigurationMigrator _migrator;

    private bool _dirty;

    public ConfigurationService(
        SaveService saveService,
        MessageService messageService,
        ConfigurationMigrator migrator)
    {
        _saveService = saveService;
        _messageService = messageService;
        _migrator = migrator;

        Current = Load();
        if (_migrator.Migrate(Current))
            SaveNow();
    }

    public event Action<PluginConfiguration, PluginConfigurationChange>? Changed;

    public PluginConfiguration Current { get; }

    public void Update(Action<PluginConfiguration> update, PluginConfigurationChange change = PluginConfigurationChange.General)
    {
        update(Current);
        Save(change);
    }

    public void Save(PluginConfigurationChange change = PluginConfigurationChange.General)
    {
        _dirty = true;
        Changed?.Invoke(Current, change);
        _saveService.DelaySave(Current);
    }

    public void SaveNow()
    {
        _dirty = false;
        _saveService.ImmediateSaveSync(Current);
    }

    public void Dispose()
    {
        if (_dirty)
            SaveNow();
    }

    private PluginConfiguration Load()
    {
        var config = new PluginConfiguration();
        if (!File.Exists(_saveService.FileNames.ConfigFile))
            return config;

        try
        {
            var text = File.ReadAllText(_saveService.FileNames.ConfigFile);
            JsonConvert.PopulateObject(text, config, new JsonSerializerSettings
            {
                Error = HandleDeserializationError,
                Converters = new List<JsonConverter> { new ActorIdentifierJsonConverter() }
            });
        }
        catch (Exception ex)
        {
            _messageService.NotificationMessage(ex,
                "Error reading configuration, reverting to default.\nYou may be able to restore your configuration using the rolling backups in the XIVLauncher/backups/CustomizePlus directory.",
                "Error reading configuration", NotificationType.Error);
        }

        return config;
    }

    private static void HandleDeserializationError(object? sender, ErrorEventArgs errorArgs)
    {
        Plugin.Logger.Error(
            $"Error parsing configuration at {errorArgs.ErrorContext.Path}, using default or migrating:\n{errorArgs.ErrorContext.Error}");
        errorArgs.ErrorContext.Handled = true;
    }
}
