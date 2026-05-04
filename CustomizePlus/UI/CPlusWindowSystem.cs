using CustomizePlus.Configuration.Data;
using CustomizePlus.UI.Windows;
using CustomizePlus.UI.Windows.MainWindow;
using Dalamud.Interface;

namespace CustomizePlus.UI;

public class CPlusWindowSystem : IDisposable, IUiService
{
    private readonly IUiBuilder _uiBuilder;
    private readonly MainWindow _mainWindow;
    private readonly WindowSystem _windowSystem;

    public CPlusWindowSystem(
        WindowSystem windowSystem,
        IUiBuilder uiBuilder,
        MainWindow mainWindow,
        CPlusChangeLog changelog,
        PopupSystem popupSystem,
        PluginConfiguration configuration)
    {
        _uiBuilder = uiBuilder;
        _mainWindow = mainWindow;
        _windowSystem = windowSystem;

        _windowSystem.AddWindow(mainWindow);
        _windowSystem.AddWindow(changelog.Changelog);
        _windowSystem.AddWindow(popupSystem);
        _uiBuilder.OpenMainUi += _mainWindow.Toggle;
        _uiBuilder.OpenConfigUi += _mainWindow.OpenSettings;

        _uiBuilder.DisableGposeUiHide = !configuration.UISettings.HideWindowInGPose; //seems to be broken as of 2024/10/18
        _uiBuilder.DisableCutsceneUiHide = !configuration.UISettings.HideWindowInCutscene;
        _uiBuilder.DisableUserUiHide = !configuration.UISettings.HideWindowWhenUiHidden;
    }

    public void Dispose()
    {
        _uiBuilder.OpenMainUi -= _mainWindow.Toggle;
        _uiBuilder.OpenConfigUi -= _mainWindow.OpenSettings;
    }
}
