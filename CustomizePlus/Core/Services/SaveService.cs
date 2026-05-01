using System.Text;

namespace CustomizePlus.Core.Services;

/// <summary>
/// Any file type that we want to save via SaveService.
/// </summary>
public interface ISavable : ISavable<FilenameService>
{
    string ToFilename(FilenameService fileNames);

    void Save(StreamWriter writer);

    string ISavable<FilenameService>.ToFilePath(FilenameService fileNames)
        => ToFilename(fileNames);

    void ISavable<FilenameService>.Save(Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        Save(writer);
    }
}

public sealed class SaveService : BaseSaveService<FilenameService>
{
    public SaveService(Logger logger, FrameworkManager framework, FilenameService fileNames)
        : base(logger, framework, fileNames)
    {
        BackupMode = BackupMode.SingleBackup;
    }
}
