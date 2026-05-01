using CustomizePlus.Templates.Data;

namespace CustomizePlus.Templates.Events;

/// <summary>
/// Triggered when something related to template editor happens
/// </summary>
public class TemplateEditorEvent(LunaLogger log)
    : EventBase<TemplateEditorEvent.Arguments, TemplateEditorEvent.Priority>(nameof(TemplateEditorEvent), log)
{
    public readonly record struct Arguments(Type Type, Template? Template)

    public enum Type
    {
        /// <summary>
        /// Called when something requests editor to be enabled.
        /// </summary>
        EditorEnableRequested,
        /// <summary>
        /// Called when something requests editor to be enabled. Stage 2 - logic after tab has been switched.
        /// </summary>
        EditorEnableRequestedStage2
    }

    public enum Priority
    {
        MainWindow = -1,
        TemplatePanel
    }
}