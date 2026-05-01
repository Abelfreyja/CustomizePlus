using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates.Events;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;

public class TemplatePanel : IHeader, IPanel, IDisposable
{
    private readonly TemplateFileSystemSelector _selector;
    private readonly TemplateManager _manager;
    private readonly BoneEditorPanel _boneEditor;
    private readonly PluginConfiguration _configuration;
    private readonly MessageService _messageService;
    private readonly PopupSystem _popupSystem;
    private readonly Logger _logger;

    private readonly TemplateEditorEvent _editorEvent;

    private string? _newName;
    private Template? _changedTemplate;

    /// <summary>
    /// Set to true if we received OnEditorEvent EditorEnableRequested and waiting for selector value to be changed.
    /// </summary>
    private bool _isEditorEnablePending = false;

    private string SelectionName
        => _selector.SelectedPaths.Count > 1
            ? "Multiple Templates"
            : _selector.Selected == null
                ? "No Selection"
                : _selector.IncognitoMode
                    ? _selector.Selected.Incognito
                    : _selector.Selected.Name.Text;

    public ReadOnlySpan<byte> Id
        => "TemplatePanel"u8;

    public bool Collapsed
        => false;

    public TemplatePanel(
        TemplateFileSystemSelector selector,
        TemplateManager manager,
        BoneEditorPanel boneEditor,
        PluginConfiguration configuration,
        MessageService messageService,
        PopupSystem popupSystem,
        Logger logger,
        TemplateEditorEvent editorEvent)
    {
        _selector = selector;
        _manager = manager;
        _boneEditor = boneEditor;
        _configuration = configuration;
        _messageService = messageService;
        _popupSystem = popupSystem;
        _logger = logger;

        _editorEvent = editorEvent;

        _editorEvent.Subscribe(OnEditorEvent, TemplateEditorEvent.Priority.TemplatePanel);

        _selector.SelectionChanged += SelectorSelectionChanged;
    }

    public void Draw()
    {
        if (_selector.SelectedPaths.Count > 1)
        {
            DrawMultiSelection();
        }
        else
        {
            DrawPanel();
        }
    }

    public void Draw(Vector2 size)
        => DrawHeader();

    public void Dispose()
    {
        _editorEvent.Unsubscribe(OnEditorEvent);
    }

    private HeaderDrawer.Button LockButton()
        => _selector.Selected == null
            ? HeaderDrawer.Button.Invisible
            : _selector.Selected.IsWriteProtected
                ? new HeaderDrawer.Button
                {
                    Description = "Make this template editable.",
                    Icon = FontAwesomeIcon.Lock,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, false),
                    Disabled = _boneEditor.IsEditorActive
                }
                : new HeaderDrawer.Button
                {
                    Description = "Write-protect this template.",
                    Icon = FontAwesomeIcon.LockOpen,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, true),
                    Disabled = _boneEditor.IsEditorActive
                };

    private HeaderDrawer.Button ExportToClipboardButton()
        => new()
        {
            Description = "Copy the current template to your clipboard.",
            Icon = FontAwesomeIcon.Copy,
            OnClick = ExportToClipboard,
            Visible = _selector.Selected != null,
            Disabled = _boneEditor.IsEditorActive
        };

    private void DrawHeader()
        => HeaderDrawer.Draw(SelectionName, 0, Im.Color.Get(ImGuiColor.FrameBackground).Color,
            1, ExportToClipboardButton(), LockButton(),
            HeaderDrawer.Button.IncognitoButton(_selector.IncognitoMode, v => _selector.IncognitoMode = v));

    private void DrawMultiSelection()
    {
        if (_selector.SelectedPaths.Count == 0)
            return;

        var sizeType = Im.Style.FrameHeight;
        var availableSizePercent = (Im.ContentRegion.Available.X - sizeType - 4 * Im.Style.CellPadding.X) / 100;
        var sizeMods = availableSizePercent * 35;
        var sizeFolders = availableSizePercent * 65;

        Im.Line.New();
        Im.Text("Currently Selected Templates");
        Im.Separator();
        using var table = Im.Table.Begin("templates", 3, TableFlags.RowBackground);
        if (!table)
            return;

        table.SetupColumn("btn", TableColumnFlags.WidthFixed, sizeType);
        table.SetupColumn("name", TableColumnFlags.WidthFixed, sizeMods);
        table.SetupColumn("path", TableColumnFlags.WidthFixed, sizeFolders);

        var i = 0;
        foreach (var (fullName, path) in _selector.SelectedPaths.Select(p => (p.FullPath, p))
                     .OrderBy(p => p.Item1, StringComparer.OrdinalIgnoreCase))
        {
            using var id = Im.Id.Push(i++);
            table.NextColumn();
            var icon = path is IFileSystemData<Template> ? FontAwesomeIcon.FileCircleMinus : FontAwesomeIcon.FolderMinus;
            if (UiHelpers.DrawIconButton(icon, new Vector2(sizeType), "Remove from selection.", false))
                _selector.RemovePathFromMultiSelection(path);

            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text(path is IFileSystemData<Template> data ? _selector.IncognitoMode ? data.Value.Incognito : data.Value.Name.Text : string.Empty);

            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text(_selector.IncognitoMode ? "Incognito is active" : fullName);
        }
    }

    private void DrawPanel()
    {
        if (_selector.Selected == null)
            return;

        using (var disabled = Im.Disabled(_selector.Selected?.IsWriteProtected ?? true))
        {
            DrawBasicSettings();
        }

        _boneEditor.Draw();
    }

    private void DrawEditorToggle()
    {
        (bool isEditorAllowed, bool isEditorActive) = CanToggleEditor();

        var width = MathF.Min(180 * ImGuiHelpers.GlobalScale, Im.ContentRegion.Available.X);
        if (UiHelpers.DrawDisabledButton($"{(_boneEditor.IsEditorActive ? "Finish" : "Start")} bone editing", new Vector2(width, 0),
            "Toggle the bone editor for this template", !isEditorAllowed))
        {
            if (!isEditorActive)
                _boneEditor.EnableEditor(_selector.Selected!);
            else
                _boneEditor.DisableEditor();
        }
    }

    private (bool isEditorAllowed, bool isEditorActive) CanToggleEditor()
    {
        return ((!_selector.Selected?.IsWriteProtected ?? false) || _configuration.PluginEnabled, _boneEditor.IsEditorActive);
    }

    private void DrawBasicSettings()
    {
        using (var table = Im.Table.Begin("BasicSettings", 2))
        {
            if (!table)
                return;

            table.SetupColumn("Label", TableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
            table.SetupColumn("Control", TableColumnFlags.WidthStretch);

            table.NextRow();
            UiHelpers.DrawPropertyLabel("Template Name");
            table.NextColumn();
            DrawTemplateNameControl();

            table.NextRow();
            UiHelpers.DrawPropertyLabel("Bone Editor");
            table.NextColumn();
            DrawEditorToggle();
        }
    }

    private void DrawTemplateNameControl()
    {
        var name = _newName ?? _selector.Selected!.Name;
        Im.Item.SetNextWidthFull();

        if (!_selector.IncognitoMode)
        {
            if (Im.Input.Text("##Name", ref name, maxLength: 128))
            {
                _newName = name;
                _changedTemplate = _selector.Selected;
            }

            if (Im.Item.DeactivatedAfterEdit && _changedTemplate != null)
            {
                _manager.Rename(_changedTemplate, name);
                _newName = null;
                _changedTemplate = null;
            }
        }
        else
        {
            Im.Cursor.FrameAlign();
            Im.Text(_selector.Selected!.Incognito);
        }
    }

    private void ExportToClipboard()
    {
        try
        {
            Im.Clipboard.Set(Base64Helper.ExportTemplateToBase64(_selector.Selected!));
            _popupSystem.ShowPopup(PopupSystem.Messages.ClipboardDataNotLongTerm);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not copy data from template {_selector.Selected!.UniqueId} to clipboard: {ex}");
            _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
        }
    }


    private void SelectorSelectionChanged(Template? oldSelection, Template? newSelection, in TemplateFileSystemSelector.TemplateState state)
    {
        if (!_isEditorEnablePending)
            return;

        _isEditorEnablePending = false;

        _boneEditor.EnableEditor(_selector.Selected!);
    }

    private void OnEditorEvent(in TemplateEditorEvent.Arguments args)
    {
        var (type, template) = args;
        if (type != TemplateEditorEvent.Type.EditorEnableRequestedStage2)
            return;

        if(template == null)
            return;

        (bool isEditorAllowed, bool isEditorActive) = CanToggleEditor();

        if (!isEditorAllowed || isEditorActive)
            return;

        if(_selector.Selected != template)
        {
            _selector.SelectByValue(template);

            _isEditorEnablePending = true;
        }
        else
            _boneEditor.EnableEditor(_selector.Selected!);
    }
}
