using CustomizePlus.Armatures.Data;

namespace CustomizePlus.Armatures.Events;

/// <summary>
/// Triggered when armature is changed
/// </summary>
public sealed class ArmatureChanged(LunaLogger log)
    : EventBase<ArmatureChanged.Arguments, ArmatureChanged.Priority>(nameof(ArmatureChanged), log)
{
    public readonly record struct Arguments(Type Type, Armature Armature, object? Data);

    public enum Type
    {
        Created,
        Deleted,
        /// <summary>
        /// Called when armature was rebound to other profile or bone template bindings were rebuilt
        /// </summary>
        Updated
    }

    public enum Priority
    {
        ProfileManager,
        CustomizePlusIpc
    }

    public enum DeletionReason
    {
        Gone,
        NoActiveProfiles,
        ProfileManagerEvent,
        TemplateEditorEvent
    }
}
