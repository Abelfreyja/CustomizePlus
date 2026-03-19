using CustomizePlus.Armatures.Data;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Game.Services;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Data;
using CustomizePlus.UI.Windows.Controls;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using OtterGui;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;

public class BoneEditorPanel
{
    private readonly TemplateFileSystemSelector _templateFileSystemSelector;
    private readonly TemplateEditorManager _editorManager;
    private readonly TemplateEditorService _editorService;
    private readonly PluginConfiguration _configuration;
    private readonly GameObjectService _gameObjectService;
    private readonly ActorAssignmentUi _actorAssignmentUi;
    private readonly PopupSystem _popupSystem;
    private readonly Logger _logger;

    private BoneAttribute _editingAttribute => _editorService.ActiveAttribute;
    private int _precision;

    private bool _isShowLiveBones;
    private bool _isMirrorModeEnabled => _editorService.MirrorMode;
    private bool _isGizmoEnabled => _editorService.GizmoEnabled;

    private Dictionary<BoneData.BoneFamily, bool> _groupExpandedState = new();

    private bool _openSavePopup;

    private bool _isUnlocked = false;

    private string _boneSearch = string.Empty;
    private bool _boneEditorSliderActive;

    private readonly HashSet<string> _propagationHighlights = new(StringComparer.Ordinal);
    private readonly HashSet<string> _propagationSources = new(StringComparer.Ordinal);

    private float _propagateButtonXPos = 0;
    private float _parentRowScreenPosY = 0;

    private static readonly Vector4 AttributeButtonActiveColor = new(0.28f, 0.5f, 0.8f, 0.7f);
    private static readonly Vector4 AttributeButtonHoverColor = new(0.35f, 0.6f, 0.9f, 0.85f);
    private static readonly Vector4 AttributeButtonPressedColor = new(0.22f, 0.45f, 0.75f, 0.85f);
    private static readonly Vector4 ToggleButtonActiveColor = new(0.25f, 0.65f, 0.4f, 0.65f);
    private static readonly Vector4 ToggleButtonHoverColor = new(0.3f, 0.75f, 0.45f, 0.8f);
    private static readonly Vector4 ToggleButtonPressedColor = new(0.2f, 0.6f, 0.35f, 0.8f);

    private IReadOnlySet<string> _favoriteBones => _editorService.FavoriteBones;

    private string? _pendingClipboardText;
    private string? _pendingImportText;
    public bool HasChanges => _editorManager.HasChanges;
    public bool IsEditorActive => _editorManager.IsEditorActive;
    public bool IsEditorPaused => _editorManager.IsEditorPaused;
    public bool IsCharacterFound => _editorManager.IsCharacterFound;

    public BoneEditorPanel(
        TemplateFileSystemSelector templateFileSystemSelector,
        TemplateEditorManager editorManager,
        TemplateEditorService editorService,
        PluginConfiguration configuration,
        GameObjectService gameObjectService,
        ActorAssignmentUi actorAssignmentUi,
        Logger logger)
    {
        _templateFileSystemSelector = templateFileSystemSelector;
        _editorManager = editorManager;
        _editorService = editorService;
        _configuration = configuration;
        _gameObjectService = gameObjectService;
        _actorAssignmentUi = actorAssignmentUi;
        _logger = logger;

        _isShowLiveBones = configuration.EditorConfiguration.ShowLiveBones;
        _precision = configuration.EditorConfiguration.EditorValuesPrecision;
    }

    public bool EnableEditor(Template template)
    {
        if (_editorManager.EnableEditor(template))
        {
            //_editorManager.SetLimitLookupToOwned(_configuration.EditorConfiguration.LimitLookupToOwnedObjects);
            return true;
        }

        return false;
    }

    public bool DisableEditor()
    {
        if (!_editorManager.HasChanges)
            return _editorManager.DisableEditor();

        if (_editorManager.HasChanges && !IsEditorActive)
            throw new Exception("Invalid state in BoneEditorPanel: has changes but editor is not active");

        _openSavePopup = true;

        return false;
    }

    public void Draw()
    {
        _isUnlocked = IsCharacterFound && IsEditorActive && !IsEditorPaused;
        _boneEditorSliderActive = false;

        DrawEditorConfirmationPopup();

        ImGui.Separator();

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            string characterText = null!;

            if (_templateFileSystemSelector.IncognitoMode)
                characterText = "Previewing on: incognito active";
            else
                characterText = _editorManager.Character.IsValid ? $"Previewing on: {(_editorManager.Character.Type == Penumbra.GameData.Enums.IdentifierType.Owned ?
                _editorManager.Character.ToNameWithoutOwnerName() : _editorManager.Character.ToString())}" : "No valid character selected";

            ImGuiUtil.PrintIcon(FontAwesomeIcon.User);
            ImGui.SameLine();
            ImGui.Text(characterText);

            ImGui.Separator();

            var isShouldDraw = ImGui.CollapsingHeader("Change preview character");

            if (isShouldDraw)
            {
                var width = new Vector2(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Limit to my creatures").X - 68, 0);

                using (var disabled = ImRaii.Disabled(!IsEditorActive || IsEditorPaused))
                {
                    if (!_templateFileSystemSelector.IncognitoMode)
                    {
                        _actorAssignmentUi.DrawWorldCombo(width.X / 2);
                        ImGui.SameLine();
                        _actorAssignmentUi.DrawPlayerInput(width.X / 2);

                        var buttonWidth = new Vector2((165 * ImGuiHelpers.GlobalScale) - (ImGui.GetStyle().ItemSpacing.X / 2), 0);

                        if (ImGuiUtil.DrawDisabledButton("Apply to player character", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetPlayer))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.PlayerIdentifier);

                        ImGui.SameLine();

                        if (ImGuiUtil.DrawDisabledButton("Apply to retainer", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetRetainer))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.RetainerIdentifier);

                        ImGui.SameLine();

                        if (ImGuiUtil.DrawDisabledButton("Apply to mannequin", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetMannequin))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.MannequinIdentifier);

                        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
                        if (ImGuiUtil.DrawDisabledButton("Apply to current character", buttonWidth, string.Empty, !currentPlayer.IsValid))
                            _editorManager.ChangeEditorCharacter(currentPlayer);

                        ImGui.Separator();

                        _actorAssignmentUi.DrawObjectKindCombo(width.X / 2);
                        ImGui.SameLine();
                        _actorAssignmentUi.DrawNpcInput(width.X / 2);

                        if (ImGuiUtil.DrawDisabledButton("Apply to selected NPC", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetNpc))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.NpcIdentifier);
                    }
                    else
                        ImGui.TextUnformatted("Incognito active");
                }
            }

            ImGui.Separator();

            using (var table = ImRaii.Table("BoneEditorMenu", 2))
            {
                ImGui.TableSetupColumn("Attributes", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Space", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                if (DrawAttributeButton(BoneAttribute.Position, FontAwesomeIcon.ArrowsAlt, "Position",
                        "May have unintended effects. Edit at your own risk!"))
                    ApplyEditorAttribute(BoneAttribute.Position);

                ImGui.SameLine();
                if (DrawAttributeButton(BoneAttribute.Rotation, FontAwesomeIcon.Sync, "Rotation",
                        "May have unintended effects. Edit at your own risk!"))
                    ApplyEditorAttribute(BoneAttribute.Rotation);

                ImGui.SameLine();
                if (DrawAttributeButton(BoneAttribute.Scale, FontAwesomeIcon.CompressArrowsAlt, "Scale"))
                    ApplyEditorAttribute(BoneAttribute.Scale);

                ImGui.SameLine();
                ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                ImGui.InputTextWithHint("##BoneSearch", "Search bones...", ref _boneSearch, 64);

                ImGui.SameLine();
                ImGui.BeginDisabled(!_editorService.CanUndo);
                if (ImGuiComponents.IconButton("##UndoBone", FontAwesomeIcon.Undo))
                {
                    _editorService.Undo();
                }
                ImGui.EndDisabled();
                CtrlHelper.AddHoverText("Undo");

                ImGui.SameLine();
                ImGui.BeginDisabled(!_editorService.CanRedo);
                if (ImGuiComponents.IconButton("##RedoBone", FontAwesomeIcon.Redo))
                {
                    _editorService.Redo();
                }
                ImGui.EndDisabled();
                CtrlHelper.AddHoverText("Redo");

                using (var disabled = ImRaii.Disabled(!_isUnlocked))
                {
                    ImGui.SameLine();
                    if (DrawToggleButton("ShowLiveBones", FontAwesomeIcon.Eye, _isShowLiveBones, "Show Live Bones",
                            "If selected, present for editing all bones found in the game data, else show only bones for which the profile already contains edits."))
                    {
                        _isShowLiveBones = !_isShowLiveBones;
                        _configuration.EditorConfiguration.ShowLiveBones = _isShowLiveBones;
                        _configuration.Save();
                    }

                    ImGui.SameLine();
                    using (var mirrorDisabled = ImRaii.Disabled(!_isShowLiveBones))
                    {
                        if (DrawToggleButton("MirrorMode", FontAwesomeIcon.GripLinesVertical, _isMirrorModeEnabled, "Mirror Mode",
                                "Bone changes will be reflected from left to right and vice versa"))
                            ApplyMirrorMode(!_isMirrorModeEnabled);
                    }

                    ImGui.SameLine();
                    if (DrawToggleButton("GizmoEnabled", FontAwesomeIcon.Crosshairs, _isGizmoEnabled, "Gizmo",
                            "Toggle the gizmo overlay and interaction."))
                        ApplyGizmoEnabled(!_isGizmoEnabled);
                }

                ImGui.TableNextColumn();

                if (ImGui.SliderInt("##Precision", ref _precision, 0, 6, $"{_precision} Place{(_precision == 1 ? "" : "s")}"))
                {
                    _configuration.EditorConfiguration.EditorValuesPrecision = _precision;
                    _configuration.Save();
                }
                CtrlHelper.AddHoverText("Level of precision to display while editing values");
            }

            ImGui.Separator();

            using (var table = ImRaii.Table($"BoneEditorContents", 6, ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.BordersV | ImGuiTableFlags.ScrollY))
            {
                if (table)
                {
                    var col1Label = _editingAttribute == BoneAttribute.Rotation ? "Roll" : "X";
                    var col2Label = _editingAttribute == BoneAttribute.Rotation ? "Pitch" : "Y";
                    var col3Label = _editingAttribute == BoneAttribute.Rotation ? "Yaw" : "Z";
                    var col4Label = _editingAttribute == BoneAttribute.Scale ? "All" : "N/A";

                    ImGui.TableSetupColumn("Bones", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthFixed, 6 * CtrlHelper.IconButtonWidth);

                    ImGui.TableSetupColumn($"{col1Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn($"{col2Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn($"{col3Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn($"{col4Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetColumnEnabled(4, _editingAttribute == BoneAttribute.Scale);

                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableHeadersRow();

                    IEnumerable<EditRowParams> relevantModelBones = null!;
                    if (_editorManager.IsEditorActive && _editorManager.EditorProfile != null && _editorManager.EditorProfile.Armatures.Count > 0)
                        relevantModelBones = _isShowLiveBones && _editorManager.EditorProfile.Armatures.Count > 0
                            ? _editorManager.EditorProfile.Armatures[0].GetAllBones().DistinctBy(x => x.BoneName).Select(x => new EditRowParams(x))
                            : _editorManager.EditorProfile.Armatures[0].BoneTemplateBinding.Where(x => x.Value.Bones.ContainsKey(x.Key))
                                .Select(x => new EditRowParams(x.Key, x.Value.Bones[x.Key])); //todo: this is awful
                    else
                        relevantModelBones = _templateFileSystemSelector.Selected!.Bones.Select(x => new EditRowParams(x.Key, x.Value));

                    if (!string.IsNullOrEmpty(_boneSearch))
                    {
                        relevantModelBones = relevantModelBones
                            .Where(x => x.BoneDisplayName.Contains(_boneSearch, StringComparison.OrdinalIgnoreCase)
                                     || x.BoneCodeName.Contains(_boneSearch, StringComparison.OrdinalIgnoreCase));
                    }

                    var favoriteRows = relevantModelBones
                        .Where(b => _favoriteBones.Contains(b.BoneCodeName))
                        .OrderBy(b => BoneData.GetBoneRanking(b.BoneCodeName))
                        .ToList();

                    var nonFavoriteRows = relevantModelBones
                        .Where(b => !_favoriteBones.Contains(b.BoneCodeName))
                        .ToList();

                    UpdatePropagationHighlights(favoriteRows.Concat(nonFavoriteRows));

                    var groupedBones = nonFavoriteRows
                        .GroupBy(x => BoneData.GetBoneFamily(x.BoneCodeName));

                    if (favoriteRows.Count > 0)
                    {
                        const string favoritesHeaderId = "FavoritesHeader";

                        if (!_groupExpandedState.TryGetValue((BoneData.BoneFamily)(-1), out var expanded))
                            _groupExpandedState[(BoneData.BoneFamily)(-1)] = expanded = true;

                        if (expanded)
                            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                        else
                            ImGui.TableNextRow();

                        using var id = ImRaii.PushId(favoritesHeaderId);
                        ImGui.TableNextColumn();
                        CtrlHelper.ArrowToggle($"##{favoritesHeaderId}", ref expanded);
                        ImGui.SameLine();
                        CtrlHelper.StaticLabel("Favorites");

                        if (expanded)
                        {
                            ImGui.TableNextRow();
                            foreach (var erp in favoriteRows)
                            {
                                var family = BoneData.GetBoneFamily(erp.BoneCodeName);
                                CompleteBoneEditor(family, erp);
                            }
                        }

                        _groupExpandedState[(BoneData.BoneFamily)(-1)] = expanded;
                    }

                    foreach (var boneGroup in groupedBones.OrderBy(x => (int)x.Key))
                    {
                        if (!string.IsNullOrEmpty(_pendingImportText))
                        {
                            _logger.Debug("check import text 1: " + (_pendingImportText));
                            try
                            {
                                var importedBones = Base64Helper.ImportEditedBonesFromBase64(_pendingImportText);
                                if (importedBones != null)
                                {
                                    foreach (var boneData in importedBones)
                                    {
                                        _editorManager.ModifyBoneTransform(
                                            boneData.BoneCodeName,
                                            new BoneTransform
                                            {
                                                Translation = boneData.Translation,
                                                Rotation = boneData.Rotation,
                                                Scaling = boneData.Scaling,
                                                ChildScaling = boneData.ChildScaling,
                                                ChildScalingIndependent = boneData.ChildScalingIndependent,
                                                PropagateTranslation = boneData.PropagateTranslation,
                                                PropagateRotation = boneData.PropagateRotation,
                                                PropagateScale = boneData.PropagateScale
                                            }
                                        );
                                    }
                                }
                            }
                            catch {  }
                            finally
                            {
                                _pendingImportText = null;
                            }
                        }

                        //Hide root bone if it's not enabled in settings or if we are in rotation mode
                        if (boneGroup.Key == BoneData.BoneFamily.Root &&
                            (!_configuration.EditorConfiguration.RootPositionEditingEnabled ||
                                _editingAttribute == BoneAttribute.Rotation))
                            continue;

                        //create a dropdown entry for the family if one doesn't already exist
                        //mind that it'll only be rendered if bones exist to fill it
                        if (!_groupExpandedState.TryGetValue(boneGroup.Key, out var expanded))
                        {
                            _groupExpandedState[boneGroup.Key] = false;
                            expanded = false;
                        }

                        if (expanded)
                        {
                            //paint the row in header colors if it's expanded
                            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                        }
                        else
                        {
                            ImGui.TableNextRow();
                        }

                        using var id = ImRaii.PushId(boneGroup.Key.ToString());
                        ImGui.TableNextColumn();

                        CtrlHelper.ArrowToggle($"##{boneGroup.Key}", ref expanded);
                        ImGui.SameLine();
                        CtrlHelper.StaticLabel(boneGroup.Key.ToString());
                        if (BoneData.DisplayableFamilies.TryGetValue(boneGroup.Key, out var tip) && tip != null)
                            CtrlHelper.AddHoverText(tip);

                        // sigma
                        var rowMin = ImGui.GetItemRectMin();
                        var rowMax = new Vector2(ImGui.GetContentRegionAvail().X + rowMin.X, ImGui.GetItemRectMax().Y);

                        if (ImGui.IsMouseHoveringRect(rowMin, rowMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            ImGui.OpenPopup($"GroupContext##{boneGroup.Key}");
                        }

                        if (ImGui.BeginPopup($"GroupContext##{boneGroup.Key}"))
                        {
                            using (var disabled = ImRaii.Disabled(!_isUnlocked))
                            {
                                if (ImGui.MenuItem("Copy Group"))
                                {
                                    try
                                    {
                                        var editedBones = boneGroup
                                            .Where(b => b.Transform != null && b.Transform.IsEdited())
                                            .Select(b => (b.BoneCodeName, b.Transform))
                                            .ToList();

                                        if (editedBones.Count > 0)
                                        {
                                            _pendingClipboardText = Base64Helper.ExportEditedBonesToBase64(editedBones);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
                                    }
                                }

                            if (ImGui.MenuItem("Import Group"))
                            {
                                var clipboardText = ImUtf8.GetClipboardText();
                                if (!string.IsNullOrEmpty(clipboardText))
                                    _pendingImportText = clipboardText;
                            }
                        }

                            ImGui.EndPopup();
                        }

                        if (expanded)
                        {
                            ImGui.TableNextRow();
                            foreach (var erp in boneGroup.OrderBy(x => BoneData.GetBoneRanking(x.BoneCodeName)))
                            {
                                CompleteBoneEditor(boneGroup.Key, erp);
                            }
                        }

                        _groupExpandedState[boneGroup.Key] = expanded;
                    }
                }
            }
        }

        _editorService.SetBoneEditorSliderActive(_boneEditorSliderActive);

        if (!string.IsNullOrEmpty(_pendingClipboardText))
        {
            try
            {
                ImUtf8.SetClipboardText(_pendingClipboardText);
                _logger.Debug("copied to clipboard: " + _pendingClipboardText);
            }
            catch (Exception ex)
            {
                _logger.Debug("clipboard blew up :(");
            }
            _pendingClipboardText = null;
        }

    }

    private void DrawEditorConfirmationPopup()
    {
        if (_openSavePopup)
        {
            ImGui.OpenPopup("SavePopup");
            _openSavePopup = false;
        }

        var viewportSize = ImGui.GetWindowViewport().Size;
        ImGui.SetNextWindowSize(new Vector2(viewportSize.X / 4, viewportSize.Y / 12));
        ImGui.SetNextWindowPos(viewportSize / 2, ImGuiCond.Always, new Vector2(0.5f));
        using var popup = ImRaii.Popup("SavePopup", ImGuiWindowFlags.Modal);
        if (!popup)
            return;

        ImGui.SetCursorPos(new Vector2((ImGui.GetWindowWidth() / 4) - 40, ImGui.GetWindowHeight() / 4));
        ImGuiUtil.TextWrapped("You have unsaved changes in current template, what would you like to do?");

        var buttonWidth = new Vector2(150 * ImGuiHelpers.GlobalScale, 0);
        var yPos = ImGui.GetWindowHeight() - (2 * ImGui.GetFrameHeight());
        var xPos = ((ImGui.GetWindowWidth() - ImGui.GetStyle().ItemSpacing.X) / 4) - buttonWidth.X;
        ImGui.SetCursorPos(new Vector2(xPos, yPos));

        var ExitedEditor = false;

        if (ImGui.Button("Save", buttonWidth))
        {
            _editorManager.SaveChangesAndDisableEditor();
            ExitedEditor = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Save as a copy", buttonWidth))
        {
            _editorManager.SaveChangesAndDisableEditor(true);
            ExitedEditor = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Do not save", buttonWidth))
        {
            _editorManager.DisableEditor();
            ExitedEditor = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Keep editing", buttonWidth))
        {
            ImGui.CloseCurrentPopup();
        }

        if (ExitedEditor)
            _editorService.ClearHistory();
    }

    #region ImGui helper functions

    private static Vector2 GetAttributeButtonSize()
    {
        var height = ImGui.GetFrameHeight();
        return new Vector2(height * 2.9f, height);
    }

    private static Vector2 GetToggleButtonSize()
    {
        var height = ImGui.GetFrameHeight();
        return new Vector2(height * 1.4f, height);
    }

    private bool DrawAttributeButton(BoneAttribute attribute, FontAwesomeIcon icon, string title, string? description = null)
    {
        var isActive = _editingAttribute == attribute;
        var size = GetAttributeButtonSize();
        bool clicked;
        using (var colors = ImRaii.PushColor(ImGuiCol.Button, AttributeButtonActiveColor, isActive)
                   .Push(ImGuiCol.ButtonHovered, AttributeButtonHoverColor, isActive)
                   .Push(ImGuiCol.ButtonActive, AttributeButtonPressedColor, isActive))
        using (var align = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f)))
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            clicked = ImGui.Button($"{icon.ToIconString()}##attribute-{attribute}", size);
        }
        DrawControlTooltip(title, description);
        return clicked;
    }

    private bool DrawToggleButton(string id, FontAwesomeIcon icon, bool isActive, string title, string description)
    {
        var size = GetToggleButtonSize();
        bool clicked;
        using (var colors = ImRaii.PushColor(ImGuiCol.Button, ToggleButtonActiveColor, isActive)
                   .Push(ImGuiCol.ButtonHovered, ToggleButtonHoverColor, isActive)
                   .Push(ImGuiCol.ButtonActive, ToggleButtonPressedColor, isActive))
        using (var align = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f)))
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            clicked = ImGui.Button($"{icon.ToIconString()}##{id}", size);
        }
        DrawControlTooltip(title, description);
        return clicked;
    }

    private static void DrawControlTooltip(string title, string? description = null)
    {
        if (!ImGui.IsItemHovered())
            return;

        var wrapWidth = ImGui.GetFontSize() * 32f;
        var maxWidth = wrapWidth + (ImGui.GetStyle().WindowPadding.X * 2f);
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(maxWidth, float.MaxValue));
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(title);
        if (!string.IsNullOrEmpty(description))
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
            ImGui.TextUnformatted(description);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTooltip();
    }

    private bool ResetBoneButton(EditRowParams bone)
    {
        var output = ImGuiComponents.IconButton(bone.BoneCodeName, FontAwesomeIcon.Recycle);
        CtrlHelper.AddHoverText(
            $"Reset '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' to default {_editingAttribute} values");

        if (output)
        {
            var twin = bone.Basis?.TwinBone?.BoneName;
            _editorService.ResetBoneAttribute(bone.BoneCodeName, twin, _editingAttribute);
        }

        return output;
    }

    private bool RevertBoneButton(EditRowParams bone)
    {
        var output = ImGuiComponents.IconButton(bone.BoneCodeName, FontAwesomeIcon.ArrowCircleLeft);
        CtrlHelper.AddHoverText(
            $"Revert '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' to last saved {_editingAttribute} values");

        if (output)
        {
            var twin = bone.Basis?.TwinBone?.BoneName;
            _editorService.RevertBoneAttribute(bone.BoneCodeName, twin, _editingAttribute);
        }

        return output;
    }

    private bool PropagateCheckbox(EditRowParams bone, ref bool enabled)
    {
        const FontAwesomeIcon icon = FontAwesomeIcon.Link;
        var id = $"##Propagate{bone.BoneCodeName}";

        if (enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Active);

        var output = ImGuiComponents.IconButton(id, icon);
        CtrlHelper.AddHoverText(
            $"Apply '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' transformations to its child bones");

        if (enabled)
            ImGui.PopStyleColor();

        if (output)
            enabled = !enabled;

        return output;
    }

    private bool FavoriteButton(EditRowParams bone)
    {
        var isFavorite = _editorService.IsFavoriteBone(bone.BoneCodeName);

        const FontAwesomeIcon icon = FontAwesomeIcon.Star;
        var id = $"##Favorite{bone.BoneCodeName}";

        if (isFavorite)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Favorite);

        var output = ImGuiComponents.IconButton(id, icon);

        if (isFavorite)
            ImGui.PopStyleColor();

        CtrlHelper.AddHoverText(
            $"Toggle favorite on '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' bone");

        if (output)
        {
            _editorService.ToggleFavorite(bone.BoneCodeName);
            isFavorite = _editorService.IsFavoriteBone(bone.BoneCodeName);
        }

        return isFavorite;
    }

    private static (float Velocity, float Min, float Max) GetSliderSettings(BoneAttribute attribute)
        => attribute == BoneAttribute.Rotation
            ? (0.1f, -360f, 360f)
            : (0.001f, -10f, 10f);

    private bool FullBoneSlider(string label, ref Vector3 value)
    {
        var (velocity, minValue, maxValue) = GetSliderSettings(_editingAttribute);

        var temp = _editingAttribute switch
        {
            BoneAttribute.Position => 0.0f,
            BoneAttribute.Rotation => 0.0f,
            _ => value.X == value.Y && value.Y == value.Z ? value.X : 1.0f
        };


        ImGui.PushItemWidth(ImGui.GetColumnWidth());
        var changed = ImGui.DragFloat(label, ref temp, velocity, minValue, maxValue, $"%.{_precision}f");
        if (ImGui.IsItemActive())
            _boneEditorSliderActive = true;

        if (changed)
        {
            value = new Vector3(temp, temp, temp);
            return true;

        }

        return false;
    }

    private bool SingleValueSlider(string label, ref float value)
    {
        var (velocity, minValue, maxValue) = GetSliderSettings(_editingAttribute);

        ImGui.PushItemWidth(ImGui.GetColumnWidth());
        var temp = value;
        var changed = ImGui.DragFloat(label, ref temp, velocity, minValue, maxValue, $"%.{_precision}f");
        if (ImGui.IsItemActive())
            _boneEditorSliderActive = true;

        if (changed)
        {
            value = temp;
            return true;
        }

        return false;
    }

    private void CompleteBoneEditor(BoneData.BoneFamily boneFamily, EditRowParams bone)
    {
        var codename = bone.BoneCodeName;

        var isPropagationAffected = _propagationHighlights.Contains(codename);
        Vector4 sliderHighlightColor = Vector4.Zero;

        if (isPropagationAffected)
        {
            sliderHighlightColor = GetPropagationHighlightVector(boneFamily, codename);
            var highlightColor = ImGui.ColorConvertFloat4ToU32(sliderHighlightColor);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, highlightColor);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, highlightColor);
        }

        var displayName = bone.BoneDisplayName;
        var transform = new BoneTransform(bone.Transform);

        var newVector = transform.GetValueForAttribute(_editingAttribute);
        var propagationEnabled = transform.IsPropagationEnabledForAttribute(_editingAttribute);

        bool valueChanged = false;

        bool isFavorite = false;

        using var id = ImRaii.PushId(codename);
        ImGui.TableNextColumn();
        _parentRowScreenPosY = ImGui.GetCursorScreenPos().Y;
        using (var disabled = ImRaii.Disabled(!_isUnlocked))
        {
            ImGui.Dummy(new Vector2(CtrlHelper.IconButtonWidth * 0.75f, 0));
            ImGui.SameLine();
            ResetBoneButton(bone);
            ImGui.SameLine();
            RevertBoneButton(bone);
            ImGui.SameLine();

            _propagateButtonXPos = ImGui.GetCursorPosX();
            if (PropagateCheckbox(bone, ref propagationEnabled))
            {
                _editorService.SaveCurrentStateForUndo();
                valueChanged = true;
            }

            ImGui.SameLine();
            isFavorite = FavoriteButton(bone);

            // adjusted logic, should only snapshot if there is a change in the value.
            // change the X
            ImGui.TableNextColumn();
            float tempX = newVector.X;
            using (new SliderHighlightScope(isPropagationAffected, sliderHighlightColor))
            {
                if (SingleValueSlider($"##{displayName}-X", ref tempX))
                {
                    newVector.X = tempX;
                    valueChanged = true;
                }
            }
            TrackSliderHistory();

            // change the Y
            ImGui.TableNextColumn();
            float tempY = newVector.Y;
            using (new SliderHighlightScope(isPropagationAffected, sliderHighlightColor))
            {
                if (SingleValueSlider($"##{displayName}-Y", ref tempY))
                {
                    newVector.Y = tempY;
                    valueChanged = true;
                }
            }
            TrackSliderHistory();

            // change the Z
            ImGui.TableNextColumn();
            float tempZ = newVector.Z;
            using (new SliderHighlightScope(isPropagationAffected, sliderHighlightColor))
            {
                if (SingleValueSlider($"##{displayName}-Z", ref tempZ))
                {
                    newVector.Z = tempZ;
                    valueChanged = true;
                }
            }
            TrackSliderHistory();

            // scale
            if (_editingAttribute != BoneAttribute.Scale)
                ImGui.BeginDisabled();

            ImGui.TableNextColumn();
            Vector3 tempScale = newVector;
            using (new SliderHighlightScope(isPropagationAffected, sliderHighlightColor))
            {
                if (FullBoneSlider($"##{displayName}-All", ref tempScale))
                {
                    newVector = tempScale;
                    valueChanged = true;
                }
            }
            TrackSliderHistory();

            if (_editingAttribute != BoneAttribute.Scale)
                ImGui.EndDisabled();
        }

        ImGui.TableNextColumn();
        if ((BoneData.IsIVCSCompatibleBone(codename) || boneFamily == BoneData.BoneFamily.Unknown) && !codename.StartsWith("j_f_"))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
            ImGuiUtil.PrintIcon(FontAwesomeIcon.Wrench);
            ImGui.PopStyleColor();
            CtrlHelper.AddHoverText("This is a bone from modded skeleton." +
                "\r\nIMPORTANT: The Customize+ team does not provide support for issues related to these bones." +
                "\r\nThese bones need special clothing and body mods designed specifically for them." +
                "\r\nEven if they are intended for these bones, not all clothing mods will support every bone." +
                "\r\nIf you experience issues, try performing the same actions using posing tools.");
            ImGui.SameLine();
        }

        if (DrawParentChainIndicator(bone, isPropagationAffected))
            ImGui.SameLine();

        var boneLabel = !isFavorite ? displayName : $"{displayName} ({boneFamily})";
        var rawLabel = BoneData.IsIVCSCompatibleBone(codename) ? $"(IVCS Compatible) {codename}" : codename;
        var wasSelected = string.Equals(_editorService.SelectedBone, codename, StringComparison.Ordinal);
        var buttonPadding = ImGui.GetStyle().FramePadding;
        var textSize = ImGui.CalcTextSize(boneLabel);
        var buttonSize = textSize + (buttonPadding * 2f);
        using (var disabled = ImRaii.Disabled(!IsEditorActive || IsEditorPaused))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            if (wasSelected)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.45f, 0.75f, 0.4f));
            else
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.18f, 0.18f, 0.3f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.4f, 0.4f, 0.5f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.55f, 0.85f, 0.4f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.45f, 0.75f, 0.5f));
            if (ImGui.Button(boneLabel, buttonSize))
                _editorService.SetSelectedBone(codename);
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(rawLabel);
            ImGui.Separator();
            ImGui.TextUnformatted("Click to select this bone for gizmo manipulation.");
            ImGui.EndTooltip();
        }

        if (valueChanged)
        {
            transform.UpdateAttribute(_editingAttribute, newVector, propagationEnabled);
            _editorManager.ModifyBoneTransform(codename, transform);

            if (_isMirrorModeEnabled && bone.Basis?.TwinBone != null)
            {
                _editorManager.ModifyBoneTransform(
                    bone.Basis.TwinBone.BoneName,
                    BoneData.IsIVCSCompatibleBone(codename)
                        ? transform.GetSpecialReflection()
                        : transform.GetStandardReflection()
                );
            }

            _editorService.SetSelectedBone(codename);
        }

        ImGui.TableNextRow();

        if (_editingAttribute == BoneAttribute.Scale && propagationEnabled)
        {
            RenderChildScalingRow(bone, transform);
        }
    }

    private void RenderChildScalingRow(EditRowParams bone, BoneTransform transform)
    {
        var codename = bone.BoneCodeName;
        var displayName = bone.BoneDisplayName;

        bool isChildScaleIndependent = transform.ChildScalingIndependent;
        bool childScaleChanged = false;
        var childScale = isChildScaleIndependent ? transform.ChildScaling : transform.Scaling;

        using var id = ImRaii.PushId($"{codename}_childscale");

        ImGui.TableNextColumn();
        
        ImGui.SetCursorPosX(_propagateButtonXPos);

        using (var disabled = ImRaii.Disabled(!_isUnlocked))
        {
            var wasLinked = !isChildScaleIndependent;

            if (wasLinked)
                ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Active);

            if (ImGuiComponents.IconButton($"##ChildLink{codename}", FontAwesomeIcon.Link))
            {
                _editorService.SaveCurrentStateForUndo();

                isChildScaleIndependent = !isChildScaleIndependent;
                if (isChildScaleIndependent)
                {
                    childScale = transform.Scaling;
                }
                else
                {
                    transform.ChildScaling = Vector3.One;
                }
                transform.ChildScalingIndependent = isChildScaleIndependent;
                childScaleChanged = true;
            }

            if (wasLinked)
                ImGui.PopStyleColor();

            if (!isChildScaleIndependent)
                ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Active);

            CtrlHelper.AddHoverText(
                $"Link '{BoneData.GetBoneDisplayName(codename)}' child bone scaling to parent scaling");

            if (!isChildScaleIndependent)
                ImGui.PopStyleColor();
        }

        // Draws a bracket between the two rows.
        var drawList = ImGui.GetWindowDrawList();
        var bracketColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        var lineThickness = 2.0f;

        var rowHeight = ImGui.GetFrameHeight();
        var bracketWidth = CtrlHelper.IconButtonWidth * 0.3f;

        var availWidth = ImGui.GetContentRegionAvail().X;
        var cursorScreenPos = ImGui.GetCursorScreenPos();
        var rightEdgeX = cursorScreenPos.X + availWidth - bracketWidth;

        var parentRowCenterY = _parentRowScreenPosY + (rowHeight * 0.5f);
        var childRowCenterY = cursorScreenPos.Y + (rowHeight * 0.5f);
        var bracketCenterY = (parentRowCenterY + childRowCenterY) * 0.5f;

        var topY = parentRowCenterY;
        var bottomY = bracketCenterY;
        var heightThird = (topY - bottomY) / 3;
        var topRightM = new Vector2(rightEdgeX + bracketWidth - 1, topY);
        var topLeft = new Vector2(rightEdgeX, topY);
        var bottomLeft = new Vector2(rightEdgeX, bottomY);
        var bottomLeftM = new Vector2(rightEdgeX - 1, bottomY); // Just works
        var bottomRight = new Vector2(rightEdgeX + bracketWidth, bottomY);

        drawList.AddLine(topRightM, topLeft, bracketColor, lineThickness);   // Top
        if (!isChildScaleIndependent)
        {
            drawList.AddLine(topLeft, bottomLeft, bracketColor, lineThickness); // Middle
        }
        else
        {
            var gapStart = new Vector2(rightEdgeX, topY - heightThird);
            var gapEnd = new Vector2(rightEdgeX, topY - (2 * heightThird));
            drawList.AddLine(topLeft, gapStart, bracketColor, lineThickness);
            drawList.AddLine(gapEnd, bottomLeft, bracketColor, lineThickness);
        }
        drawList.AddLine(bottomLeftM, bottomRight, bracketColor, lineThickness); // Bottom

        using (var disabled = ImRaii.Disabled(!_isUnlocked || !isChildScaleIndependent))
        {
            ImGui.TableNextColumn();
            float tempChildX = childScale.X;
            if (SingleValueSlider($"##child-{displayName}-X", ref tempChildX))
            {
                childScale.X = tempChildX;
                childScaleChanged = true;
            }
            TrackSliderHistory();

            ImGui.TableNextColumn();
            float tempChildY = childScale.Y;
            if (SingleValueSlider($"##child-{displayName}-Y", ref tempChildY))
            {
                childScale.Y = tempChildY;
                childScaleChanged = true;
            }
            TrackSliderHistory();

            ImGui.TableNextColumn();
            float tempChildZ = childScale.Z;
            if (SingleValueSlider($"##child-{displayName}-Z", ref tempChildZ))
            {
                childScale.Z = tempChildZ;
                childScaleChanged = true;
            }
            TrackSliderHistory();

            ImGui.TableNextColumn();
            if (FullBoneSlider($"##child-{displayName}-All", ref childScale))
                childScaleChanged = true;
            TrackSliderHistory();
        }

        ImGui.TableNextColumn();
        CtrlHelper.StaticLabel($"{displayName} - Child Bones", CtrlHelper.TextAlignment.Left, "Scale applied to child bones");

        if (childScaleChanged)
        {
            transform.ChildScaling = childScale;
            _editorManager.ModifyBoneTransform(codename, transform);

            if (_isMirrorModeEnabled && bone.Basis?.TwinBone != null)
            {
                _editorManager.ModifyBoneTransform(
                    bone.Basis.TwinBone.BoneName,
                    BoneData.IsIVCSCompatibleBone(codename)
                        ? transform.GetSpecialReflection()
                        : transform.GetStandardReflection()
                );
            }
        }

        ImGui.TableNextRow();
    }

    private bool DrawParentChainIndicator(EditRowParams bone, bool isPropagationAffected)
    {
        if (!isPropagationAffected)
            return false;

        var ancestors = EnumerateAncestors(bone)
            .Select(name => new
            {
                Code = name,
                Display = BoneData.GetBoneDisplayName(name),
                Family = BoneData.GetBoneFamily(name),
                IsSource = _propagationSources.Contains(name)
            })
            .ToList();

        if (ancestors.Count == 0)
            return false;

        ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Info);
        ImGuiUtil.PrintIcon(FontAwesomeIcon.Link);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Parent chain:");
            ImGui.Separator();
            foreach (var ancestor in ancestors)
            {
                var textColor = Constants.PropagationColors.GetTooltipColor(ancestor.Family, ancestor.IsSource);

                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.TextUnformatted($"- {ancestor.Display} ({ancestor.Code})");
                ImGui.PopStyleColor();
            }

            ImGui.EndTooltip();
        }

        return true;
    }

    private void UpdatePropagationHighlights(IEnumerable<EditRowParams> rows)
    {
        _propagationHighlights.Clear();
        _propagationSources.Clear();

        if (rows == null)
            return;

        var rowList = rows as IList<EditRowParams> ?? rows.ToList();
        if (rowList.Count == 0)
            return;

        var available = new HashSet<string>(rowList.Select(r => r.BoneCodeName), StringComparer.Ordinal);

        foreach (var row in rowList)
        {
            var transform = row.Transform;
            var shouldPropagate = transform.IsPropagationEnabledForAttribute(_editingAttribute);

            if (!shouldPropagate)
                continue;

            _propagationSources.Add(row.BoneCodeName);
            _propagationHighlights.Add(row.BoneCodeName);

            foreach (var descendant in EnumerateDescendants(row, available))
            {
                _propagationHighlights.Add(descendant);
            }
        }
    }

    private IEnumerable<string> EnumerateDescendants(EditRowParams source, HashSet<string> available)
    {
        foreach (var descendant in BoneRelationHelper.EnumerateDescendants(source.Basis, source.BoneCodeName))
        {
            if (available.Contains(descendant))
                yield return descendant;
        }
    }

    private IEnumerable<string> EnumerateAncestors(EditRowParams bone)
    {
        foreach (var ancestor in BoneRelationHelper.EnumerateAncestors(bone.Basis, bone.BoneCodeName))
            yield return ancestor;
    }

    private void ApplyEditorAttribute(BoneAttribute attribute)
        => _editorService.SetActiveAttribute(attribute);

    private void ApplyMirrorMode(bool enabled)
        => _editorService.SetMirrorMode(enabled);

    private void ApplyGizmoEnabled(bool enabled)
        => _editorService.SetGizmoEnabled(enabled);

    private void TrackSliderHistory()
    {
        if (ImGui.IsItemActivated())
            _editorService.BeginPendingEdit();

        if (ImGui.IsItemDeactivatedAfterEdit())
            _editorService.CommitPendingEditIfChanged();
    }

    private Vector4 GetPropagationHighlightVector(BoneData.BoneFamily family, string boneCodeName)
    {
        return _propagationSources.Contains(boneCodeName)
            ? Constants.PropagationColors.GetParentColor(family)
            : Constants.PropagationColors.GetChildColor(family);
    }

    private readonly struct SliderHighlightScope : IDisposable
    {
        private readonly bool _active;

        public SliderHighlightScope(bool active, Vector4 color)
        {
            _active = active;
            if (_active)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                var borderColor = new Vector4(
                    MathF.Min(color.X + 0.35f, 1f),
                    MathF.Min(color.Y + 0.35f, 1f),
                    MathF.Min(color.Z + 0.35f, 1f),
                    MathF.Min(color.W + 0.50f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Border, borderColor);
            }
        }

        public void Dispose()
        {
            if (_active)
            {
                ImGui.PopStyleColor();
                ImGui.PopStyleVar();
            }
        }
    }
    #endregion
}

/// <summary>
/// Simple structure for representing arguments to the editor table.
/// Can be constructed with or without access to a live armature.
/// </summary>
internal struct EditRowParams
{
    public string BoneCodeName;
    public string BoneDisplayName => BoneData.GetBoneDisplayName(BoneCodeName);
    public BoneTransform Transform;
    public ModelBone? Basis = null;

    public EditRowParams(ModelBone mb)
    {
        BoneCodeName = mb.BoneName;
        Transform = mb.CustomizedTransform ?? new BoneTransform();
        Basis = mb;
    }

    public EditRowParams(string codename, BoneTransform tr)
    {
        BoneCodeName = codename;
        Transform = tr;
        Basis = null;
    }
}