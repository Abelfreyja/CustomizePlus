using CustomizePlus.Templates.Data;

namespace CustomizePlus.Templates.Events;

/// <summary>
/// Triggered when Template is changed
/// </summary>
public class TemplateChanged(LunaLogger log) : EventBase<TemplateChanged.Arguments, TemplateChanged.Priority>(nameof(TemplateChanged), log)
{
    public readonly record struct Arguments(Type Type, Template? Template, object? Data);

    public enum Type
    {
        Created,
        Deleted,
        Renamed,
        NewBone,
        UpdatedBone,
        DeletedBone,
        EditorEnabled,
        EditorDisabled,
        EditorCharacterChanged,
        ReloadedAll,
        WriteProtection
    }

    public enum Priority
    {
        TemplateCombo = -2,
        TemplateFileSystemSelector = -1,
        TemplateFileSystem = 0,
        DesignHeader = 0,
        ArmatureManager = 1,
        ProfileManager = 2,
        CustomizePlusIpc = 3
    }
}
