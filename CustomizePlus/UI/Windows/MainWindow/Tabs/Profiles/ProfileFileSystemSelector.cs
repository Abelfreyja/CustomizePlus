using CustomizePlus.Configuration.Data;
using CustomizePlus.Configuration.Services;
using CustomizePlus.Game.Services;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Events;
using CustomizePlus.UI.Windows.Controls;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using static CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles.ProfileFileSystemSelector;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class ProfileFileSystemSelector : CPlusFileSystemSelector<Profile, ProfileState>
{
    private readonly PluginConfiguration _configuration;
    private readonly ConfigurationService _configurationService;
    private readonly ProfileManager _profileManager;
    private readonly ProfileChanged _event;
    private readonly GameObjectService _gameObjectService;
    private readonly IClientState _clientState;

    private Profile? _cloneProfile;
    private string _newName = string.Empty;

    public bool IncognitoMode
    {
        get => _configuration.UISettings.IncognitoMode;
        set
        {
            _configuration.UISettings.IncognitoMode = value;
            _configurationService.Save(PluginConfigurationChange.Interface);
        }
    }

    public struct ProfileState
    {
        public ColorId Color;
    }

    public ProfileFileSystemSelector(
        ProfileFileSystem fileSystem,
        PluginConfiguration configuration,
        ConfigurationService configurationService,
        ProfileManager profileManager,
        ProfileChanged @event,
        GameObjectService gameObjectService,
        IClientState clientState,
        MessageService messageService)
        : base(messageService, fileSystem, nameof(ProfileFileSystemSelector))
    {
        _configuration = configuration;
        _configurationService = configurationService;
        _profileManager = profileManager;
        _event = @event;
        _gameObjectService = gameObjectService;
        _clientState = clientState;

        _event.Subscribe(OnProfileChange, ProfileChanged.Priority.ProfileFileSystemSelector);

        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;

        AddButton(FontAwesomeIcon.Plus, () => "Create a new profile with default configuration.", () => false, NewButton, 0);
        AddButton(FontAwesomeIcon.Clone, CloneTooltip, () => Selected is null, CloneButton, 20);
        AddButton(FontAwesomeIcon.Trash, () => DeleteSelectionTooltip(_configuration.UISettings.DeleteTemplateModifier, "profile"), DeleteDisabled, DeleteButton, 1000);
        SetFilterTooltip();
    }

    public override void Dispose()
    {
        base.Dispose();
        _event.Unsubscribe(OnProfileChange);
        _clientState.Login -= OnLogin;
        _clientState.Logout -= OnLogout;
    }

    protected override uint ExpandedFolderColorValue
        => ColorId.FolderExpanded.Value();

    protected override uint CollapsedFolderColorValue
        => ColorId.FolderCollapsed.Value();

    protected override bool FoldersDefaultOpen
        => _configuration.UISettings.FoldersDefaultOpen;

    protected override void DrawLeafName(IFileSystemData<Profile> node, in ProfileState state, bool selected)
    {
        var flag = selected ? TreeNodeFlags.Selected | LeafFlags : LeafFlags;
        var name = IncognitoMode ? node.Value.Incognito : node.Value.Name.Text;
        using var color = ImGuiColor.Text.Push(state.Color.Value());
        DrawLeafTreeNode(node, flag, name);
    }

    protected override void DrawPopups()
    {
        DrawNewProfilePopup();
    }

    private void DrawNewProfilePopup()
    {
        if (!UiHelpers.DrawNamePopup("##NewProfile", ref _newName))
            return;

        if (_cloneProfile != null)
        {
            _profileManager.Clone(_cloneProfile, _newName, true);
            _cloneProfile = null;
        }
        else
        {
            _profileManager.Create(_newName, true);
        }

        _newName = string.Empty;
    }

    private void OnProfileChange(in ProfileChanged.Arguments args)
    {
        var (type, profile, arg3) = args;
        switch (type)
        {
            case ProfileChanged.Type.Created:
            case ProfileChanged.Type.Deleted:
            case ProfileChanged.Type.Renamed:
            case ProfileChanged.Type.Toggled:
            case ProfileChanged.Type.AddedCharacter:
            case ProfileChanged.Type.RemovedCharacter:
            case ProfileChanged.Type.ReloadedAll:
                SetFilterDirty();
                break;
        }
    }

    private void OnLogin()
    {
        SetFilterDirty();
    }

    private void OnLogout(int type, int code)
    {
        SetFilterDirty();
    }

    private void NewButton()
    {
        OpenSelectorPopup("##NewProfile");
    }

    private string CloneTooltip()
        => Selected is null
            ? "No profile selected."
            : "Clone the currently selected profile to a duplicate";

    private void CloneButton()
    {
        _cloneProfile = Selected!;
        OpenSelectorPopup("##NewProfile");
    }

    private bool DeleteDisabled()
        => Selected is null || !_configuration.UISettings.DeleteTemplateModifier.IsActive();

    private void DeleteButton()
        => DeleteSelection(_configuration.UISettings.DeleteTemplateModifier, "profile", _profileManager.Delete);

    #region Filters

    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;
    private LowerString _filter = LowerString.Empty;
    private int _filterType = -1;

    private void SetFilterTooltip()
    {
        FilterTooltip = "Filter profiles for those where their full paths or names contain the given substring.\n"
          + "Enter n:[string] to filter only for profile names and no paths.";
    }

    /// <summary> Appropriately identify and set the string filter and its type. </summary>
    protected override bool ChangeFilter(string filterValue)
    {
        (_filter, _filterType) = filterValue.Length switch
        {
            0 => (LowerString.Empty, -1),
            > 1 when filterValue[1] == ':' =>
                filterValue[0] switch
                {
                    'n' => filterValue.Length == 2 ? (LowerString.Empty, -1) : (new LowerString(filterValue[2..]), 1),
                    'N' => filterValue.Length == 2 ? (LowerString.Empty, -1) : (new LowerString(filterValue[2..]), 1),
                    _ => (new LowerString(filterValue), 0),
                },
            _ => (new LowerString(filterValue), 0),
        };

        return true;
    }

    /// <summary>
    /// The overwritten filter method also computes the state.
    /// Folders have default state and are filtered out on the direct string instead of the other options.
    /// If any filter is set, they should be hidden by default unless their children are visible,
    /// or they contain the path search string.
    /// </summary>
    protected override bool ApplyFiltersAndState(IFileSystemNode node, out ProfileState state)
    {
        if (node is IFileSystemFolder folder)
        {
            state = default;
            return FilterValue.Length > 0 && !folder.FullPath.Contains(FilterValue, IgnoreCase);
        }

        if (node is IFileSystemData<Profile> data)
            return ApplyFiltersAndState(data, out state);

        state = default;
        return true;
    }

    /// <summary> Apply the string filters. </summary>
    private bool ApplyStringFilters(IFileSystemData<Profile> node, Profile profile)
    {
        return _filterType switch
        {
            -1 => false,
            0 => !(_filter.IsContained(node.FullPath) || profile.Name.Contains(_filter)),
            1 => !profile.Name.Contains(_filter),
            _ => false, // Should never happen
        };
    }

    /// <summary> Combined wrapper for handling all filters and setting state. </summary>
    private bool ApplyFiltersAndState(IFileSystemData<Profile> node, out ProfileState state)
    {
        state = default;

        var profile = node.Value;
        if (profile is null)
            return true;

        //Do not display temporary profiles;
        if (profile.IsTemporary)
        {
            state.Color = ColorId.DisabledProfile;
            return false;
        }

        //todo: priority check
        var identifier = _gameObjectService.GetCurrentPlayerActorIdentifier();
        if (profile.Enabled)
            state.Color = profile.Characters.Any(x => x.MatchesIgnoringOwnership(identifier)) ? ColorId.LocalCharacterEnabledProfile : ColorId.EnabledProfile;
        else
            state.Color = profile.Characters.Any(x => x.MatchesIgnoringOwnership(identifier)) ? ColorId.LocalCharacterDisabledProfile : ColorId.DisabledProfile;

        //todo: missing actor color

        return ApplyStringFilters(node, profile);
    }

    #endregion
}
