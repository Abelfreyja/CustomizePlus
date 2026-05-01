using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CustomizePlus.Core.Services;

public sealed class FileSystemSaveService<T>(
    Logger log,
    BaseFileSystem fileSystem,
    SaveService saveService,
    IEnumerable<T> values,
    Func<string, T?> valueFromIdentifier,
    Func<FilenameService, string> lockedFile,
    Func<FilenameService, string> expandedFile,
    Func<FilenameService, string> selectedFile,
    Func<FilenameService, string> organizationFile,
    Func<FilenameService, string> migrationFile)
    : FileSystemSaver<SaveService, FilenameService>(log, fileSystem, saveService)
    where T : class, IFileSystemValue<T>, ISavable
{
    private static readonly Dictionary<string, ISortMode> SortModes = new(StringComparer.Ordinal)
    {
        [nameof(ISortMode.FoldersFirst)] = ISortMode.FoldersFirst,
        [nameof(ISortMode.Lexicographical)] = ISortMode.Lexicographical,
        [nameof(ISortMode.InverseFoldersFirst)] = ISortMode.InverseFoldersFirst,
        [nameof(ISortMode.InverseLexicographical)] = ISortMode.InverseLexicographical,
        [nameof(ISortMode.FoldersLast)] = ISortMode.FoldersLast,
        [nameof(ISortMode.InverseFoldersLast)] = ISortMode.InverseFoldersLast,
        [nameof(ISortMode.InternalOrder)] = ISortMode.InternalOrder,
        [nameof(ISortMode.InverseInternalOrder)] = ISortMode.InverseInternalOrder,
    };

    public override void Load()
    {
        MigrateLegacySortOrder();
        base.Load();
    }

    protected override string LockedFile(FilenameService provider)
        => lockedFile(provider);

    protected override string ExpandedFile(FilenameService provider)
        => expandedFile(provider);

    protected override string SelectionFile(FilenameService provider)
        => selectedFile(provider);

    protected override string OrganizationFile(FilenameService provider)
        => organizationFile(provider);

    protected override string MigrationFile(FilenameService provider)
        => migrationFile(provider);

    protected override bool GetValueFromIdentifier(ReadOnlySpan<char> identifier, [NotNullWhen(true)] out IFileSystemValue? value)
    {
        value = valueFromIdentifier(identifier.ToString());
        return value is not null;
    }

    protected override void CreateDataNodes()
    {
        foreach (var value in values)
        {
            var folder = value.Path.Folder.Length is 0
                ? FileSystem.Root
                : FileSystem.FindOrCreateAllFolders(value.Path.Folder);
            FileSystem.CreateDuplicateDataNode(folder, value.Path.GetIntendedName(value.DisplayName), value);
        }
    }

    protected override ISortMode? ParseSortMode(string name)
        => SortModes.GetValueOrDefault(name);

    protected override void SaveDataValue(IFileSystemValue value)
    {
        if (value is T typedValue)
            SaveService.QueueSave(typedValue);
    }

    private void MigrateLegacySortOrder()
    {
        var file = MigrationFile(SaveService.FileNames);
        if (file.Length is 0 || !File.Exists(file))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind is not JsonValueKind.Object || document.RootElement.TryGetProperty("Data", out _))
                return;

            Log.Information($"Migrating legacy Customize+ sort order {file} to Luna file system...");
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is not JsonValueKind.String || property.Value.GetString() is not { } path)
                {
                    Log.Warning($"Ignoring invalid legacy file system entry {property.Name} in {file}.");
                    continue;
                }

                if (!GetValueFromIdentifier(property.Name, out var value))
                {
                    Log.Warning($"Data Value {property.Name} with path {path} could not be found.");
                    continue;
                }

                ApplyMigrationToData(value, path);
            }

            File.Move(file, file + ".bak", true);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to migrate legacy Customize+ sort order {file}:\n{ex}");
        }
    }
}
