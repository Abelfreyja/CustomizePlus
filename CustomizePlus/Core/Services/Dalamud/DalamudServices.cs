using Dalamud.Plugin;

namespace CustomizePlus.Core.Services.Dalamud;

#pragma warning disable SeStringEvaluator

public class DalamudServices
{
    public static void AddServices(ServiceManager services, IDalamudPluginInterface pi)
    {
        services.AddDalamudServices(pi);
    }
}