using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Services;
using CustomizePlus.Game.Helpers;
using CustomizePlus.Game.Services;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Interop.Ipc;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Enums;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Events;
using CustomizePlus.UI.Windows.Controls;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using OtterGui;
using OtterGui.Extensions;
using OtterGui.Raii;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static FFXIVClientStructs.FFXIV.Client.LayoutEngine.ILayoutInstance;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class ProfilePanel
{
    private readonly ProfileFileSystemSelector _selector;
    private readonly ProfileManager _manager;
    private readonly PluginConfiguration _configuration;
    private readonly TemplateCombo _templateCombo;
    private readonly TemplateEditorManager _templateEditorManager;
    private readonly ActorAssignmentUi _actorAssignmentUi;
    private readonly ActorManager _actorManager;
    private readonly TemplateEditorEvent _templateEditorEvent;
    private readonly GearDataService _gearDataService;
    private readonly EmoteService _emoteService;
    private readonly GearSlotIconService _gearSlotIconService;
    private readonly ITextureProvider _textureProvider;
    private readonly PenumbraIpcHandler _penumbraIpc;
    private readonly ModService _modService;

    private string? _newName;
    private int? _newPriority;
    private Profile? _changedProfile;

    private System.Action? _endAction;
    private Lumina.Excel.Sheets.Action? _luminaActionSheet;

    private int _dragIndex = -1;

    private string SelectionName
        => _selector.Selected == null ? "No Selection" : _selector.IncognitoMode ? _selector.Selected.Incognito : _selector.Selected.Name.Text;

    public ProfilePanel(
        ProfileFileSystemSelector selector,
        ProfileManager manager,
        PluginConfiguration configuration,
        TemplateCombo templateCombo,
        TemplateEditorManager templateEditorManager,
        ActorAssignmentUi actorAssignmentUi,
        ActorManager actorManager,
        TemplateEditorEvent templateEditorEvent,
        GearDataService gearDataService,
        EmoteService emoteService,
        GearSlotIconService gearSlotIconService,
        ITextureProvider textureProvider,
        PenumbraIpcHandler penumbraIpc,
        ModService modService)
    {
        _selector = selector;
        _manager = manager;
        _configuration = configuration;
        _templateCombo = templateCombo;
        _templateEditorManager = templateEditorManager;
        _actorAssignmentUi = actorAssignmentUi;
        _actorManager = actorManager;
        _templateEditorEvent = templateEditorEvent;
        _gearDataService = gearDataService;
        _emoteService = emoteService;
        _gearSlotIconService = gearSlotIconService;
        _textureProvider = textureProvider;
        _penumbraIpc = penumbraIpc;
        _modService = modService;
    }

    public void Draw()
    {
        using var group = ImRaii.Group();
        if (_selector.SelectedPaths.Count > 1)
        {
            DrawMultiSelection();
        }
        else
        {
            DrawHeader();
            DrawPanel();
        }
    }

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

    private void DrawHeader()
        => HeaderDrawer.Draw(SelectionName, 0, ImGui.GetColorU32(ImGuiCol.FrameBg),
            0, LockButton(),
            HeaderDrawer.Button.IncognitoButton(_selector.IncognitoMode, v => _selector.IncognitoMode = v));

    private static Vector4 GetConditionBaseColor(ConditionType type)
        => type switch
        {
            ConditionType.Mod => new Vector4(0.55f, 0.80f, 1f, 1f),
            ConditionType.Gear => new Vector4(0.95f, 0.75f, 0.45f, 1f),
            ConditionType.Race => new Vector4(0.70f, 0.55f, 0.95f, 1f),
            ConditionType.Emote => new Vector4(0.45f, 0.85f, 0.65f, 1f),
            _ => Vector4.One
        };

    private static Vector4 LightenColor(Vector4 color, float amount)
        => new Vector4(
            MathF.Min(color.X + amount, 1f),
            MathF.Min(color.Y + amount, 1f),
            MathF.Min(color.Z + amount, 1f),
            color.W);

    private static Vector4 DarkenColor(Vector4 color, float amount)
        => new Vector4(
            MathF.Max(color.X - amount, 0f),
            MathF.Max(color.Y - amount, 0f),
            MathF.Max(color.Z - amount, 0f),
            color.W);

    private static Vector4 DimColor(Vector4 color, float factor, float alphaFactor)
    {
        static float Clamp(float value) => MathF.Max(0f, MathF.Min(1f, value));

        return new Vector4(
            Clamp(color.X * factor),
            Clamp(color.Y * factor),
            Clamp(color.Z * factor),
            Clamp(color.W * alphaFactor));
    }

    private static IReadOnlyList<SubRace> GetSubRacesForRace(Race race)
        => race switch
        {
            Race.Hyur => new[] { SubRace.Midlander, SubRace.Highlander },
            Race.Elezen => new[] { SubRace.Wildwood, SubRace.Duskwight },
            Race.Lalafell => new[] { SubRace.Plainsfolk, SubRace.Dunesfolk },
            Race.Miqote => new[] { SubRace.SeekerOfTheSun, SubRace.KeeperOfTheMoon },
            Race.Roegadyn => new[] { SubRace.Seawolf, SubRace.Hellsguard },
            Race.AuRa => new[] { SubRace.Raen, SubRace.Xaela },
            Race.Hrothgar => new[] { SubRace.Helion, SubRace.Lost },
            Race.Viera => new[] { SubRace.Rava, SubRace.Veena },
            _ => Array.Empty<SubRace>(),
        };

    private void EnsureValidRaceSelection()
    {
        var clans = GetSubRacesForRace(_newRaceRace);
        if (clans.Count == 0)
        {
            _newRaceClan = SubRace.Unknown;
            return;
        }

        if (!clans.Contains(_newRaceClan))
            _newRaceClan = clans[0];
    }

    private static string DescribeRaceCondition(RaceCondition condition)
    {
        var raceName = condition.Race.ToName();
        var clanName = condition.Clan.ToName();
        var genderName = condition.Gender.ToName();
        return $"{raceName} ({clanName}) - {genderName}";
    }

    private string DescribeEmoteCondition(EmoteCondition condition)
    {
        var entry = _emoteService.FindById(condition.EmoteId);
        if (entry != null)
            return $"{entry.Name} (#{entry.Id})";

        return $"Emote #{condition.EmoteId}";
    }

    private bool CanAddRaceCondition()
        => _newRaceRace != Race.Unknown
           && _newRaceClan != SubRace.Unknown
           && (_newRaceGender == Gender.Male || _newRaceGender == Gender.Female);

    private void DrawMultiSelection()
    {
        if (_selector.SelectedPaths.Count == 0)
            return;

        var sizeType = ImGui.GetFrameHeight();
        var availableSizePercent = (ImGui.GetContentRegionAvail().X - sizeType - 4 * ImGui.GetStyle().CellPadding.X) / 100;
        var sizeMods = availableSizePercent * 35;
        var sizeFolders = availableSizePercent * 65;

        ImGui.NewLine();
        ImGui.TextUnformatted("Currently Selected Profiles");
        ImGui.Separator();
        using var table = ImRaii.Table("profile", 3, ImGuiTableFlags.RowBg);
        ImGui.TableSetupColumn("btn", ImGuiTableColumnFlags.WidthFixed, sizeType);
        ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed, sizeMods);
        ImGui.TableSetupColumn("path", ImGuiTableColumnFlags.WidthFixed, sizeFolders);

        var i = 0;
        foreach (var (fullName, path) in _selector.SelectedPaths.Select(p => (p.FullName(), p))
                     .OrderBy(p => p.Item1, StringComparer.OrdinalIgnoreCase))
        {
            using var id = ImRaii.PushId(i++);
            ImGui.TableNextColumn();
            var icon = (path is ProfileFileSystem.Leaf ? FontAwesomeIcon.FileCircleMinus : FontAwesomeIcon.FolderMinus).ToIconString();
            if (ImGuiUtil.DrawDisabledButton(icon, new Vector2(sizeType), "Remove from selection.", false, true))
                _selector.RemovePathFromMultiSelection(path);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(path is ProfileFileSystem.Leaf l ? _selector.IncognitoMode ? l.Value.Incognito : l.Value.Name.Text : string.Empty);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(_selector.IncognitoMode ? "Incognito is active" : fullName);
        }
    }

    private void DrawPanel()
    {
        using var child = ImRaii.Child("##Panel", -Vector2.One, true);
        if (!child || _selector.Selected == null)
            return;

        DrawEnabledSetting();

        ImGui.Separator();

        using (var disabled = ImRaii.Disabled(_selector.Selected?.IsWriteProtected ?? true))
        {
            DrawBasicSettings();

            ImGui.Separator();

            var isShouldDrawCharacter = ImGui.CollapsingHeader("Add character");

            if (isShouldDrawCharacter)
                DrawAddCharactersArea();

            ImGui.Separator();

            DrawCharacterListArea();

            ImGui.Separator();

            var isShouldDrawConditions = ImGui.CollapsingHeader("Add conditions");

            if (isShouldDrawConditions)
                DrawConditionsArea();

            ImGui.Separator();

            DrawTemplateArea();
        }
    }

    private void DrawEnabledSetting()
    {
        var spacing = ImGui.GetStyle().ItemInnerSpacing with { X = ImGui.GetStyle().ItemSpacing.X, Y = ImGui.GetStyle().ItemSpacing.Y };

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing))
        {
            var enabled = _selector.Selected?.Enabled ?? false;
            using (ImRaii.Disabled(_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused))
            {
                if (ImGui.Checkbox("##Enabled", ref enabled))
                    _manager.SetEnabled(_selector.Selected!, enabled);
                ImGuiUtil.LabeledHelpMarker("Enabled",
                    "Whether the templates in this profile should be applied at all.");
            }
        }
    }

    private void DrawBasicSettings()
    {
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            using (var table = ImRaii.Table("BasicSettings", 2))
            {
                ImGui.TableSetupColumn("BasicCol1", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("lorem ipsum dolor").X);
                ImGui.TableSetupColumn("BasicCol2", ImGuiTableColumnFlags.WidthStretch);

                ImGuiUtil.DrawFrameColumn("Profile Name");
                ImGui.TableNextColumn();
                var width = new Vector2(ImGui.GetContentRegionAvail().X, 0);
                var name = _newName ?? _selector.Selected!.Name;
                ImGui.SetNextItemWidth(width.X);

                if (!_selector.IncognitoMode)
                {
                    if (ImGui.InputText("##ProfileName", ref name, 128))
                    {
                        _newName = name;
                        _changedProfile = _selector.Selected;
                    }

                    if (ImGui.IsItemDeactivatedAfterEdit() && _changedProfile != null)
                    {
                        _manager.Rename(_changedProfile, name);
                        _newName = null;
                        _changedProfile = null;
                    }
                }
                else
                    ImGui.TextUnformatted(_selector.Selected!.Incognito);

                ImGui.TableNextRow();

                ImGuiUtil.DrawFrameColumn("Priority");
                ImGui.TableNextColumn();

                var priority = _newPriority ?? _selector.Selected!.Priority;

                ImGui.SetNextItemWidth(50);
                if (ImGui.InputInt("##Priority", ref priority, 0, 0))
                {
                    _newPriority = priority;
                    _changedProfile = _selector.Selected;
                }

                if (ImGui.IsItemDeactivatedAfterEdit() && _changedProfile != null)
                {
                    _manager.SetPriority(_changedProfile, priority);
                    _newPriority = null;
                    _changedProfile = null;
                }

                ImGuiComponents.HelpMarker("Profiles with a higher number here take precedence before profiles with a lower number.\n" +
                    "That means if two or more profiles affect same character, profile with higher priority will be applied to that character.");
            }
        }
    }

    private void DrawAddCharactersArea()
    {
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            var width = new Vector2(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Limit to my creatures").X - 68, 0);

            ImGui.SetNextItemWidth(width.X);

            bool appliesToMultiple = _manager.DefaultProfile == _selector.Selected || _manager.DefaultLocalPlayerProfile == _selector.Selected;
            using (ImRaii.Disabled(appliesToMultiple))
            {
                _actorAssignmentUi.DrawWorldCombo(width.X / 2);
                ImGui.SameLine();
                _actorAssignmentUi.DrawPlayerInput(width.X / 2);

                var buttonWidth = new Vector2(165 * ImGuiHelpers.GlobalScale - ImGui.GetStyle().ItemSpacing.X / 2, 0);

                if (ImGuiUtil.DrawDisabledButton("Apply to player character", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetPlayer))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.PlayerIdentifier);

                ImGui.SameLine();

                if (ImGuiUtil.DrawDisabledButton("Apply to retainer", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetRetainer))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.RetainerIdentifier);

                ImGui.SameLine();

                if (ImGuiUtil.DrawDisabledButton("Apply to mannequin", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetMannequin))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.MannequinIdentifier);

                var currentPlayer = _actorManager.GetCurrentPlayer().CreatePermanent();
                if (ImGuiUtil.DrawDisabledButton("Apply to current character", buttonWidth, string.Empty, !currentPlayer.IsValid))
                    _manager.AddCharacter(_selector.Selected!, currentPlayer);

                ImGui.Separator();

                _actorAssignmentUi.DrawObjectKindCombo(width.X / 2);
                ImGui.SameLine();
                _actorAssignmentUi.DrawNpcInput(width.X / 2);

                if (ImGuiUtil.DrawDisabledButton("Apply to selected NPC", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetNpc))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.NpcIdentifier);
            }
        }
    }

    private void DrawCharacterListArea()
    {
        var isDefaultLP = _manager.DefaultLocalPlayerProfile == _selector.Selected;
        var isDefaultLPOrCurrentProfilesEnabled = (_manager.DefaultLocalPlayerProfile?.Enabled ?? false) || (_selector.Selected?.Enabled ?? false);
        using (ImRaii.Disabled(isDefaultLPOrCurrentProfilesEnabled))
        {
            if (ImGui.Checkbox("##DefaultLocalPlayerProfile", ref isDefaultLP))
                _manager.SetDefaultLocalPlayerProfile(isDefaultLP ? _selector.Selected! : null);
            ImGuiUtil.LabeledHelpMarker("Apply to any character you are logged in with",
                "Whether the templates in this profile should be applied to any character you are currently logged in with.\r\nTakes priority over the next option for said character.\r\nThis setting cannot be applied to multiple profiles.");
        }
        if (isDefaultLPOrCurrentProfilesEnabled)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
            ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
            ImGui.PopStyleColor();
            ImGuiUtil.HoverTooltip("Can only be changed when both currently selected and profile where this checkbox is checked are disabled.");
        }

        ImGui.SameLine();
        using(ImRaii.Disabled(true))
            ImGui.Button("##splitter", new Vector2(1, ImGui.GetFrameHeight()));
        ImGui.SameLine();

        var isDefault = _manager.DefaultProfile == _selector.Selected;
        var isDefaultOrCurrentProfilesEnabled = (_manager.DefaultProfile?.Enabled ?? false) || (_selector.Selected?.Enabled ?? false);
        using (ImRaii.Disabled(isDefaultOrCurrentProfilesEnabled))
        {
            if (ImGui.Checkbox("##DefaultProfile", ref isDefault))
                _manager.SetDefaultProfile(isDefault ? _selector.Selected! : null);
            ImGuiUtil.LabeledHelpMarker("Apply to all players and retainers",
                "Whether the templates in this profile are applied to all players and retainers without a specific profile.\r\nThis setting cannot be applied to multiple profiles.");
        }
        if (isDefaultOrCurrentProfilesEnabled)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
            ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
            ImGui.PopStyleColor();
            ImGuiUtil.HoverTooltip("Can only be changed when both currently selected and profile where this checkbox is checked are disabled.");
        }
        bool appliesToMultiple = _manager.DefaultProfile == _selector.Selected || _manager.DefaultLocalPlayerProfile == _selector.Selected;

        ImGui.Separator();

        using var dis = ImRaii.Disabled(appliesToMultiple);
        using var table = ImRaii.Table("CharacterTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY, new Vector2(ImGui.GetContentRegionAvail().X, 200));
        if (!table)
            return;

        ImGui.TableSetupColumn("##charaDel", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, 320 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        if (appliesToMultiple)
        {
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Applies to multiple targets");
            return;
        }

        //warn: .ToList() might be performance critical at some point
        //the copying via ToList is done because manipulations with .Templates list result in "Collection was modified" exception here
        var charas = _selector.Selected!.Characters.WithIndex().ToList();

        if (charas.Count == 0)
        {
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("No characters are associated with this profile");
        }

        foreach (var (character, idx) in charas)
        {
            using var id = ImRaii.PushId(idx);
            ImGui.TableNextColumn();
            var keyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
            var tt = keyValid
                ? "Remove this character from the profile."
                : $"Remove this character from the profile.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt, !keyValid, true))
                _endAction = () => _manager.DeleteCharacter(_selector.Selected!, character);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(!_selector.IncognitoMode ? $"{character.ToNameWithoutOwnerName()}{character.TypeToString()}" : "Incognito");

            var profiles = _manager.GetEnabledProfilesByActor(character).ToList();
            if (profiles.Count > 1)
            {
                //todo: make helper
                ImGui.SameLine();
                if (profiles.Any(x => x.IsTemporary))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Error);
                    ImGuiUtil.PrintIcon(FontAwesomeIcon.Lock);
                }
                else if (profiles[0] != _selector.Selected!)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
                    ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Info);
                    ImGuiUtil.PrintIcon(FontAwesomeIcon.Star);
                }

                ImGui.PopStyleColor();

                if (profiles.Any(x => x.IsTemporary))
                    ImGuiUtil.HoverTooltip("This character is being affected by temporary profile set by external plugin. This profile will not be applied!");
                else
                    ImGuiUtil.HoverTooltip(profiles[0] != _selector.Selected! ? "Several profiles are trying to affect this character. This profile will not be applied!" :
                        "Several profiles are trying to affect this character. This profile is being applied.");
            }
        }

        _endAction?.Invoke();
        _endAction = null;
    }

    private void DrawTemplateArea()
    {
        using var table = ImRaii.Table("TemplateTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("##Index", ImGuiTableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##Enabled", ImGuiTableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);

        ImGui.TableSetupColumn("Template", ImGuiTableColumnFlags.WidthFixed, 220 * ImGuiHelpers.GlobalScale);

        ImGui.TableSetupColumn("##editbtn", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);

        ImGui.TableHeadersRow();

        //warn: .ToList() might be performance critical at some point
        //the copying via ToList is done because manipulations with .Templates list result in "Collection was modified" exception here
        foreach (var (template, idx) in _selector.Selected!.Templates.WithIndex().ToList())
        {
            using var id = ImRaii.PushId(idx);
            ImGui.TableNextColumn();
            var keyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
            var tt = keyValid
                ? "Remove this template from the profile."
                : $"Remove this template from the profile.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt, !keyValid, true))
                _endAction = () => _manager.DeleteTemplate(_selector.Selected!, idx);
            ImGui.TableNextColumn();
            ImGui.Selectable($"#{idx + 1:D2}");
            DrawDragDrop(_selector.Selected!, idx);

            ImGui.TableNextColumn();
            var enabled = !_selector.Selected!.DisabledTemplates.Contains(template.UniqueId);
            if (ImGui.Checkbox("##EnableCheckbox", ref enabled))
                _manager.ToggleTemplate(_selector.Selected!, idx);
            ImGuiUtil.HoverTooltip("Whether this template is applied to the profile.");

            ImGui.TableNextColumn();

            _templateCombo.Draw(_selector.Selected!, template, idx);

            DrawDragDrop(_selector.Selected!, idx);

            ImGui.TableNextColumn();

            var disabledCondition = _templateEditorManager.IsEditorActive || template.IsWriteProtected;

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Edit.ToIconString(), new Vector2(ImGui.GetFrameHeight()), "Open this template in the template editor.", disabledCondition, true))
                _templateEditorEvent.Invoke(TemplateEditorEvent.Type.EditorEnableRequested, template);

            if (disabledCondition)
            {
                //todo: make helper
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
                ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
                ImGui.PopStyleColor();
                ImGuiUtil.HoverTooltip("This template cannot be edited because it is either write protected or you are already editing one of the templates.");
            }
        }

        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("New");
        ImGui.TableNextColumn();
        _templateCombo.Draw(_selector.Selected!, null, -1);
        ImGui.TableNextRow();

        _endAction?.Invoke();
        _endAction = null;
    }

    private void DrawDragDrop(Profile profile, int index)
    {
        const string dragDropLabel = "TemplateDragDrop";
        using (var target = ImRaii.DragDropTarget())
        {
            if (target.Success && ImGuiUtil.IsDropping(dragDropLabel))
            {
                if (_dragIndex >= 0)
                {
                    var idx = _dragIndex;
                    _endAction = () => _manager.MoveTemplate(profile, idx, index);
                }

                _dragIndex = -1;
            }
        }

        using (var source = ImRaii.DragDropSource())
        {
            if (source)
            {
                ImGui.TextUnformatted($"Moving template #{index + 1:D2}...");
                if (ImGui.SetDragDropPayload(dragDropLabel, null, 0))
                {
                    _dragIndex = index;
                }
            }
        }
    }

    private void UpdateIdentifiers()
    {

    }

    private ConditionType _newConditionType = ConditionType.Gear;
    private GearSlot? _selectedGearSlot = null;
    private bool _gearPopupJustOpened = false;

    private GearSelector? _gearSelector;
    private ModSelector? _modSelector;
    private EmoteSelector? _emoteSelector;
    private Gender _newRaceGender = Gender.Male;
    private Race _newRaceRace = Race.Hyur;
    private SubRace _newRaceClan = SubRace.Midlander;

    private void DrawConditionsArea()
    {
        if (_selector.Selected == null)
            return;

        bool conditionsEnabled = _selector.Selected.ConditionsEnabled;
        if (ImGui.Checkbox("Enable Conditions", ref conditionsEnabled))
            _manager.SetConditionsEnabled(_selector.Selected, conditionsEnabled);

        ImGui.SameLine();

        ImGuiUtil.LabeledHelpMarker("What do conditions do?",
            "Conditions control whether this profile becomes active based on specific criteria.\n" +
            "For example, you can set conditions to only apply this profile when your character is wearing specific gear or when certain mods are enabled.\n" +
            "If the conditions are not met, the profile will be ignored, allowing other profiles to activate instead.");

        ImGui.Separator();

        float spacing = 4f;
        Vector2 buttonSize = new(90f, 24f);

        foreach (ConditionType type in Enum.GetValues(typeof(ConditionType)))
        {
            ImGui.PushID((int)type);

            bool selected = _newConditionType == type;

            var baseColor = GetConditionBaseColor(type);

            Vector4 buttonColor;
            Vector4 hoverColor;
            Vector4 activeColor;

            if (selected)
            {
                buttonColor = baseColor;
                hoverColor = LightenColor(baseColor, 0.08f);
                activeColor = DarkenColor(baseColor, 0.12f);
            }
            else
            {
                buttonColor = DimColor(baseColor, 0.45f, 0.65f);
                hoverColor = DimColor(baseColor, 0.60f, 0.75f);
                activeColor = DimColor(baseColor, 0.35f, 0.60f);
            }

            ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeColor);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 3.5f));

            if (ImGui.Button(type.ToString(), buttonSize))
                _newConditionType = type;

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(3);
            ImGui.PopID();

            ImGui.SameLine(0, spacing);
        }
        ImGui.NewLine();

        ImGui.Separator();

        if (_newConditionType == ConditionType.Gear)
        {

            foreach (var (slot, icon) in _gearSlotIconService.Icons)
            {
                ImGui.PushID((int)slot);

                bool selected = _selectedGearSlot == slot;
                Vector4 tintColor = selected ? new Vector4(1f, .81f, .2f, 1f) : Vector4.One;

                ImGui.Image(icon.Handle, new Vector2(36, 36), Vector2.Zero, Vector2.One, tintColor, Vector4.Zero);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(GearSlotHelper.DisplayName(slot));

                if (ImGui.IsItemClicked())
                {
                    _selectedGearSlot = slot;
                    _gearSelector = new GearSelector(_gearDataService, _textureProvider, slot);
                    _gearPopupJustOpened = true;
                    ImGui.OpenPopup("GearSelectorPopup");
                }

                const float gearPopupIconSize = 40f;
                ImGui.SetNextWindowSize(new Vector2(400f, 0f), ImGuiCond.Appearing);

                if (ImGui.BeginPopup("GearSelectorPopup"))
                {
                    if (_gearPopupJustOpened)
                    {
                        _gearPopupJustOpened = false;
                    }

                    _gearSelector?.Draw(gearPopupIconSize);
                    ImGui.EndPopup();
                }
                else if (!_gearPopupJustOpened && _selectedGearSlot == slot && _gearSelector?.SelectedItem == null)
                {
                    _selectedGearSlot = null;
                    _gearSelector = null;
                }

                ImGui.PopID();
                ImGui.SameLine();
            }
            ImGui.NewLine();

            if (_gearSelector?.SelectedItem is { } selectedItem)
            {
                var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(selectedItem.Icon));
                var modelId = selectedItem.ModelMain;
                var modelBase = (modelId >> 16) & 0xFFFF;
                var modelVariant = modelId & 0xFFFF;

                ImGui.BeginGroup();

                if (icon.TryGetWrap(out var wrap, out _))
                    ImGui.Image(wrap.Handle, new Vector2(60));
                else
                    ImGui.Dummy(new Vector2(60));

                ImGui.SameLine();

                ImGui.BeginGroup();
                ImGui.TextUnformatted(selectedItem.Name.ToString());
                ImGui.TextUnformatted($"Model: {modelBase}, {modelVariant}");
                var slotName = _selectedGearSlot.HasValue ? GearSlotHelper.DisplayName(_selectedGearSlot.Value) : "None";
                ImGui.TextUnformatted($"Slot: {slotName}");
                ImGui.EndGroup();

                ImGui.EndGroup();
            }
        }
        else if (_newConditionType == ConditionType.Mod)
        {
            _modSelector ??= new ModSelector(_modService);
            _modSelector.Draw();
        }
        else if (_newConditionType == ConditionType.Race)
        {
            EnsureValidRaceSelection();

            if (ImGui.BeginCombo("Gender##RaceConditionGender", _newRaceGender.ToName()))
            {
                var genders = new[] { Gender.Male, Gender.Female };
                foreach (var gender in genders)
                {
                    bool selected = _newRaceGender == gender;
                    if (ImGui.Selectable(gender.ToName(), selected))
                    {
                        _newRaceGender = gender;
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            if (ImGui.BeginCombo("Race##RaceConditionRace", _newRaceRace.ToName()))
            {
                foreach (Race race in Enum.GetValues(typeof(Race)))
                {
                    if (race == Race.Unknown)
                        continue;

                    bool selected = _newRaceRace == race;
                    if (ImGui.Selectable(race.ToName(), selected))
                    {
                        _newRaceRace = race;
                        EnsureValidRaceSelection();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            var availableClans = GetSubRacesForRace(_newRaceRace);
            var clanLabel = availableClans.Count > 0 ? _newRaceClan.ToName() : "No Clans";
            if (ImGui.BeginCombo("Clan##RaceConditionClan", clanLabel))
            {
                foreach (var clan in availableClans)
                {
                    bool selected = _newRaceClan == clan;
                    if (ImGui.Selectable(clan.ToName(), selected))
                    {
                        _newRaceClan = clan;
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }
        else if (_newConditionType == ConditionType.Emote)
        {
            _emoteSelector ??= new EmoteSelector(_emoteService, _textureProvider);

            _emoteSelector.Draw();
        }

        bool canAdd =
            (_newConditionType == ConditionType.Gear && _gearSelector?.SelectedItem != null)
            || (_newConditionType == ConditionType.Mod && _modSelector?.SelectedMod is not null)
            || (_newConditionType == ConditionType.Race && CanAddRaceCondition())
            || (_newConditionType == ConditionType.Emote && _emoteSelector?.SelectedEmote is not null);

        if (ImGui.Button("Add Condition") && canAdd)
        {
            var profile = _selector.Selected;
            if (_newConditionType == ConditionType.Gear && _gearSelector?.SelectedItem is { } item)
            {
                if (_selectedGearSlot is GearSlot slot)
                {
                    var condition = new GearCondition(slot, (ushort)item.ModelMain);
                    _manager.AddGearCondition(profile, condition);
                }
            }
            else if (_newConditionType == ConditionType.Mod && _modSelector?.SelectedMod is { } modName)
            {
                var condition = new ModCondition(modName);
                _manager.AddModCondition(profile, condition);
                _modSelector = null;
            }
            else if (_newConditionType == ConditionType.Race && CanAddRaceCondition())
            {
                var condition = new RaceCondition(_newRaceRace, _newRaceClan, _newRaceGender);
                _manager.AddRaceCondition(profile, condition);
            }
            else if (_newConditionType == ConditionType.Emote && _emoteSelector?.SelectedEmote is { } emote)
            {
                var condition = new EmoteCondition(emote.Id);
                _manager.AddEmoteCondition(profile, condition);
                _emoteSelector = null;
            }
        }


        ImGui.Separator();

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV;
        if (ImGui.BeginTable("ConditionsTable", 3, tableFlags))
        {
            ImGui.TableSetupColumn("##Actions", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Condition");
            ImGui.TableHeadersRow();

            using (var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f)))
            {
                for (int i = 0; i < _selector.Selected.Conditions.Count; i++)
                {
                    var profile = _selector.Selected;
                    var cond = _selector.Selected.Conditions[i];

                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 0f);

                    ImGui.TableNextColumn();
                    using (var id = ImRaii.PushId(i))
                    {
                        var deleteKeyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
                        var tooltip = deleteKeyValid
                            ? "Remove this condition."
                            : $"Remove this condition.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

                        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tooltip, !deleteKeyValid, true))
                        {
                            _manager.RemoveCondition(_selector.Selected, cond);
                            break;
                        }

                        ImGui.SameLine(0, 4f);

                        bool enabled = cond.Enabled;
                        if (ImGui.Checkbox("##enabled", ref enabled))
                            _manager.SetConditionEnabled(profile, cond, enabled);

                    }

                    ImGui.TableNextColumn();
                    var typeColor = cond switch
                    {
                        ModCondition => new Vector4(0.55f, 0.80f, 1f, 1f),
                        GearCondition => new Vector4(0.95f, 0.75f, 0.45f, 1f),
                        RaceCondition => new Vector4(0.70f, 0.55f, 0.95f, 1f),
                        EmoteCondition => new Vector4(0.45f, 0.85f, 0.65f, 1f),
                        _ => ImGuiColors.DalamudWhite,
                    };
                    ImGui.TextColored(typeColor, cond.Type.ToString());

                    ImGui.TableNextColumn();
                    if (cond is ModCondition modCond)
                    {
                        var modName = modCond.ModName;
                        var modExists = _modService.IsValidMod(modName);
                        if (modExists)
                        {
                            ImGui.TextUnformatted(modName);
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), modName);
                            ImGui.SameLine(0, 4f);
                            ImGuiComponents.HelpMarker("This mod is not currently available in Penumbra.");
                        }
                    }
                    else if (cond is GearCondition gearCond)
                    {
                        var item = _gearDataService?.GetItemByModelId(gearCond.Slot, gearCond.ModelId);
                        if (item is { } gearItem)
                        {
                            var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(gearItem.Icon));
                            if (icon.TryGetWrap(out var wrap, out _))
                            {
                                ImGui.Image(wrap.Handle, new Vector2(18f, 18f));
                                ImGui.SameLine(0, 6f);
                            }

                            var name = gearItem.Name.ToString();
                            var slotName = GearSlotHelper.DisplayName(gearCond.Slot);
                            ImGui.TextUnformatted($"{name} ({slotName} #{gearCond.ModelId})");
                        }
                        else
                        {
                            var slotName = GearSlotHelper.DisplayName(gearCond.Slot);
                            ImGui.TextUnformatted($"Slot {slotName} | Model {gearCond.ModelId}");
                        }
                    }
                    else if (cond is RaceCondition raceCond)
                    {
                        ImGui.TextUnformatted(DescribeRaceCondition(raceCond));
                    }
                    else if (cond is EmoteCondition emoteCond)
                    {
                        var entry = _emoteService.FindById(emoteCond.EmoteId);
                        if (entry is { } emoteEntry)
                        {
                            var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(emoteEntry.IconId));
                            if (icon.TryGetWrap(out var wrap, out _))
                            {
                                ImGui.Image(wrap.Handle, new Vector2(18f, 18f));
                                ImGui.SameLine(0, 6f);
                            }

                            ImGui.TextUnformatted($"{emoteEntry.Name} (#{emoteEntry.Id})");
                        }
                        else
                        {
                            ImGui.TextUnformatted(DescribeEmoteCondition(emoteCond));
                        }
                    }
                    else
                    {
                        ImGui.TextUnformatted("Unknown condition type");
                    }
                }
            }

            ImGui.EndTable();
        }
    }
}
