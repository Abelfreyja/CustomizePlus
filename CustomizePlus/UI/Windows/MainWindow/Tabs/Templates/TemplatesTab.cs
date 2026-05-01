using CustomizePlus.Configuration.Data;
using CustomizePlus.Configuration.Services;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;

public class TemplatesTab : TwoPanelLayout
{
    private readonly PluginConfiguration _configuration;
    private readonly ConfigurationService _configurationService;
    private readonly TemplateFileSystemSelector _selector;

    public TemplatesTab(TemplateFileSystemSelector selector, TemplatePanel panel, PluginConfiguration configuration, ConfigurationService configurationService)
    {
        _configuration = configuration;
        _configurationService = configurationService;
        _selector = selector;
        LeftHeader = selector.Header;
        LeftFooter = selector.Footer;
        LeftPanel = selector;
        RightHeader = panel;
        RightFooter = EmptyHeaderFooter.Instance;
        RightPanel = panel;
    }

    public override ReadOnlySpan<byte> Label
        => "TemplatesTab"u8;

    public void Draw()
        => Draw(new TwoPanelWidth(_configuration.UISettings.CurrentTemplateSelectorWidth, ScalingMode.Absolute));

    protected override void SetWidth(float width, ScalingMode mode)
    {
        var adaptedSize = MathF.Round(width / Im.Style.GlobalScale);
        if (Math.Abs(adaptedSize - _configuration.UISettings.CurrentTemplateSelectorWidth) < 0.1f)
            return;

        _configuration.UISettings.CurrentTemplateSelectorWidth = adaptedSize;
        _configurationService.Save(PluginConfigurationChange.Layout);
    }

    protected override void DrawPopups()
        => _selector.DrawSelectorPopups();

    protected override float MinimumWidth
        => Im.ContentRegion.Available.X * _configuration.UISettings.TemplateSelectorMinimumScale;

    protected override float MaximumWidth
        => MathF.Max(MinimumWidth, MathF.Min(
            Im.ContentRegion.Available.X * _configuration.UISettings.TemplateSelectorMaximumScale,
            Im.ContentRegion.Available.X - 470 * Im.Style.GlobalScale));
}
