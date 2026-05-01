using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using CustomizePlus.UI.Windows;
using Dalamud.Configuration;
using Newtonsoft.Json;
using Penumbra.GameData.Actors;

namespace CustomizePlus.Configuration.Data;

[Serializable]
public class PluginConfiguration : IPluginConfiguration, ISavable
{
    public const int CurrentVersion = Constants.ConfigurationVersion;

    public int Version { get; set; } = CurrentVersion;

    public bool PluginEnabled { get; set; } = true;

    public bool DebuggingModeEnabled { get; set; }

    /// <summary>
    /// Id of the default profile applied to all characters without any profile. Can be set to Empty to disable this feature.
    /// </summary>
    public Guid DefaultProfile { get; set; } = Guid.Empty;

    /// <summary>
    /// Id of the profile applied to any character user logins with. Can be set to Empty to disable this feature.
    /// </summary>
    public Guid DefaultLocalPlayerProfile { get; set; } = Guid.Empty;

    [Serializable]
    public class ChangelogSettingsEntries
    {
        public int LastSeenVersion { get; set; } = CPlusChangeLog.LastChangelogVersion;
        public ChangeLogDisplayType ChangeLogDisplayType { get; set; } = ChangeLogDisplayType.New;
    }

    public ChangelogSettingsEntries ChangelogSettings { get; set; } = new();

    [Serializable]
    public class UISettingsEntries
    {
        [JsonConverter(typeof(DoubleModifierJsonConverter))]
        public DoubleModifier DeleteTemplateModifier { get; set; } = new(ModifierHotkey.Control, ModifierHotkey.Shift);

        public bool FoldersDefaultOpen { get; set; } = true;

        public bool OpenWindowAtStart { get; set; } = false;

        public bool HideWindowInCutscene { get; set; } = true;

        public bool HideWindowWhenUiHidden { get; set; } = true;

        public bool HideWindowInGPose { get; set; } = false;

        public bool IncognitoMode { get; set; } = false;

        public float CurrentTemplateSelectorWidth { get; set; } = 200f;

        public float TemplateSelectorMinimumScale { get; set; } = 0.1f;

        public float TemplateSelectorMaximumScale { get; set; } = 0.5f;

        public float CurrentProfileSelectorWidth { get; set; } = 200f;

        public float ProfileSelectorMinimumScale { get; set; } = 0.1f;

        public float ProfileSelectorMaximumScale { get; set; } = 0.5f;

        public List<string> ViewedMessageWindows { get; set; } = new();
    }

    public UISettingsEntries UISettings { get; set; } = new();

    [Serializable]
    public class EditorConfigurationEntries
    {
        /// <summary>
        /// Hides root position from the UI. DOES NOT DISABLE LOADING IT FROM THE CONFIG!
        /// </summary>
        public bool RootPositionEditingEnabled { get; set; } = false;

        public bool ShowLiveBones { get; set; } = true;

        public bool BoneMirroringEnabled { get; set; } = false;

        public ActorIdentifier PreviewCharacter { get; set; } = ActorIdentifier.Invalid;

        public int EditorValuesPrecision { get; set; } = 3;

        public BoneAttribute EditorMode { get; set; } = BoneAttribute.Position;

        public bool SetPreviewToCurrentCharacterOnLogin { get; set; } = false;

        public HashSet<string> FavoriteBones { get; set; } = new();
    }

    public EditorConfigurationEntries EditorConfiguration { get; set; } = new();

    [Serializable]
    public class CommandSettingsEntries
    {
        public bool PrintSuccessMessages { get; set; } = true;
    }

    public CommandSettingsEntries CommandSettings { get; set; } = new();

    [Serializable]
    public class ProfileApplicationSettingsEntries
    {
        public bool ApplyInCharacterWindow { get; set; } = true;
        public bool ApplyInTryOn { get; set; } = true;
        public bool ApplyInCards { get; set; } = true;
        public bool ApplyInInspect { get; set; } = true;
        public bool ApplyInLobby { get; set; } = true;
    }

    public ProfileApplicationSettingsEntries ProfileApplicationSettings { get; set; } = new();

    [Serializable]
    public class IntegrationSettingsEntries
    {
        public bool PenumbraPCPIntegrationEnabled { get; set; } = true;
    }

    public IntegrationSettingsEntries IntegrationSettings { get; set; } = new();

    public string ToFilename(FilenameService fileNames)
        => fileNames.ConfigFile;

    public void Save(StreamWriter writer)
    {
        using var jWriter = new JsonTextWriter(writer) { Formatting = Formatting.Indented };
        var serializer = new JsonSerializer { Formatting = Formatting.Indented };
        serializer.Converters.Add(new ActorIdentifierJsonConverter());
        serializer.Serialize(jWriter, this);
    }
}
