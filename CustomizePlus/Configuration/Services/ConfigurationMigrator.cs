using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using Dalamud.Interface.ImGuiNotification;

namespace CustomizePlus.Configuration.Services;

public class ConfigurationMigrator
{
    private readonly MessageService _messageService; //we can't use popups here since they rely on PluginConfiguration and using them here hangs plugin loading
    private readonly Logger _logger;

    public ConfigurationMigrator(
        MessageService messageService,
        Logger logger
        )
    {
        _messageService = messageService;
        _logger = logger;
    }

    public bool Migrate(PluginConfiguration config)
    {
        var configVersion = config.Version;

        if (configVersion >= Constants.ConfigurationVersion)
            return false;

        //We no longer support migrations of any versions < 4
        if (configVersion < 4)
        {
            _messageService.NotificationMessage("Unsupported version of Customize+ configuration data detected. Check FAQ over at https://github.com/Aether-Tools/CustomizePlus for information.", NotificationType.Error);
            return false;
        }

        // V4 to V5: Added ChildScaling field to BoneTransform
        if (configVersion == 4)
        {
            _logger.Information("Migrating configuration from V4 to V5 (ChildScaling feature)");
        }

        config.Version = Constants.ConfigurationVersion;
        return true;
    }
}
