using CustomizePlus.Core.Services;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Events;
using Dalamud.Interface.ImGuiNotification;

namespace CustomizePlus.Profiles;

public sealed class ProfileFileSystem : BaseFileSystem, IDisposable
{
    private readonly ProfileManager _profileManager;
    private readonly ProfileChanged _profileChanged;
    private readonly MessageService _messageService;
    private readonly FileSystemSaveService<Profile> _saver;

    public ProfileFileSystem(
        ProfileManager profileManager,
        SaveService saveService,
        ProfileChanged profileChanged,
        MessageService messageService,
        Logger logger)
        : base("ProfileFileSystem", logger, true)
    {
        _profileManager = profileManager;
        _profileChanged = profileChanged;
        _messageService = messageService;
        _saver = new FileSystemSaveService<Profile>(
            logger,
            this,
            saveService,
            _profileManager.Profiles.Where(p => !p.IsTemporary),
            ProfileFromIdentifier,
            fileNames => fileNames.ProfileLockedNodes,
            fileNames => fileNames.ProfileExpandedFolders,
            fileNames => fileNames.ProfileSelectedNodes,
            fileNames => fileNames.ProfileOrganization,
            fileNames => fileNames.LegacyProfileSortOrder);

        _profileChanged.Subscribe(OnProfileChange, ProfileChanged.Priority.ProfileFileSystem);
        _saver.Load();
    }

    public void Dispose()
    {
        _profileChanged.Unsubscribe(OnProfileChange);
        _saver.Dispose();
        Selection.Dispose();
    }

    private Profile? ProfileFromIdentifier(string identifier)
        => Guid.TryParse(identifier, out var id)
            ? _profileManager.Profiles.FirstOrDefault(profile => profile.UniqueId == id && !profile.IsTemporary)
            : null;

    private void OnProfileChange(in ProfileChanged.Arguments args)
    {
        var (type, profile, data) = args;
        switch (type)
        {
            case ProfileChanged.Type.Created when profile is not null:
                var parent = Root;
                if (data is string path)
                {
                    try
                    {
                        parent = FindOrCreateAllFolders(path);
                    }
                    catch (Exception ex)
                    {
                        _messageService.NotificationMessage(ex, $"Could not move profile to {path} because the folder could not be created.", NotificationType.Error);
                    }
                }

                CreateDuplicateDataNode(parent, profile.Name.Text, profile);
                return;
            case ProfileChanged.Type.Deleted when profile?.Node is { } node:
                Delete(node);
                return;
            case ProfileChanged.Type.ReloadedAll:
                _saver.Load();
                return;
            case ProfileChanged.Type.Renamed when profile?.Node is { } node && data is string oldName:
                var old = oldName.FixName();
                var name = node.Name.ToString();
                if (old == name || (name.IsDuplicateName(out var baseName, out _) && baseName == old))
                    RenameWithDuplicates(node, profile.Name.Text);
                return;
        }
    }
}
