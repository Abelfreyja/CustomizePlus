using CustomizePlus.Configuration.Data;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates.Events;
using CustomizePlus.UI;
using Dalamud.Bindings.ImGui;
using ImSharp;

namespace CustomizePlus.UI.Windows.Controls;

public abstract class TemplateComboBase : SimpleFilterCombo<Tuple<Template, string>>, IDisposable
{
    private readonly Func<IReadOnlyList<Tuple<Template, string>>> _generator;
    private readonly PluginConfiguration _configuration;
    private readonly TemplateChanged _templateChanged;
    // protected readonly TabSelected TabSelected;

    private Template? _currentTemplate;

    protected Tuple<Template, string>? CurrentSelection;

    protected TemplateComboBase(
        Func<IReadOnlyList<Tuple<Template, string>>> generator,
        TemplateChanged templateChanged,
        //TabSelected tabSelected,
        PluginConfiguration configuration)
        : base(SimpleFilterType.Partwise)
    {
        _generator = generator;
        _templateChanged = templateChanged;
        //TabSelected = tabSelected;
        _configuration = configuration;
        _templateChanged.Subscribe(OnTemplateChange, TemplateChanged.Priority.TemplateCombo);
    }

    public bool Incognito
        => _configuration.UISettings.IncognitoMode;

    void IDisposable.Dispose()
        => _templateChanged.Unsubscribe(OnTemplateChange);

    public override StringU8 DisplayString(in Tuple<Template, string> value)
        => new(value.Item1.Name.Text);

    public override string FilterString(in Tuple<Template, string> value)
        => $"{value.Item2}\0{value.Item1.Name.Lower}";

    public override ColorParameter TextColor(in Tuple<Template, string> value)
        => ColorId.UsedTemplate.Value();

    public override IEnumerable<Tuple<Template, string>> GetBaseItems()
        => _generator();

    protected override bool DrawItem(in SimpleCacheItem<Tuple<Template, string>> item, int globalIndex, bool selected)
    {
        using var color = Im.Color.Push(ImGuiColor.Text, item.TextColor);
        var ret = Im.Selectable(item.DisplayString, selected);
        DrawPath(item.Item.Item2, item.Item.Item1);

        return ret;
    }

    protected override bool IsSelected(SimpleCacheItem<Tuple<Template, string>> item, int globalIndex)
        => ReferenceEquals(item.Item.Item1, _currentTemplate);

    private static void DrawPath(string path, Template template)
    {
        if (path.Length <= 0 || template.Name == path)
            return;

        DrawRightAligned(template.Name, path, ImGui.GetColorU32(ImGuiCol.TextDisabled));
    }

    protected bool Draw(Template? currentTemplate, string? label, float width)
    {
        _currentTemplate = currentTemplate;
        var name = label ?? "Select Template Here...";
        var ret = base.Draw("##template", name, string.Empty, width, out var selection);
        CurrentSelection = selection?.Item;

        _currentTemplate = null;

        return ret;
    }

    private void OnTemplateChange(in TemplateChanged.Arguments args)
    {
        var type = args.Type;
        if (type is TemplateChanged.Type.Created or TemplateChanged.Type.Renamed or TemplateChanged.Type.Deleted)
            CacheManager.Instance.SetDirty(CurrentId);
    }

    private static void DrawRightAligned(string leftText, string text, uint color)
    {
        var start = ImGui.GetItemRectMin();
        var pos = start.X + ImGui.CalcTextSize(leftText).X;
        var maxSize = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        var remainingSpace = maxSize - pos;
        var requiredSize = ImGui.CalcTextSize(text).X + ImGui.GetStyle().ItemInnerSpacing.X;
        var offset = remainingSpace - requiredSize;
        if (ImGui.GetScrollMaxY() == 0)
            offset -= ImGui.GetStyle().ItemInnerSpacing.X;

        if (offset < ImGui.GetStyle().ItemSpacing.X)
            UiHelpers.DrawHoverTooltip(text);
        else
            ImGui.GetWindowDrawList().AddText(start with { X = pos + offset },
                color, text);
    }
}

public sealed class TemplateCombo : TemplateComboBase
{
    private readonly ProfileManager _profileManager;

    public TemplateCombo(
        TemplateManager templateManager,
        ProfileManager profileManager,
        TemplateChanged templateChanged,
        //TabSelected tabSelected,
        PluginConfiguration configuration)
        : base(
            () => templateManager.Templates
                .Select(d => new Tuple<Template, string>(d, d.Node?.FullPath ?? string.Empty))
                .OrderBy(d => d.Item2)
                .ToList(), templateChanged,/* tabSelected, */configuration)
    {
        _profileManager = profileManager;
    }

    public Template? Template
        => CurrentSelection?.Item1;

    public void Draw(Profile profile, Template? template, int templateIndex)
    {
        if (!Draw(template, Incognito ? template?.Incognito : template?.Name, ImGui.GetContentRegionAvail().X))
            return;

        if (templateIndex >= 0)
            _profileManager.ChangeTemplate(profile, templateIndex, CurrentSelection!.Item1);
        else
            _profileManager.AddTemplate(profile, CurrentSelection!.Item1);
    }
}
