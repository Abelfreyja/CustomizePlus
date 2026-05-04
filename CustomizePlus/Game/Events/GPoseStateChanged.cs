namespace CustomizePlus.Game.Events;

/// <summary>
/// Triggered when GPose is entered/exited
/// </summary>
public sealed class GPoseStateChanged(LunaLogger log)
    : EventBase<GPoseStateChanged.Arguments, GPoseStateChanged.Priority>(nameof(GPoseStateChanged), log)
{
    public readonly record struct Arguments(Type Type);

    public enum Type
    {
        Entered,
        AttemptingExit,
        Exiting,
        Exited
    }

    public enum Priority
    {
        TemplateEditorManager = -1,
        GPoseAmnesisKtisisWarningService
    }
}
