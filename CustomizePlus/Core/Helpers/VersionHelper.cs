#if !DEBUG
using System.Reflection;
#endif

using Dalamud.Plugin;

namespace CustomizePlus.Core.Helpers;

internal static class VersionHelper
{
    //for the love of god don't be an asshole and never change this.
    public const string AetherToolsRepo = "https://raw.githubusercontent.com/aether-tools/dalamudplugins/main/repo.json";
    public const string SeaOfStarsRepo = "https://raw.githubusercontent.com/ottermandias/seaofstars/main/repo.json";

    public static string Version { get; private set; } = "Initializing";

    public static bool IsTesting { get; private set; } = false;

    public static bool IsDebug { get; private set; } = false;

    public static bool IsValidate { get; private set; } = false;

    static VersionHelper()
    {
#if DEBUG
        Version = $"{ThisAssembly.Git.Commit}+{ThisAssembly.Git.Sha} [DEBUG]";
        IsDebug = true;
#else
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
#endif

        if (ThisAssembly.Git.BaseTag.ToLowerInvariant().Contains("testing"))
            IsTesting = true;

#if VALIDATE_BUILD
        IsValidate = true;
#endif

        if (IsTesting)
            Version += " [TESTING BUILD]";

        if (IsValidate)
            Version += " [VALIDATE BUILD]";
    }

    public static bool IsTrustedBuild(IDalamudPluginInterface pi) =>
      pi.SourceRepository?.Trim().ToLowerInvariant() switch
      {
          null => false,
          AetherToolsRepo => true,
          SeaOfStarsRepo => true,
          _ => false,
      };

    public static string GetInstallationSource(IDalamudPluginInterface pi)
    {
        var path = pi.AssemblyLocation.Directory?.FullName;

        try
        {
            if (!string.IsNullOrEmpty(path))
            {
                path = Path.GetFullPath(path);

                //we don't want to expose windows username if we can
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (path.StartsWith(appData, StringComparison.OrdinalIgnoreCase))
                    path = path.Replace(appData, "%AppData%", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            path = "Unable to obtain installation directory";
        }

        return $"{pi.SourceRepository} ({path ?? "Unknown installation directory"})";
    }
}
