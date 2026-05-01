using CustomizePlus.Core.Services;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates.Events;
using Dalamud.Interface.ImGuiNotification;

namespace CustomizePlus.Templates;

public sealed class TemplateFileSystem : BaseFileSystem, IDisposable
{
    private readonly TemplateManager _templateManager;
    private readonly TemplateChanged _templateChanged;
    private readonly MessageService _messageService;
    private readonly FileSystemSaveService<Template> _saver;

    public TemplateFileSystem(
        TemplateManager templateManager,
        SaveService saveService,
        TemplateChanged templateChanged,
        MessageService messageService,
        Logger logger)
        : base("TemplateFileSystem", logger, true)
    {
        _templateManager = templateManager;
        _templateChanged = templateChanged;
        _messageService = messageService;
        _saver = new FileSystemSaveService<Template>(
            logger,
            this,
            saveService,
            _templateManager.Templates,
            TemplateFromIdentifier,
            fileNames => fileNames.TemplateLockedNodes,
            fileNames => fileNames.TemplateExpandedFolders,
            fileNames => fileNames.TemplateSelectedNodes,
            fileNames => fileNames.TemplateOrganization,
            fileNames => fileNames.LegacyTemplateSortOrder);

        _templateChanged.Subscribe(OnTemplateChange, TemplateChanged.Priority.TemplateFileSystem);
        _saver.Load();
    }

    public void Dispose()
    {
        _templateChanged.Unsubscribe(OnTemplateChange);
        _saver.Dispose();
        Selection.Dispose();
    }

    private Template? TemplateFromIdentifier(string identifier)
        => Guid.TryParse(identifier, out var id)
            ? _templateManager.GetTemplate(id)
            : null;

    private void OnTemplateChange(in TemplateChanged.Arguments args)
    {
        var (type, template, data) = args;
        switch (type)
        {
            case TemplateChanged.Type.Created when template is not null:
                var parent = Root;
                if (data is string path)
                {
                    try
                    {
                        parent = FindOrCreateAllFolders(path);
                    }
                    catch (Exception ex)
                    {
                        _messageService.NotificationMessage(ex, $"Could not move template to {path} because the folder could not be created.", NotificationType.Error);
                    }
                }

                CreateDuplicateDataNode(parent, template.Name.Text, template);
                return;
            case TemplateChanged.Type.Deleted when template?.Node is { } node:
                Delete(node);
                return;
            case TemplateChanged.Type.ReloadedAll:
                _saver.Load();
                return;
            case TemplateChanged.Type.Renamed when template?.Node is { } node && data is string oldName:
                var old = oldName.FixName();
                var name = node.Name.ToString();
                if (old == name || (name.IsDuplicateName(out var baseName, out _) && baseName == old))
                    RenameWithDuplicates(node, template.Name.Text);
                return;
        }
    }
}
