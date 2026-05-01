namespace CustomizePlus.Configuration.Services;

[Flags]
public enum PluginConfigurationChange
{
    None = 0,
    General = 1 << 0,
    PluginState = 1 << 1,
    ProfileApplication = 1 << 2,
    Interface = 1 << 3,
    Editor = 1 << 4,
    Integration = 1 << 5,
    Command = 1 << 6,
    Layout = 1 << 7,
    Popup = 1 << 8,
    Changelog = 1 << 9,
}
