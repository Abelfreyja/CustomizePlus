using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Events;
using CustomizePlus.UI.Windows.Controls;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Penumbra.GameData.Actors;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class ProfilePanel : IHeader, IPanel
{
    private readonly ProfileFileSystemSelector _selector;
    private readonly ProfileManager _manager;
    private readonly PluginConfiguration _configuration;
    private readonly TemplateCombo _templateCombo;
    private readonly TemplateEditorManager _templateEditorManager;
    private readonly ActorAssignmentUi _actorAssignmentUi;
    private readonly ActorManager _actorManager;
    private readonly TemplateEditorEvent _templateEditorEvent;
    private readonly PopupSystem _popupSystem;
    private readonly Logger _logger;

    private string? _newName;
    private int? _newPriority;
    private Profile? _changedProfile;

    private Action? _endAction;

    private int _dragIndex = -1;

    private string SelectionName
        => _selector.SelectedPaths.Count > 1
            ? "Multiple Profiles"
            : _selector.Selected == null
                ? "No Selection"
                : _selector.IncognitoMode
                    ? _selector.Selected.Incognito
                    : _selector.Selected.Name.Text;

    public ReadOnlySpan<byte> Id
        => "ProfilePanel"u8;

    public bool Collapsed
        => false;

    public ProfilePanel(
        ProfileFileSystemSelector selector,
        ProfileManager manager,
        PluginConfiguration configuration,
        TemplateCombo templateCombo,
        TemplateEditorManager templateEditorManager,
        ActorAssignmentUi actorAssignmentUi,
        ActorManager actorManager,
        TemplateEditorEvent templateEditorEvent,
        PopupSystem popupSystem,
        Logger logger)
    {
        _selector = selector;
        _manager = manager;
        _configuration = configuration;
        _templateCombo = templateCombo;
        _templateEditorManager = templateEditorManager;
        _actorAssignmentUi = actorAssignmentUi;
        _actorManager = actorManager;
        _templateEditorEvent = templateEditorEvent;
        _popupSystem = popupSystem;
        _logger = logger;
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

    private HeaderDrawer.Button LockButton()
        => _selector.Selected == null
            ? HeaderDrawer.Button.Invisible
            : _selector.Selected.IsWriteProtected
                ? new HeaderDrawer.Button
                {
                    Description = "Make this profile editable.",
                    Icon = FontAwesomeIcon.Lock,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, false)
                }
                : new HeaderDrawer.Button
                {
                    Description = "Write-protect this profile.",
                    Icon = FontAwesomeIcon.LockOpen,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, true)
                };

    private HeaderDrawer.Button ExportToClipboardButton()
         => _selector.Selected == null
        ? HeaderDrawer.Button.Invisible
        :new HeaderDrawer.Button {
            Description = "Copy the current profile combined into one template to your clipboard.",
            Icon = FontAwesomeIcon.Copy,
            OnClick = ExportToClipboard,
            Visible = _selector.Selected != null,
            Disabled = false
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
        Im.Text("Currently Selected Profiles");
        Im.Separator();
        using var table = Im.Table.Begin("profile", 3, TableFlags.RowBackground);
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
            var icon = path is IFileSystemData<Profile> ? FontAwesomeIcon.FileCircleMinus : FontAwesomeIcon.FolderMinus;
            if (UiHelpers.DrawIconButton(icon, new Vector2(sizeType), "Remove from selection.", false))
                _selector.RemovePathFromMultiSelection(path);

            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text(path is IFileSystemData<Profile> data ? _selector.IncognitoMode ? data.Value.Incognito : data.Value.Name.Text : string.Empty);

            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text(_selector.IncognitoMode ? "Incognito is active" : fullName);
        }
    }

    private void DrawPanel()
    {
        if (_selector.Selected == null)
            return;

        DrawBasicSettings();

        Im.Separator();

        using (var disabled = Im.Disabled(_selector.Selected?.IsWriteProtected ?? true))
        {
            var isShouldDraw = Im.Tree.Header("Add character");

            if (isShouldDraw)
                DrawAddCharactersArea();

            Im.Separator();

            DrawCharacterListArea();

            Im.Separator();

            DrawTemplateArea();
        }
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
            UiHelpers.DrawPropertyLabel("Enabled");
            table.NextColumn();
            DrawEnabledControl();

            table.NextRow();
            UiHelpers.DrawPropertyLabel("Profile Name");
            table.NextColumn();
            DrawProfileNameControl();

            table.NextRow();
            UiHelpers.DrawPropertyLabel("Priority");
            table.NextColumn();
            DrawPriorityControl();
        }
    }

    private void DrawEnabledControl()
    {
        var enabled = _selector.Selected?.Enabled ?? false;
        using (Im.Disabled(_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused))
        {
            if (Im.Checkbox("##Enabled", ref enabled))
                _manager.SetEnabled(_selector.Selected!, enabled);
        }

        Im.Line.Same();
        LunaStyle.DrawAlignedHelpMarker("Whether the templates in this profile should be applied at all.");
    }

    private void DrawProfileNameControl()
    {
        using var disabled = Im.Disabled(_selector.Selected?.IsWriteProtected ?? true);
        var name = _newName ?? _selector.Selected!.Name;
        Im.Item.SetNextWidthFull();

        if (!_selector.IncognitoMode)
        {
            if (Im.Input.Text("##ProfileName", ref name, maxLength: 128))
            {
                _newName = name;
                _changedProfile = _selector.Selected;
            }

            if (Im.Item.DeactivatedAfterEdit && _changedProfile != null)
            {
                _manager.Rename(_changedProfile, name);
                _newName = null;
                _changedProfile = null;
            }
        }
        else
        {
            Im.Cursor.FrameAlign();
            Im.Text(_selector.Selected!.Incognito);
        }
    }

    private void DrawPriorityControl()
    {
        using var disabled = Im.Disabled(_selector.Selected?.IsWriteProtected ?? true);
        var priority = _newPriority ?? _selector.Selected!.Priority;

        Im.Item.SetNextWidth(90 * ImGuiHelpers.GlobalScale);
        if (Im.Input.Scalar("##Priority", ref priority, "%d", 0, 0))
        {
            _newPriority = priority;
            _changedProfile = _selector.Selected;
        }

        if (Im.Item.DeactivatedAfterEdit && _changedProfile != null)
        {
            _manager.SetPriority(_changedProfile, priority);
            _newPriority = null;
            _changedProfile = null;
        }

        Im.Line.Same();
        LunaStyle.DrawAlignedHelpMarker("Profiles with a higher number here take precedence before profiles with a lower number.\n"
            + "That means if two or more profiles affect same character, profile with higher priority will be applied to that character.");
    }

     private void ExportToClipboard()
    {
        try
        {
            Im.Clipboard.Set(Base64Helper.ExportProfileToBase64(_selector.Selected!));
            _popupSystem.ShowPopup(PopupSystem.Messages.ClipboardDataNotLongTerm);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not copy data from profile {_selector.Selected!.UniqueId} to clipboard: {ex}");
            _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
        }
    }

    private void DrawAddCharactersArea()
    {
        using (var style = Im.Style.Push(ImStyleDouble.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            var width = new Vector2(Im.ContentRegion.Available.X - Im.Font.CalculateSize("Limit to my creatures").X - 68, 0);

            Im.Item.SetNextWidth(width.X);

            bool appliesToMultiple = _manager.DefaultProfile == _selector.Selected || _manager.DefaultLocalPlayerProfile == _selector.Selected;
            using (Im.Disabled(appliesToMultiple))
            {
                _actorAssignmentUi.DrawWorldCombo(width.X / 2);
                Im.Line.Same();
                _actorAssignmentUi.DrawPlayerInput(width.X / 2);

                var buttonWidth = new Vector2(165 * ImGuiHelpers.GlobalScale - Im.Style.ItemSpacing.X / 2, 0);

                if (UiHelpers.DrawDisabledButton("Apply to player character", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetPlayer))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.PlayerIdentifier);

                Im.Line.Same();

                if (UiHelpers.DrawDisabledButton("Apply to retainer", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetRetainer))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.RetainerIdentifier);

                Im.Line.Same();

                if (UiHelpers.DrawDisabledButton("Apply to mannequin", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetMannequin))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.MannequinIdentifier);

                var currentPlayer = _actorManager.GetCurrentPlayer().CreatePermanent();
                if (UiHelpers.DrawDisabledButton("Apply to current character", buttonWidth, string.Empty, !currentPlayer.IsValid))
                    _manager.AddCharacter(_selector.Selected!, currentPlayer);

                Im.Separator();

                _actorAssignmentUi.DrawObjectKindCombo(width.X / 2);
                Im.Line.Same();
                _actorAssignmentUi.DrawNpcInput(width.X / 2);

                if (UiHelpers.DrawDisabledButton("Apply to selected NPC", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetNpc))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.NpcIdentifier);
            }
        }
    }

    private void DrawCharacterListArea()
    {
        var isDefaultLP = _manager.DefaultLocalPlayerProfile == _selector.Selected;
        var isDefaultLPOrCurrentProfilesEnabled = (_manager.DefaultLocalPlayerProfile?.Enabled ?? false) || (_selector.Selected?.Enabled ?? false);
        using (Im.Disabled(isDefaultLPOrCurrentProfilesEnabled))
        {
            if (Im.Checkbox("##DefaultLocalPlayerProfile", ref isDefaultLP))
                _manager.SetDefaultLocalPlayerProfile(isDefaultLP ? _selector.Selected! : null);
            LunaStyle.DrawAlignedHelpMarkerLabel("Apply to any character you are logged in with",
                "Whether the templates in this profile should be applied to any character you are currently logged in with.\r\nTakes priority over the next option for said character.\r\nThis setting cannot be applied to multiple profiles.");
        }
        if (isDefaultLPOrCurrentProfilesEnabled)
        {
            Im.Line.Same();
            using var warning = ImGuiColor.Text.Push(Constants.Colors.Warning);
            UiHelpers.DrawIcon(FontAwesomeIcon.ExclamationTriangle);
            UiHelpers.DrawHoverTooltip("Can only be changed when both currently selected and profile where this checkbox is checked are disabled.");
        }

        Im.Line.Same();
        using(Im.Disabled(true))
            Im.Button("##splitter", new Vector2(1, Im.Style.FrameHeight));
        Im.Line.Same();

        var isDefault = _manager.DefaultProfile == _selector.Selected;
        var isDefaultOrCurrentProfilesEnabled = (_manager.DefaultProfile?.Enabled ?? false) || (_selector.Selected?.Enabled ?? false);
        using (Im.Disabled(isDefaultOrCurrentProfilesEnabled))
        {
            if (Im.Checkbox("##DefaultProfile", ref isDefault))
                _manager.SetDefaultProfile(isDefault ? _selector.Selected! : null);
            LunaStyle.DrawAlignedHelpMarkerLabel("Apply to all players and retainers",
                "Whether the templates in this profile are applied to all players and retainers without a specific profile.\r\nThis setting cannot be applied to multiple profiles.");
        }
        if (isDefaultOrCurrentProfilesEnabled)
        {
            Im.Line.Same();
            using var warning = ImGuiColor.Text.Push(Constants.Colors.Warning);
            UiHelpers.DrawIcon(FontAwesomeIcon.ExclamationTriangle);
            UiHelpers.DrawHoverTooltip("Can only be changed when both currently selected and profile where this checkbox is checked are disabled.");
        }
        bool appliesToMultiple = _manager.DefaultProfile == _selector.Selected || _manager.DefaultLocalPlayerProfile == _selector.Selected;

        Im.Separator();

        using var dis = Im.Disabled(appliesToMultiple);
        using var table = Im.Table.Begin("CharacterTable", 2, TableFlags.RowBackground | TableFlags.ScrollX | TableFlags.ScrollY, new Vector2(Im.ContentRegion.Available.X, 200));
        if (!table)
            return;

        table.SetupColumn("##charaDel", TableColumnFlags.WidthFixed, Im.Style.FrameHeight);
        table.SetupColumn("Character", TableColumnFlags.WidthFixed, 320 * ImGuiHelpers.GlobalScale);
        table.HeaderRow();

        if (appliesToMultiple)
        {
            table.NextColumn();
            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text("Applies to multiple targets");
            return;
        }

        //warn: .ToList() might be performance critical at some point
        //the copying via ToList is done because manipulations with .Templates list result in "Collection was modified" exception here
        var charas = _selector.Selected!.Characters.Select((character, idx) => (character, idx)).ToList();

        if (charas.Count == 0)
        {
            table.NextColumn();
            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text("No characters are associated with this profile");
        }

        foreach (var (character, idx) in charas)
        {
            using var id = Im.Id.Push(idx);
            table.NextColumn();
            var keyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
            var tt = keyValid
                ? "Remove this character from the profile."
                : $"Remove this character from the profile.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

            if (UiHelpers.DrawIconButton(FontAwesomeIcon.Trash, new Vector2(Im.Style.FrameHeight), tt, !keyValid))
                _endAction = () => _manager.DeleteCharacter(_selector.Selected!, character);
            table.NextColumn();
            Im.Cursor.FrameAlign();
            Im.Text(!_selector.IncognitoMode ? $"{character.ToNameWithoutOwnerName()}{character.TypeToString()}" : "Incognito");

            var profiles = _manager.GetEnabledProfilesByActor(character).ToList();
            if (profiles.Count > 1)
            {
                //todo: make helper
                Im.Line.Same();
                if (profiles.Any(x => x.IsTemporary))
                {
                    using var color = ImGuiColor.Text.Push(Constants.Colors.Error);
                    UiHelpers.DrawIcon(FontAwesomeIcon.Lock);
                }
                else if (profiles[0] != _selector.Selected!)
                {
                    using var color = ImGuiColor.Text.Push(Constants.Colors.Warning);
                    UiHelpers.DrawIcon(FontAwesomeIcon.ExclamationTriangle);
                }
                else
                {
                    using var color = ImGuiColor.Text.Push(Constants.Colors.Info);
                    UiHelpers.DrawIcon(FontAwesomeIcon.Star);
                }

                if (profiles.Any(x => x.IsTemporary))
                    UiHelpers.DrawHoverTooltip("This character is being affected by temporary profile set by external plugin. This profile will not be applied!");
                else
                    UiHelpers.DrawHoverTooltip(profiles[0] != _selector.Selected! ? "Several profiles are trying to affect this character. This profile will not be applied!" :
                        "Several profiles are trying to affect this character. This profile is being applied.");
            }
        }

        _endAction?.Invoke();
        _endAction = null;
    }

    private void DrawTemplateArea()
    {
        using var table = Im.Table.Begin("TemplateTable", 5, TableFlags.RowBackground | TableFlags.ScrollX | TableFlags.ScrollY);
        if (!table)
            return;

        table.SetupColumn("##del", TableColumnFlags.WidthFixed, Im.Style.FrameHeight);
        table.SetupColumn("##Index", TableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);
        table.SetupColumn("##Enabled", TableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);

        table.SetupColumn("Template", TableColumnFlags.WidthFixed, 220 * ImGuiHelpers.GlobalScale);

        table.SetupColumn("##editbtn", TableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);

        table.HeaderRow();

        //warn: .ToList() might be performance critical at some point
        //the copying via ToList is done because manipulations with .Templates list result in "Collection was modified" exception here
        foreach (var (template, idx) in _selector.Selected!.Templates.Select((template, idx) => (template, idx)).ToList())
        {
            using var id = Im.Id.Push(idx);
            table.NextColumn();
            var keyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
            var tt = keyValid
                ? "Remove this template from the profile."
                : $"Remove this template from the profile.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

            if (UiHelpers.DrawIconButton(FontAwesomeIcon.Trash, new Vector2(Im.Style.FrameHeight), tt, !keyValid))
                _endAction = () => _manager.DeleteTemplate(_selector.Selected!, idx);
            table.NextColumn();
            Im.Selectable($"#{idx + 1:D2}");
            DrawDragDrop(_selector.Selected!, idx);

            table.NextColumn();
            var enabled = !_selector.Selected!.DisabledTemplates.Contains(template.UniqueId);
            if (Im.Checkbox("##EnableCheckbox", ref enabled))
                _manager.ToggleTemplate(_selector.Selected!, idx);
            UiHelpers.DrawHoverTooltip("Whether this template is applied to the profile.");

            table.NextColumn();

            _templateCombo.Draw(_selector.Selected!, template, idx);

            DrawDragDrop(_selector.Selected!, idx);

            table.NextColumn();

            var disabledCondition = _templateEditorManager.IsEditorActive || template.IsWriteProtected;

            if (UiHelpers.DrawIconButton(FontAwesomeIcon.Edit, new Vector2(Im.Style.FrameHeight), "Open this template in the template editor.", disabledCondition))
                _templateEditorEvent.Invoke(new TemplateEditorEvent.Arguments(TemplateEditorEvent.Type.EditorEnableRequested, template));

            if (disabledCondition)
            {
                //todo: make helper
                Im.Line.Same();
                using var warning = ImGuiColor.Text.Push(Constants.Colors.Warning);
                UiHelpers.DrawIcon(FontAwesomeIcon.ExclamationTriangle);
                UiHelpers.DrawHoverTooltip("This template cannot be edited because it is either write protected or you are already editing one of the templates.");
            }
        }

        table.NextColumn();
        table.NextColumn();
        table.NextColumn();
        Im.Cursor.FrameAlign();
        Im.Text("New");
        table.NextColumn();
        _templateCombo.Draw(_selector.Selected!, null, -1);
        table.NextRow();

        _endAction?.Invoke();
        _endAction = null;
    }

    private void DrawDragDrop(Profile profile, int index)
    {
        const string dragDropLabel = "TemplateDragDrop";
        using (var target = Im.DragDrop.Target())
        {
            if (target.Success && UiHelpers.IsDragDropPayload(dragDropLabel))
            {
                if (_dragIndex >= 0)
                {
                    var idx = _dragIndex;
                    _endAction = () => _manager.MoveTemplate(profile, idx, index);
                }

                _dragIndex = -1;
            }
        }

        using (var source = Im.DragDrop.Source())
        {
            if (source)
            {
                Im.Text($"Moving template #{index + 1:D2}...");
                if (source.SetPayload(dragDropLabel))
                {
                    _dragIndex = index;
                }
            }
        }
    }

    private void UpdateIdentifiers()
    {

    }
}
