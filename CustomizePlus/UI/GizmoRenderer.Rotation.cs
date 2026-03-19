using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Game.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary> rotation gizmo rendering for <see cref="GizmoRenderer"/>.</summary>
public sealed partial class GizmoRenderer
{
    private void DrawRotationGizmo(
        string boneName,
        ModelBone bone,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        Vector2 screenPos,
        BoneTransform? templateTransform,
        CameraService.CameraInfo? cameraInfo,
        bool useWorldSpace,
        GizmoRenderOptions options)
    {
        if (!cameraInfo.HasValue)
            return;
        var camera = cameraInfo.Value;

        var drawList = GetGizmoDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var mousePos = ImGui.GetIO().MousePos;

        var wheelOpen = IsOptionWheelOpen();
        var centerInteractionOpen = _centerBoneSelectorActive;

        var radius = Math.Clamp(RotationRingBaseRadius * scale, AxisMinScreenLength, AxisMaxScreenLength);
        var rotationDragActive = IsDragActiveFor(boneName, BoneAttribute.Rotation);

        var combinedRotation = NormalizeOrIdentity(Quaternion.Multiply(rootTransform.Rotation, boneRotation));
        var transformMatrix = useWorldSpace ? Matrix4x4.Identity : Matrix4x4.CreateFromQuaternion(combinedRotation);
        var viewMatrix = camera.ViewRotationMatrix;
        viewMatrix *= Matrix4x4.CreateScale(-1f, 1f, 1f);

        if (rotationDragActive && _dragState.HasRotationProjection)
        {
            screenPos = _dragState.RotationProjectionCenter;
            radius = _dragState.RotationProjectionRadius;
            transformMatrix = _dragState.RotationProjectionTransformMatrix;
            viewMatrix = _dragState.RotationProjectionViewMatrix;
        }
        else if (!_dragState.IsDragging)
        {
            _dragState.SetRotationProjection(screenPos, radius, transformMatrix, viewMatrix);
        }

        var padding = new Vector2(radius + (30f * scale));
        var pointerInRegion = IsMouseWithinRect(screenPos - padding, screenPos + padding);
        var centerRadius = ResolveCenterClickRadius(scale);
        var centerHovered = options.DrawCenter && IsMouseWithinCircle(screenPos, centerRadius);

        Span<Vector3> worldAxes = stackalloc Vector3[3];
        for (var i = 0; i < worldAxes.Length; i++)
        {
            var axisEnum = (GizmoAxis)(i + 1);
            worldAxes[i] = ResolveAxisWorldDirection(
                axisEnum,
                rootTransform,
                boneRotation,
                useWorldSpace,
                useBoneLocal: true,
                applyRootScale: false);
        }

        var hoverThreshold = RotationHoverTolerance * scale;
        var hoverState = new RotationHoverState
        {
            Axis = GizmoAxis.None,
            Distance = hoverThreshold,
            Tangent = Vector2.UnitX,
            HasPoint = false,
            Angle = 0f,
        };

        var considerHover = !_dragState.IsDragging && !centerInteractionOpen && !centerHovered;
        if (considerHover)
        {
            for (var i = 0; i < worldAxes.Length; i++)
            {
                var axisEnum = (GizmoAxis)(i + 1);
                RenderRotationAxis(
                    drawList,
                    transformMatrix,
                    viewMatrix,
                    screenPos,
                    radius,
                    RotationRingThickness * scale,
                    axisEnum,
                    mousePos,
                    ref hoverState,
                    updateHover: true,
                    drawPath: false,
                    isActive: false,
                    isHovered: false);
            }
        }

        var hoveredAxis = considerHover ? hoverState.Axis : GizmoAxis.None;
        var axisHoverActive = hoveredAxis != GizmoAxis.None && hoverState.Distance <= hoverThreshold;
        var centerHoverActive = centerHovered;

        if (considerHover && (hoveredAxis != GizmoAxis.None || centerHoverActive))
            ImGui.SetNextFrameWantCaptureMouse(true);
        else if (centerHoverActive)
            ImGui.SetNextFrameWantCaptureMouse(true);

        var isFocused = _dragState.IsDragging
                        || wheelOpen
                        || centerInteractionOpen
                        || centerHoverActive
                        || hoveredAxis != GizmoAxis.None;
        using var alphaScope = PushGizmoAlpha(isFocused);

        var backgroundColor = ImGui.GetColorU32(GizmoColors.RotationBackground);
        drawList.AddCircleFilled(screenPos, radius, backgroundColor, 96);

        for (var i = 0; i < worldAxes.Length; i++)
        {
            var axisEnum = (GizmoAxis)(i + 1);
            if (rotationDragActive && axisEnum != _dragState.ActiveAxis)
                continue;

            var isActive = _dragState.IsDragging && axisEnum == _dragState.ActiveAxis;
            var isHovered = !_dragState.IsDragging && axisEnum == hoveredAxis;
            RenderRotationAxis(
                drawList,
                transformMatrix,
                viewMatrix,
                screenPos,
                radius,
                RotationRingThickness * scale,
                axisEnum,
                mousePos,
                ref hoverState,
                updateHover: false,
                drawPath: true,
                isActive: isActive,
                isHovered: isHovered);
        }

        if (rotationDragActive)
            DrawRotationDragHighlight(drawList, transformMatrix, viewMatrix, screenPos, radius, scale);

        if (axisHoverActive && hoverState.HasPoint)
        {
            var indicatorColor = GetAxisColor(hoveredAxis, false, true);
            drawList.AddCircle(hoverState.ScreenPoint, RotationHoverIndicatorRadius * scale, indicatorColor, 48, RotationHoverIndicatorThickness * scale);
        }
        else if (_dragState.IsDragging && _dragState.ActiveAttribute == BoneAttribute.Rotation && _dragState.ActiveAxis != GizmoAxis.None)
        {
            Vector2? dragIndicator = null;
            if (_dragState.RotationDragStartAngle.HasValue)
            {
                dragIndicator = ProjectRotationAxisPoint(
                    screenPos,
                    radius,
                    _dragState.ActiveAxis,
                    _dragState.RotationDragStartAngle.Value,
                    transformMatrix,
                    viewMatrix);
            }

            if (!dragIndicator.HasValue && _dragState.RotationDragStartScreenPoint.HasValue)
                dragIndicator = _dragState.RotationDragStartScreenPoint;

            if (dragIndicator.HasValue)
            {
                var activeColor = GetAxisColor(_dragState.ActiveAxis, true, false);
                drawList.AddCircleFilled(dragIndicator.Value, RotationDragIndicatorRadius * scale, activeColor, 48);
                _dragState.SetRotationAnchorScreenPoint(dragIndicator);
            }
        }

        DrawBoneLabelIfEnabled(options, BoneAttribute.Rotation, boneName, drawList, screenPos, scale);

        if (options.DrawCenter)
        {
            var highlightActive = centerHoverActive || ImGui.GetTime() < _centerHighlightUntil;
            DrawCircularCenterHandle(drawList, screenPos, scale, centerRadius, highlightActive);
        }

        var centerInteractionActive = HandleCenterInteraction(
            boneName,
            bone,
            scale,
            options.AllowCenterInteraction,
            centerHoverActive,
            screenPos,
            centerRadius);

        var canStartAxisDrag = !centerInteractionActive && axisHoverActive;
        if (centerHoverActive)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        else if (canStartAxisDrag)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (canStartAxisDrag && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            var axisIndex = AxisToIndex(hoveredAxis);
            var worldAxis = axisIndex >= 0 ? worldAxes[axisIndex] : AxisUnitVector(hoveredAxis);
            var dragStartScreenPoint = hoverState.HasPoint ? hoverState.ScreenPoint : (Vector2?)null;
            var dragStartAngle = hoverState.HasPoint ? hoverState.Angle : (float?)null;
            BeginRotationDrag(hoveredAxis, hoverState.Tangent, worldAxis, boneName, templateTransform, rootTransform, boneRotation, bone, dragStartScreenPoint, dragStartAngle);
        }

        HandleDragLifecycleForBone(boneName, screenPos);

        HandleRadialInputIfEnabled(
            options.HandleRadialInput,
            pointerInRegion,
            centerHoverActive,
            hoveredAxis != GizmoAxis.None);
    }

    private void RenderRotationAxis(
        ImDrawListPtr drawList,
        Matrix4x4 transformMatrix,
        Matrix4x4 viewMatrix,
        Vector2 center,
        float radius,
        float thickness,
        GizmoAxis axis,
        Vector2 mousePos,
        ref RotationHoverState hoverState,
        bool updateHover,
        bool drawPath,
        bool isActive,
        bool isHovered)
    {
        const float zClip = 0f;
        Vector2? previousPos = null;

        for (var i = 0; i <= RotationRingSegments; i++)
        {
            var angle = (float)(2.0 * Math.PI * i / RotationRingSegments);
            var basePoint = BuildRotationAxisPoint(axis, radius, angle);

            var transformed = Vector3.Transform(basePoint, transformMatrix);
            transformed = Vector3.Transform(transformed, viewMatrix);
            var currentPos = center + new Vector2(transformed.X, transformed.Y);

            if (previousPos.HasValue)
            {
                var isVisible = transformed.Z < zClip;
                if (updateHover && isVisible)
                {
                    var fromPos = previousPos.Value;
                    var segment = currentPos - fromPos;
                    var segmentLengthSq = segment.LengthSquared();
                    if (segmentLengthSq <= float.Epsilon)
                        continue;

                    var t = Vector2.Dot(mousePos - fromPos, segment) / segmentLengthSq;
                    t = Math.Clamp(t, 0f, 1f);
                    var projected = fromPos + (segment * t);
                    var distance = (mousePos - projected).Length();
                    if (distance < hoverState.Distance)
                    {
                        var tangent = segment;
                        if (tangent.LengthSquared() > 0.0001f)
                            tangent = Vector2.Normalize(tangent);
                        else
                            tangent = Vector2.UnitX;

                        var prevAngle = (float)(2.0 * Math.PI * (i - 1) / RotationRingSegments);
                        var projectedAngle = prevAngle + ((angle - prevAngle) * t);
                        hoverState = new RotationHoverState
                        {
                            Axis = axis,
                            Distance = distance,
                            Tangent = tangent,
                            ScreenPoint = projected,
                            HasPoint = true,
                            Angle = NormalizeAngle(projectedAngle),
                        };
                    }
                }

                if (drawPath)
                {
                    var color = isVisible ? GetAxisColor(axis, isActive, isHovered) : GetAxisBackgroundColor(axis, isActive);
                    drawList.AddLine(previousPos.Value, currentPos, color, thickness);
                }
            }

            previousPos = currentPos;
        }
    }

    private void DrawRotationDragHighlight(
        ImDrawListPtr drawList,
        Matrix4x4 transformMatrix,
        Matrix4x4 viewMatrix,
        Vector2 center,
        float radius,
        float scale)
    {
        if (!_dragState.RotationDragStartAngle.HasValue || _dragState.ActiveAxis == GizmoAxis.None)
            return;

        var deltaRadians = _dragState.RotationDragDeltaRadians;
        deltaRadians = RemapRotationDeltaForAxis(_dragState.ActiveAxis, deltaRadians);
        if (Math.Abs(deltaRadians) <= 0.0001f)
            return;

        var steps = Math.Clamp((int)(Math.Abs(deltaRadians) / TwoPi * RotationRingSegments), 4, RotationRingSegments);
        Vector2? previous = null;
        var color = ImGui.GetColorU32(GizmoColors.RotationDragHighlight);
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var angle = _dragState.RotationDragStartAngle.Value + (deltaRadians * t);
            var projected = ProjectRotationAxisPoint(center, radius, _dragState.ActiveAxis, NormalizeAngle(angle), transformMatrix, viewMatrix);
            if (previous.HasValue)
                drawList.AddLine(previous.Value, projected, color, RotationRingThickness * scale * RotationHighlightThicknessMultiplier);
            previous = projected;
        }
    }

    private struct RotationHoverState
    {
        public GizmoAxis Axis;
        public float Distance;
        public Vector2 Tangent;
        public Vector2 ScreenPoint;
        public bool HasPoint;
        public float Angle;
    }
}
