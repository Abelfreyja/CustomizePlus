namespace CustomizePlus.Core.Events;

/// <summary>
/// Triggered when complete plugin reload is requested
/// </summary>
public sealed class ReloadEvent(LunaLogger log) : EventBase<ReloadEvent.Arguments, ReloadEvent.Priority>(nameof(ReloadEvent), log)
{
    public readonly record struct Arguments(Type Type);

    public enum Type
    {
        ReloadAll,
        ReloadProfiles,
        ReloadTemplates
    }

    public enum Priority
    {
        TemplateManager = -2,
        ProfileManager = -1
    }
}
