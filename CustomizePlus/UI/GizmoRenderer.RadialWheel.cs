using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary> radial wheel rendering for <see cref="GizmoRenderer"/>.</summary>
public sealed partial class GizmoRenderer
{
    private void DrawOptionWheel()
    {
        if (_centerBoneSelectorActive)
        {
            if (IsOptionWheelOpen() && ImGui.BeginPopup(OptionWheelPopupId))
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var radius = OptionWheelBaseRadius * scale;
        var innerRadius = radius * OptionWheelInnerRadiusFraction;
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(_optionWheelCenter - new Vector2(radius), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(radius * 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var flags = ImGuiWindowFlags.NoDecoration
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoBackground
                    | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav;

        var activeAttribute = _gizmoService.ActiveAttribute;
        var selectedBoneFavorite = IsSelectedBoneFavorite();
        var propagationState = GetPropagationVisualState(activeAttribute);

        if (ImGui.BeginPopup(OptionWheelPopupId, flags))
        {
            using var alphaScope = PushGizmoAlpha(true);
            var drawList = ImGui.GetForegroundDrawList();
            var windowPos = ImGui.GetWindowPos();
            var center = windowPos + new Vector2(radius);
            var mousePos = ImGui.GetIO().MousePos;

            var backgroundColor = ImGui.GetColorU32(GizmoColors.WheelBackground);
            drawList.AddCircleFilled(center, radius, backgroundColor, 96);
            drawList.AddCircleFilled(center, innerRadius, backgroundColor, 72);
            drawList.AddCircle(center, radius, ImGui.GetColorU32(GizmoColors.WheelOutline), 96, 3.5f * scale);
            drawList.AddCircle(center, innerRadius, ImGui.GetColorU32(GizmoColors.WheelOutline), 72, 3f * scale);

            var ringInner = innerRadius * 0.92f;
            var ringOuter = radius * 0.98f;
            var useWorldSpace = _gizmoService.UseWorldSpace;
            var mirrorEnabled = _gizmoService.MirrorMode;

            var segments = _radialActionsPage
                ? BuildSecondarySegments(selectedBoneFavorite, in propagationState, activeAttribute)
                : BuildPrimarySegments(activeAttribute, useWorldSpace, mirrorEnabled);

            var segmentSweep = TwoPi / segments.Count;
            var baseAngle = (-MathF.PI / 2f) - (segmentSweep * 0.5f);
            var hoveredIndex = -1;

            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var startAngle = baseAngle + (segmentSweep * i);
                var endAngle = startAngle + segmentSweep;
                var isHovered = IsPointInRingSegment(mousePos, center, ringInner, ringOuter, startAngle, endAngle);
                if (isHovered)
                    hoveredIndex = i;

                var fillColor = GizmoColors.WheelFill(segment.Color, segment.IsActive, isHovered);

                DrawRingSegment(
                    drawList,
                    center,
                    ringInner,
                    ringOuter,
                    startAngle,
                    endAngle,
                    ImGui.GetColorU32(fillColor));

                var labelAngle = (startAngle + endAngle) * 0.5f;
                var labelRadius = (ringInner + ringOuter) * 0.5f;
                var labelPosition = center + (new Vector2(MathF.Cos(labelAngle), MathF.Sin(labelAngle)) * labelRadius);
                var iconColor = GizmoColors.WheelSegmentIcon(segment.IsActive);
                DrawCenteredWheelIcon(drawList, labelPosition, segment.Icon, iconColor);
            }

            var innerBandOuter = Math.Max(ringInner - (1.5f * scale), innerRadius * 0.92f);
            var innerBandInner = Math.Max(innerBandOuter - (6f * scale), innerRadius * 0.8f);
            drawList.PathClear();
            drawList.PathArcTo(center, innerBandOuter, 0f, TwoPi, 64);
            drawList.PathArcTo(center, innerBandInner, TwoPi, 0f, 64);
            drawList.PathFillConvex(ImGui.GetColorU32(GizmoColors.WheelMask));

            var innerMaskRadius = Math.Max(innerBandInner - (2f * scale), innerRadius * 0.75f);
            var maskColor = ImGui.GetColorU32(GizmoColors.WheelMask);
            drawList.AddCircleFilled(center, innerMaskRadius, maskColor, 96);

            var centerButtonRadius = innerMaskRadius * 0.55f;
            var centerHoveredButton = Vector2.Distance(mousePos, center) <= centerButtonRadius;
            var centerColor = GizmoColors.WheelCenterButton(_radialActionsPage);
            drawList.AddCircleFilled(center, centerButtonRadius, ImGui.GetColorU32(centerColor), 64);
            drawList.AddCircle(center, centerButtonRadius, ImGui.GetColorU32(GizmoColors.WheelOutline), 64, 2f * scale);
            var toggleIcon = _radialActionsPage ? FontAwesomeIcon.Tools : FontAwesomeIcon.LayerGroup;
            var toggleColor = GizmoColors.WheelCenterIcon(_radialActionsPage);
            DrawCenteredWheelIcon(drawList, center, toggleIcon, toggleColor);

            if (hoveredIndex >= 0)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            else if (centerHoveredButton)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            SetPendingWheelTooltip(mousePos, centerHoveredButton, hoveredIndex, segments);

            var activated = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            var cancel = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
            var centerClicked = centerHoveredButton && activated;

            if (centerClicked)
            {
                _radialActionsPage = !_radialActionsPage;
            }
            else if (activated && hoveredIndex >= 0)
            {
                segments[hoveredIndex].OnClick();
                ImGui.CloseCurrentPopup();
            }
            else if ((activated && hoveredIndex < 0) || cancel)
            {
                if (cancel)
                    _wheelSuppressNextToggle = true;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar(3);
    }

    private static void DrawCenteredWheelIcon(ImDrawListPtr drawList, Vector2 center, FontAwesomeIcon icon, Vector4 color)
    {
        var iconText = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(iconText);
        ImGui.PopFont();
        drawList.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize, center - (iconSize * 0.5f), ImGui.GetColorU32(color), iconText);
    }

    private void SetPendingWheelTooltip(Vector2 mousePos, bool centerHoveredButton, int hoveredIndex, IReadOnlyList<WheelSegmentDescriptor> segments)
    {
        if (centerHoveredButton)
        {
            _pendingRadialTooltip = new RadialTooltipInfo(
                mousePos,
                _radialActionsPage ? "Show Primary Actions" : "Show Advanced Actions");
        }

        if (hoveredIndex < 0)
            return;

        var hoveredSegment = segments[hoveredIndex];
        _pendingRadialTooltip = hoveredSegment.TooltipLines != null && hoveredSegment.TooltipLines.Count > 0
            ? new RadialTooltipInfo(mousePos, hoveredSegment.Tooltip, hoveredSegment.TooltipLines)
            : new RadialTooltipInfo(mousePos, hoveredSegment.Tooltip);
    }

    private static void DrawRingSegment(
        ImDrawListPtr drawList,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        float startAngle,
        float endAngle,
        uint fillColor)
    {
        const int steps = 48;
        drawList.PathClear();
        drawList.PathArcTo(center, outerRadius, startAngle, endAngle, steps);
        drawList.PathArcTo(center, innerRadius, endAngle, startAngle, steps);
        drawList.PathFillConvex(fillColor);
    }

    private static WheelSegmentDescriptor CreateWheelSegment(
        FontAwesomeIcon icon,
        string tooltip,
        Vector4 color,
        bool isActive,
        Action onClick,
        IReadOnlyList<RadialTooltipLine>? tooltipLines = null)
        => new(icon, tooltip, color, isActive, onClick, tooltipLines);

    private List<WheelSegmentDescriptor> BuildPrimarySegments(BoneAttribute activeAttribute, bool useWorldSpace, bool mirrorEnabled)
    {
        return new List<WheelSegmentDescriptor>
        {
            CreateWheelSegment(
                FontAwesomeIcon.ArrowsAlt,
                "Position",
                GizmoColors.WheelAttribute(BoneAttribute.Position),
                activeAttribute == BoneAttribute.Position,
                () => _gizmoService.SetActiveAttribute(BoneAttribute.Position)),
            CreateWheelSegment(
                FontAwesomeIcon.Sync,
                "Rotation",
                GizmoColors.WheelAttribute(BoneAttribute.Rotation),
                activeAttribute == BoneAttribute.Rotation,
                () => _gizmoService.SetActiveAttribute(BoneAttribute.Rotation)),
            CreateWheelSegment(
                FontAwesomeIcon.CompressArrowsAlt,
                "Scale",
                GizmoColors.WheelAttribute(BoneAttribute.Scale),
                activeAttribute == BoneAttribute.Scale,
                () => _gizmoService.SetActiveAttribute(BoneAttribute.Scale)),
            CreateWheelSegment(
                FontAwesomeIcon.Crosshairs,
                "Local Space",
                GizmoColors.WheelLocalSpace,
                !useWorldSpace,
                () => _gizmoService.SetUseWorldSpace(false)),
            CreateWheelSegment(
                FontAwesomeIcon.Globe,
                "World Space",
                GizmoColors.WheelWorldSpace,
                useWorldSpace,
                () => _gizmoService.SetUseWorldSpace(true)),
            CreateWheelSegment(
                FontAwesomeIcon.GripLinesVertical,
                "Mirror Mode",
                GizmoColors.WheelMirrorMode,
                mirrorEnabled,
                () => _gizmoService.SetMirrorMode(!mirrorEnabled)),
        };
    }

    private static string BuildBoneTooltip(string? boneDisplayName, Func<string, string> withBoneText, string fallbackText)
        => boneDisplayName != null
            ? withBoneText(boneDisplayName)
            : fallbackText;

    private List<WheelSegmentDescriptor> BuildSecondarySegments(bool favoriteActive, in PropagationVisualState propagationState, BoneAttribute attributeForText)
    {
        var propagationColor = propagationState.IsEnabled ? propagationState.AccentColor : GizmoColors.PropagationDefault;
        var selectedBone = _gizmoService.SelectedBone;
        var boneDisplay = string.IsNullOrEmpty(selectedBone) ? null : BoneData.GetBoneDisplayName(selectedBone);
        var favoriteTooltip = BuildBoneTooltip(boneDisplay, display => $"Toggle favorite on '{display}' bone", "Toggle favorite on selected bone");
        var propagationTooltip = BuildBoneTooltip(boneDisplay, display => $"Apply '{display}' transformations to its child bones", "Apply transformations to child bones");
        var attributeName = attributeForText.GetFriendlyName();
        var resetTooltip = BuildBoneTooltip(boneDisplay, display => $"Reset '{display}' to default {attributeName} values", "Reset selected bone to default values");
        var revertTooltip = BuildBoneTooltip(boneDisplay, display => $"Revert '{display}' to last saved {attributeName} values", "Revert selected bone to last saved values");

        return new List<WheelSegmentDescriptor>
        {
            CreateWheelSegment(
                FontAwesomeIcon.Undo,
                "Undo",
                GizmoColors.WheelUndo,
                false,
                () => _gizmoService.ExecuteCommand(TemplateEditorService.EditorCommand.Undo)),
            CreateWheelSegment(
                FontAwesomeIcon.Redo,
                "Redo",
                GizmoColors.WheelRedo,
                false,
                () => _gizmoService.ExecuteCommand(TemplateEditorService.EditorCommand.Redo)),
            CreateWheelSegment(
                FontAwesomeIcon.Star,
                favoriteTooltip,
                GizmoColors.WheelFavorite,
                favoriteActive,
                () => _gizmoService.ExecuteCommand(TemplateEditorService.EditorCommand.ToggleFavorite)),
            CreateWheelSegment(
                FontAwesomeIcon.Link,
                propagationTooltip,
                propagationColor,
                propagationState.IsEnabled,
                () => _gizmoService.ExecuteCommand(TemplateEditorService.EditorCommand.TogglePropagation),
                propagationState.TooltipLines),
            CreateWheelSegment(
                FontAwesomeIcon.Recycle,
                resetTooltip,
                GizmoColors.WheelReset,
                false,
                () => _gizmoService.ExecuteCommand(TemplateEditorService.EditorCommand.ResetAttribute)),
            CreateWheelSegment(
                FontAwesomeIcon.ArrowCircleLeft,
                revertTooltip,
                GizmoColors.WheelRevert,
                false,
                () => _gizmoService.ExecuteCommand(TemplateEditorService.EditorCommand.RevertAttribute)),
        };
    }

    private bool IsSelectedBoneFavorite()
    {
        var bone = _gizmoService.SelectedBone;
        if (string.IsNullOrEmpty(bone))
            return false;

        return _gizmoService.IsFavoriteBone(bone);
    }

    private PropagationVisualState GetPropagationVisualState(BoneAttribute attribute)
    {
        var template = _editorManager.CurrentlyEditedTemplate;
        var bone = _gizmoService.SelectedBone;
        if (template == null || string.IsNullOrEmpty(bone) || !template.Bones.TryGetValue(bone, out var transform))
            return new PropagationVisualState(false, GizmoColors.PropagationDefault, null);

        var enabled = transform.IsPropagationEnabledForAttribute(attribute);

        if (!enabled)
            return new PropagationVisualState(false, GizmoColors.PropagationDefault, null);

        var family = BoneData.GetBoneFamily(bone);
        var accentColor = Constants.PropagationColors.GetParentColor(family);
        var armature = TryGetArmature(_gizmoService.CurrentActor, out var currentArmature) ? currentArmature : null;
        var lines = BuildPropagationTooltipLines(template, bone, attribute, armature);
        return new PropagationVisualState(true, accentColor, lines);
    }

    private IReadOnlyList<RadialTooltipLine>? BuildPropagationTooltipLines(Template template, string boneName, BoneAttribute attribute, Armature? armature)
    {
        var ancestors = EnumerateAncestorNames(boneName, armature);
        if (ancestors.Count == 0)
            return null;

        var lines = new List<RadialTooltipLine>(ancestors.Count);

        foreach (var ancestor in ancestors)
        {
            var display = BoneData.GetBoneDisplayName(ancestor);
            var family = BoneData.GetBoneFamily(ancestor);
            var isSource = template.Bones.TryGetValue(ancestor, out var transform)
                && transform.IsPropagationEnabledForAttribute(attribute);
            var color = Constants.PropagationColors.GetTooltipColor(family, isSource);
            lines.Add(new RadialTooltipLine($"- {display} ({ancestor})", color));
        }

        return lines;
    }

    private static List<string> EnumerateAncestorNames(string boneName, Armature? armature)
    {
        var modelBone = armature != null ? FindModelBone(armature, boneName) : null;
        return BoneRelationHelper.EnumerateAncestors(modelBone, boneName);
    }

    private void HandleOptionWheelInput(bool pointerInRegion)
    {
        if (_dragState.IsDragging || _centerBoneSelectorActive)
            return;

        var io = ImGui.GetIO();
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            if (pointerInRegion && !_wheelSuppressNextToggle)
            {
                _optionWheelCenter = io.MousePos;
                ImGui.OpenPopup(OptionWheelPopupId);
            }

            _wheelSuppressNextToggle = false;
        }
    }

    private void TriggerCenterHighlight()
        => _centerHighlightUntil = ImGui.GetTime() + CenterHighlightDuration;

    private static void DrawRadialTooltip(in RadialTooltipInfo tooltip)
    {
        var drawList = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        drawList.PushClipRectFullScreen();
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(8f * scale, 5f * scale);
        var offset = new Vector2(16f * scale, 18f * scale);
        var lineSpacing = 2f * scale;
        var rectMin = tooltip.MousePosition + offset;
        var hasTitle = !string.IsNullOrEmpty(tooltip.Title);
        var titleSize = hasTitle ? ImGui.CalcTextSize(tooltip.Title) : Vector2.Zero;
        var maxWidth = hasTitle ? titleSize.X : 0f;
        var totalHeight = hasTitle ? titleSize.Y : 0f;
        var hasLines = tooltip.Lines != null && tooltip.Lines.Count > 0;
        if (hasLines)
        {
            if (hasTitle)
                totalHeight += lineSpacing;

            foreach (var line in tooltip.Lines!)
            {
                var size = ImGui.CalcTextSize(line.Text);
                maxWidth = MathF.Max(maxWidth, size.X);
                totalHeight += size.Y + lineSpacing;
            }

            totalHeight -= lineSpacing;
        }

        if (totalHeight <= 0f)
        {
            var defaultSize = ImGui.GetTextLineHeight();
            totalHeight = defaultSize;
            maxWidth = MathF.Max(maxWidth, defaultSize);
        }

        var rectMax = rectMin + new Vector2(maxWidth, totalHeight) + (padding * 2f);
        var backgroundColor = ImGui.GetColorU32(GizmoColors.RadialTooltipBackground);
        var borderColor = ImGui.GetColorU32(GizmoColors.RadialTooltipBorder);
        drawList.AddRectFilled(rectMin, rectMax, backgroundColor, 0f);
        drawList.AddRect(rectMin, rectMax, borderColor, 0f, ImDrawFlags.None, 1.4f * scale);

        var cursor = rectMin + padding;
        if (hasTitle)
        {
            drawList.AddText(cursor, ImGui.GetColorU32(ImGuiCol.Text), tooltip.Title);
            cursor.Y += titleSize.Y;
        }

        if (hasLines)
        {
            if (hasTitle)
                cursor.Y += lineSpacing;

            foreach (var line in tooltip.Lines!)
            {
                drawList.AddText(cursor, ImGui.GetColorU32(line.Color), line.Text);
                var lineSize = ImGui.CalcTextSize(line.Text);
                cursor.Y += lineSize.Y + lineSpacing;
            }
        }

        drawList.PopClipRect();
    }

    private readonly struct WheelSegmentDescriptor
    {
        public WheelSegmentDescriptor(
            FontAwesomeIcon icon,
            string tooltip,
            Vector4 color,
            bool isActive,
            Action onClick,
            IReadOnlyList<RadialTooltipLine>? tooltipLines = null)
        {
            Icon = icon;
            Tooltip = tooltip;
            Color = color;
            IsActive = isActive;
            OnClick = onClick;
            TooltipLines = tooltipLines;
        }

        public FontAwesomeIcon Icon { get; }
        public string Tooltip { get; }
        public Vector4 Color { get; }
        public bool IsActive { get; }
        public Action OnClick { get; }
        public IReadOnlyList<RadialTooltipLine>? TooltipLines { get; }
    }

    private readonly struct PropagationVisualState
    {
        public PropagationVisualState(bool isEnabled, Vector4 accentColor, IReadOnlyList<RadialTooltipLine>? tooltipLines)
        {
            IsEnabled = isEnabled;
            AccentColor = accentColor;
            TooltipLines = tooltipLines;
        }

        public bool IsEnabled { get; }
        public Vector4 AccentColor { get; }
        public IReadOnlyList<RadialTooltipLine>? TooltipLines { get; }
    }

    private readonly struct RadialTooltipInfo
    {
        public RadialTooltipInfo(Vector2 mousePosition, string title, IReadOnlyList<RadialTooltipLine>? lines = null)
        {
            MousePosition = mousePosition;
            Title = title;
            Lines = lines;
        }

        public Vector2 MousePosition { get; }
        public string Title { get; }
        public IReadOnlyList<RadialTooltipLine>? Lines { get; }
    }

    private readonly struct RadialTooltipLine
    {
        public RadialTooltipLine(string text, Vector4 color)
        {
            Text = text;
            Color = color;
        }

        public string Text { get; }
        public Vector4 Color { get; }
    }
}
