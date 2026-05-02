using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Events;
using CustomizePlus.UI.Windows.Controls;
using CustomizePlus.UI.Windows.MainWindow.Tabs;
using CustomizePlus.UI.Windows.MainWindow.Tabs.Debug;
using CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;
using CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;
using Dalamud.Interface.Colors;
using ECommonsLite.ImGuiMethods;
using ECommonsLite.Schedulers;
using LunaWindow = Luna.Window;
using WindowSizeConstraints = Dalamud.Interface.Windowing.WindowSizeConstraints;

namespace CustomizePlus.UI.Windows.MainWindow;

public class MainWindow : LunaWindow, IDisposable
{
    private readonly SettingsTab _settingsTab;
    private readonly TemplatesTab _templatesTab;
    private readonly ProfilesTab _profilesTab;
    private readonly MessagesTab _messagesTab;
    private readonly IPCTestTab _ipcTestTab;
    private readonly StateMonitoringTab _stateMonitoringTab;

    private readonly PluginStateBlock _pluginStateBlock;

    private readonly TemplateEditorManager _templateEditorManager;
    private readonly PluginConfiguration _configuration;
    private readonly HookingService _hookingService;

    private readonly TemplateEditorEvent _templateEditorEvent;

    /// <summary>
    /// Used to force the main window to switch to specific tab
    /// </summary>
    private string? _switchToTab = null;

    private Action? _actionAfterTabSwitch = null;

    public MainWindow(
        SettingsTab settingsTab,
        TemplatesTab templatesTab,
        ProfilesTab profilesTab,
        MessagesTab messagesTab,
        IPCTestTab ipcTestTab,
        StateMonitoringTab stateMonitoringTab,
        PluginStateBlock pluginStateBlock,
        TemplateEditorManager templateEditorManager,
        PluginConfiguration configuration,
        HookingService hookingService,
        TemplateEditorEvent templateEditorEvent
        ) : base($"Customize+ {VersionHelper.Version}###CPlusMainWindow")
    {
        _settingsTab = settingsTab;
        _templatesTab = templatesTab;
        _profilesTab = profilesTab;
        _messagesTab = messagesTab;
        _ipcTestTab = ipcTestTab;
        _stateMonitoringTab = stateMonitoringTab;

        _pluginStateBlock = pluginStateBlock;

        _templateEditorManager = templateEditorManager;
        _configuration = configuration;
        _hookingService = hookingService;

        _templateEditorEvent = templateEditorEvent;

        _templateEditorEvent.Subscribe(OnTemplateEditorEvent, TemplateEditorEvent.Priority.MainWindow);

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(700, 675),
            MaximumSize = Im.Viewport.Main.Size,
        };

        IsOpen = configuration.UISettings.OpenWindowAtStart;
    }

    public void Dispose()
    {
        _templateEditorEvent.Unsubscribe(OnTemplateEditorEvent);
    }

    public override void Draw()
    {
        var yPos = Im.Cursor.Position.Y;
        var tabs = GetTabs();

        using (var disabled = Im.Disabled(_hookingService.RenderHookFailed || _hookingService.MovementHookFailed))
        {
            LockWindowClosureIfNeeded();
            ImGuiEx.EzTabBar("##tabs", null, _switchToTab, tabs);

            _switchToTab = null;

            if (_actionAfterTabSwitch != null)
            {
                _actionAfterTabSwitch();
                _actionAfterTabSwitch = null;
            }
        }

        _pluginStateBlock.Draw(yPos, CalculatePluginStateLeftEdge(tabs));
    }

    public void OpenSettings()
    {
        IsOpen = true;
        _switchToTab = "Settings";
    }

    private (string name, Action function, Vector4? color, bool child)[] GetTabs()
    {
        if (!_configuration.DebuggingModeEnabled)
        {
            return [
                ("Settings", _settingsTab.Draw, null, true),
                ("Templates", _templatesTab.Draw, null, true),
                ("Profiles", _profilesTab.Draw, null, true),
            ];
        }

        return [
            ("Settings", _settingsTab.Draw, null, true),
            ("Templates", _templatesTab.Draw, null, true),
            ("Profiles", _profilesTab.Draw, null, true),
            ("IPC Test", _ipcTestTab.Draw, ImGuiColors.DalamudGrey, true),
            ("State monitoring", _stateMonitoringTab.Draw, ImGuiColors.DalamudGrey, true),
        ];
    }

    private static float CalculatePluginStateLeftEdge((string name, Action function, Vector4? color, bool child)[] tabs)
    {
        var leftEdge = Im.Window.MinimumContentRegion.X;
        foreach (var (name, _, _, _) in tabs)
        {
            leftEdge += CalculateTabWidth(name);
        }

        return leftEdge + Im.Style.ItemSpacing.X;
    }

    private static float CalculateTabWidth(string label)
        => Im.Font.CalculateSize(label, false).X + (2 * Im.Style.FramePadding.X) + Im.Style.ItemInnerSpacing.X;

    private void LockWindowClosureIfNeeded()
    {
        if (_templateEditorManager.IsEditorActive)
        {
            ShowCloseButton = false;
            RespectCloseHotkey = false;
        }
        else
        {
            ShowCloseButton = true;
            RespectCloseHotkey = true;
        }
    }

    private void OnTemplateEditorEvent(in TemplateEditorEvent.Arguments args)
    {
        var (type, template) = args;
        if (type != TemplateEditorEvent.Type.EditorEnableRequested)
            return;

        if (template == null)
            return;

        if (!template.IsWriteProtected && !_templateEditorManager.IsEditorActive)
        {
            new TickScheduler(() =>
            {
                _switchToTab = "Templates";

                //To make sure the tab has switched, ugly but imgui is shit and I don't trust it.
                _actionAfterTabSwitch = () => { _templateEditorEvent.Invoke(new TemplateEditorEvent.Arguments(TemplateEditorEvent.Type.EditorEnableRequestedStage2, template)); };
            });
        }
    }
}
