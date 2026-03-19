using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary> linear gizmo rendering for <see cref="GizmoRenderer"/>.</summary>
public sealed partial class GizmoRenderer
{
    private void DrawLinearGizmo(
        string boneName,
        ModelBone bone,
        Vector3 localPosition,
        Vector3 worldPosition,
        Vector2 screenPos,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        BoneTransform? templateTransform,
        LinearMode mode,
        Vector3? cameraViewDirection,
        bool useWorldSpace,
        GizmoRenderOptions options)
    {
        var attribute = mode == LinearMode.Translation
            ? BoneAttribute.Position
            : BoneAttribute.Scale;
        var drawList = GetGizmoDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var mousePos = ImGui.GetIO().MousePos;

        var applyWorldSpace = useWorldSpace && mode == LinearMode.Translation;
        var useBoneLocal = mode == LinearMode.Translation && !useWorldSpace;
        var faceCamera = mode == LinearMode.Translation || mode == LinearMode.Scale;
        var axisCount = 0;
        for (var i = 0; i < AxisDirections.Length; i++)
        {
            var axis = (GizmoAxis)(i + 1);
            if (TryBuildAxisVisual(rootTransform, localPosition, worldPosition, screenPos, mousePos, boneRotation, axis, cameraViewDirection, applyWorldSpace, useBoneLocal, faceCamera, out var state))
                _axisVisualStates[axisCount++] = state;
        }

        if (axisCount == 0)
            return;

        var hasBounds = TryGetInteractionBounds(axisCount, screenPos, scale, out var boundsMin, out var boundsMax);
        var pointerInRegion = hasBounds && IsMouseWithinRect(boundsMin, boundsMax);

        var centerRectHalf = Vector2.Zero;
        var centerRadius = ResolveCenterClickRadius(scale);
        var centerHovered = false;
        if (options.DrawCenter)
        {
            if (mode == LinearMode.Scale)
            {
                centerRectHalf = new Vector2((ScaleHandleSize + 2f) * scale * 0.5f);
                var centerHitRectHalf = centerRectHalf + new Vector2(CenterClickHitPadding * scale);
                centerHovered = IsMouseWithinRect(screenPos - centerHitRectHalf, screenPos + centerHitRectHalf);
            }
            else
            {
                centerHovered = IsMouseWithinCircle(screenPos, centerRadius);
            }
        }

        var wheelOpen = IsOptionWheelOpen();
        var centerInteractionOpen = _centerBoneSelectorActive;

        var centerInteractionRadius = centerRadius;
        if (mode == LinearMode.Scale)
            centerInteractionRadius = MathF.Max(centerInteractionRadius, centerRectHalf.Length() + (CenterClickHitPadding * scale));

        var hoverThreshold = AxisHoverDistance * scale;
        var fallbackThreshold = AxisFallbackDistance * scale;
        GizmoAxis hoveredAxis = GizmoAxis.None;
        AxisVisualState hoveredState = default;
        var bestDistance = hoverThreshold;
        var bestFallbackDistance = float.MaxValue;
        AxisVisualState fallbackState = default;
        var hasFallback = false;
        var canConsiderHover = !_dragState.IsDragging && !centerInteractionOpen && !centerHovered;

        if (canConsiderHover)
        {
            for (var i = 0; i < axisCount; i++)
            {
                var state = _axisVisualStates[i];
                var distance = state.MinDistance;
                if (distance <= bestDistance)
                {
                    hoveredAxis = state.Axis;
                    hoveredState = state;
                    bestDistance = distance;
                }

                if (!hasFallback || distance < bestFallbackDistance)
                {
                    fallbackState = state;
                    bestFallbackDistance = distance;
                    hasFallback = true;
                }
            }
        }

        var fallbackAxis = GizmoAxis.None;
        if (canConsiderHover && hoveredAxis == GizmoAxis.None && hasFallback && bestFallbackDistance <= fallbackThreshold)
            fallbackAxis = fallbackState.Axis;
        var centerHoverActive = centerHovered;

        if (canConsiderHover && (hoveredAxis != GizmoAxis.None || fallbackAxis != GizmoAxis.None || centerHoverActive))
            ImGui.SetNextFrameWantCaptureMouse(true);
        else if (centerHoverActive)
            ImGui.SetNextFrameWantCaptureMouse(true);

        var translationDragActive = IsDragActiveFor(boneName, BoneAttribute.Position);
        var isFocused = _dragState.IsDragging
                        || wheelOpen
                        || centerInteractionOpen
                        || centerHoverActive
                        || hoveredAxis != GizmoAxis.None
                        || fallbackAxis != GizmoAxis.None;
        using var alphaScope = PushGizmoAlpha(isFocused);

        var centerGlowRadius = CenterPointRadius * CenterGlowRadiusMultiplier * scale;
        if (options.DrawCenter && mode != LinearMode.Scale)
        {
            var centerGlowColor = ImGui.GetColorU32(GizmoColors.CenterGlow);
            drawList.AddCircleFilled(screenPos, centerGlowRadius, centerGlowColor, 64);
        }

        if (translationDragActive)
        {
            for (var i = 0; i < axisCount; i++)
            {
                var suppressedState = _axisVisualStates[i];
                if (suppressedState.Axis == _dragState.ActiveAxis)
                    continue;
                DrawSuppressedTranslationAxis(drawList, in suppressedState, scale);
            }
        }

        for (var i = 0; i < axisCount; i++)
        {
            var state = _axisVisualStates[i];
            if (translationDragActive && state.Axis != _dragState.ActiveAxis)
                continue;
            var isActive = _dragState.IsDragging && state.Axis == _dragState.ActiveAxis;
            var isHovered = !_dragState.IsDragging && (state.Axis == hoveredAxis || (hoveredAxis == GizmoAxis.None && fallbackAxis == state.Axis));
            if (translationDragActive)
                isHovered = false;

            var axisColorVector = translationDragActive && isActive
                ? GizmoColors.TranslationDragActive
                : GetAxisColorVector(state.Axis, isActive, isHovered);
            var axisColor = ImGui.GetColorU32(axisColorVector);
            var glowColor = translationDragActive && isActive
                ? ImGui.GetColorU32(GizmoColors.TranslationDragGlow(axisColorVector))
                : GetAxisGlowColor(state.Axis, isActive);
            var trimmedEnd = GetTrimmedEndpoint(state, scale, mode);
            var glowEnd = trimmedEnd;

            drawList.AddLine(state.ScreenStart, glowEnd, glowColor, AxisLineThickness * scale * AxisGlowThicknessMultiplier);
            drawList.AddLine(state.ScreenStart, trimmedEnd, axisColor, AxisLineThickness * scale);

            if (mode == LinearMode.Scale)
                DrawScaleHandle(drawList, state.ScreenEnd, scale, axisColorVector, isActive, isHovered);
            else
                DrawAxisArrowhead(drawList, in state, scale, axisColorVector);

            DrawAxisLabel(drawList, in state, scale, axisColorVector, AxisLabel(state.Axis), isActive, isHovered);
        }

        if (mode == LinearMode.Scale)
        {
            if (options.DrawCenter)
            {
                var fillColor = ImGui.GetColorU32(GizmoColors.ScaleCenterFill);
                var borderColor = ImGui.GetColorU32(GizmoColors.ScaleCenterBorder);
                var highlightActive = centerHoverActive || ImGui.GetTime() < _centerHighlightUntil;
                if (highlightActive)
                {
                    var highlightColor = ImGui.GetColorU32(GizmoColors.ScaleCenterHighlight);
                    drawList.AddRect(screenPos - centerRectHalf, screenPos + centerRectHalf, highlightColor, 3.5f, ImDrawFlags.None, 2f * scale);
                }

                drawList.AddRectFilled(screenPos - centerRectHalf, screenPos + centerRectHalf, fillColor, 3.5f);
                drawList.AddRect(screenPos - centerRectHalf, screenPos + centerRectHalf, borderColor, 3.5f);
            }
        }
        else if (options.DrawCenter)
        {
            var highlightActive = centerHoverActive || ImGui.GetTime() < _centerHighlightUntil;
            DrawCircularCenterHandle(drawList, screenPos, scale, centerRadius, highlightActive, centerGlowRadius);
        }

        DrawBoneLabelIfEnabled(options, attribute, boneName, drawList, screenPos, scale);

        var centerInteractionActive = HandleCenterInteraction(
            boneName,
            bone,
            scale,
            options.AllowCenterInteraction,
            centerHoverActive,
            screenPos,
            centerInteractionRadius);

        var startAxis = hoveredAxis != GizmoAxis.None ? hoveredAxis : fallbackAxis;
        var startState = hoveredAxis != GizmoAxis.None ? hoveredState : fallbackState;
        var canStartAxisDrag = !_dragState.IsDragging && !centerInteractionActive && startAxis != GizmoAxis.None;

        if (centerHoverActive)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        else if (canStartAxisDrag)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (canStartAxisDrag && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            BeginDrag(startAxis, startState, boneName, bone, boneRotation, rootTransform, templateTransform, attribute, screenPos, localPosition, worldPosition);
        }

        if (HandleDragLifecycleForBone(boneName, screenPos))
        {
            if (translationDragActive)
                DrawTranslationDragPath(screenPos, worldPosition, scale, rootTransform);
        }

        HandleRadialInputIfEnabled(
            options.HandleRadialInput,
            pointerInRegion,
            centerHoverActive,
            hoveredAxis != GizmoAxis.None || fallbackAxis != GizmoAxis.None);
    }

    private bool TryBuildAxisVisual(
        in ActorRootTransform rootTransform,
        Vector3 localPosition,
        Vector3 worldPosition,
        Vector2 screenPos,
        Vector2 mousePos,
        Quaternion boneRotation,
        GizmoAxis axis,
        Vector3? cameraViewDirection,
        bool useWorldSpace,
        bool useBoneLocal,
        bool faceCamera,
        out AxisVisualState state)
    {
        if (AxisToIndex(axis) < 0)
        {
            state = default;
            return false;
        }

        var direction = ResolveAxisDrawDirection(axis, rootTransform, boneRotation, useWorldSpace, useBoneLocal, faceCamera, cameraViewDirection);
        if (direction.LengthSquared() <= float.Epsilon)
        {
            state = default;
            return false;
        }

        var minScreenLength = AxisMinScreenLength * ImGuiHelpers.GlobalScale;
        var targetLength = AxisBaseLength;
        AxisVisualState snapshot = default;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Vector3 axisWorldEnd;
            if (useWorldSpace)
            {
                axisWorldEnd = worldPosition + (direction * targetLength);
            }
            else
            {
                var axisLocalDirection = ResolveAxisLocalDirection(direction, boneRotation, useBoneLocal);
                var axisLocalEnd = localPosition + (axisLocalDirection * targetLength);
                axisWorldEnd = TransformPointToWorld(rootTransform, axisLocalEnd);
            }

            if (!_gameGui.WorldToScreen(axisWorldEnd, out var axisScreenEnd))
            {
                state = default;
                return false;
            }

            var axisScreenVector = axisScreenEnd - screenPos;
            var axisScreenLength = axisScreenVector.Length();
            if (axisScreenLength <= float.Epsilon)
            {
                targetLength *= 1.5f;
                continue;
            }

            if (axisScreenLength > AxisMaxScreenLength)
            {
                var multiplier = AxisMaxScreenLength / axisScreenLength;
                targetLength *= multiplier;
                continue;
            }

            var axisWorldVector = axisWorldEnd - worldPosition;
            var axisWorldLength = axisWorldVector.Length();
            if (axisWorldLength <= float.Epsilon)
            {
                state = default;
                return false;
            }

            var screenDirection = axisScreenVector / axisScreenLength;
            var worldDirection = axisWorldVector / axisWorldLength;
            var alignment = cameraViewDirection.HasValue
                ? Math.Abs(Vector3.Dot(worldDirection, cameraViewDirection.Value))
                : 0f;
            var allowScaling = alignment < AxisFacingCameraDisableScalingThreshold;
            var lineDistance = DistanceToSegment(mousePos, screenPos, axisScreenEnd);
            var handleDistance = (mousePos - axisScreenEnd).Length();

            snapshot = new AxisVisualState
            {
                Axis = axis,
                ScreenStart = screenPos,
                ScreenEnd = axisScreenEnd,
                WorldDirection = worldDirection,
                WorldLength = axisWorldLength,
                ScreenDirection = screenDirection,
                ScreenLength = axisScreenLength,
                LineDistance = lineDistance,
                HandleDistance = handleDistance,
            };

            if (!allowScaling)
            {
                state = snapshot;
                return true;
            }

            if (axisScreenLength < minScreenLength)
            {
                var multiplier = minScreenLength / axisScreenLength;
                targetLength *= multiplier;
                continue;
            }

            state = snapshot;
            return true;
        }

        state = snapshot;
        return snapshot.ScreenLength > 0f && snapshot.WorldLength > 0f;
    }

    private bool TryGetInteractionBounds(int axisCount, Vector2 screenPos, float scale, out Vector2 min, out Vector2 max)
    {
        min = screenPos;
        max = screenPos;

        if (axisCount <= 0)
            return false;

        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        for (var i = 0; i < axisCount; i++)
        {
            var state = _axisVisualStates[i];
            min = Vector2.Min(min, Vector2.Min(state.ScreenStart, state.ScreenEnd));
            max = Vector2.Max(max, Vector2.Max(state.ScreenStart, state.ScreenEnd));
        }

        min = Vector2.Min(min, screenPos);
        max = Vector2.Max(max, screenPos);

        var padding = new Vector2(35f * scale);
        min -= padding;
        max += padding;
        return true;
    }

    private static void DrawSuppressedTranslationAxis(ImDrawListPtr drawList, in AxisVisualState state, float scale)
    {
        var color = ImGui.GetColorU32(GizmoColors.TranslationDragSuppressed);
        drawList.AddLine(state.ScreenStart, state.ScreenEnd, color, AxisLineThickness * scale * 0.75f);
    }

    private static void DrawAxisArrowhead(ImDrawListPtr drawList, in AxisVisualState state, float scale, Vector4 axisColor)
    {
        var direction = state.ScreenDirection;
        if (direction.LengthSquared() <= float.Epsilon)
            return;

        direction = Vector2.Normalize(direction);
        var normal = new Vector2(-direction.Y, direction.X);
        var arrowLength = AxisArrowLength * scale;
        var arrowWidth = AxisArrowWidth * scale;
        var tip = state.ScreenEnd;
        var basePoint = tip - (direction * arrowLength);
        var left = basePoint + (normal * arrowWidth);
        var right = basePoint - (normal * arrowWidth);
        var fillColor = ImGui.GetColorU32(axisColor);
        var borderColor = ImGui.GetColorU32(GizmoColors.AxisArrowBorder(axisColor));
        drawList.AddTriangleFilled(tip, left, right, fillColor);
        drawList.AddTriangle(tip, left, right, borderColor, 1.1f * scale);
    }

    private static void DrawScaleHandle(ImDrawListPtr drawList, Vector2 position, float scale, Vector4 axisColor, bool isActive, bool isHovered)
    {
        var half = new Vector2(ScaleHandleSize * scale * 0.5f);
        var highlight = isActive || isHovered;
        var fillColor = ImGui.GetColorU32(GizmoColors.ScaleHandleFill(axisColor, highlight));
        var borderColor = ImGui.GetColorU32(GizmoColors.ScaleHandleBorder(axisColor));
        drawList.AddRectFilled(position - half, position + half, fillColor, 2.5f * scale);
        drawList.AddRect(position - half, position + half, borderColor, 2.5f * scale);
    }

    private static void DrawAxisLabel(ImDrawListPtr drawList, in AxisVisualState state, float scale, Vector4 axisColor, string label, bool isActive, bool isHovered)
    {
        if (string.IsNullOrEmpty(label))
            return;

        var direction = state.ScreenDirection;
        if (direction.LengthSquared() <= float.Epsilon)
            direction = Vector2.UnitX;
        else
            direction = Vector2.Normalize(direction);

        var center = state.ScreenEnd + (direction * AxisLabelDistance * scale);
        var textSize = ImGui.CalcTextSize(label);
        var padding = new Vector2(AxisLabelPadding * scale);
        var rectMin = center - (textSize * 0.5f) - padding;
        var rectMax = center + (textSize * 0.5f) + padding;
        var highlighted = isActive || isHovered;
        var backgroundColor = ImGui.GetColorU32(GizmoColors.AxisLabelBackground(axisColor, highlighted));
        var outlineColor = ImGui.GetColorU32(GizmoColors.AxisLabelOutline(axisColor));
        var textColor = GizmoColors.AxisLabelText(highlighted);

        drawList.AddRectFilled(rectMin, rectMax, backgroundColor, AxisLabelRoundness * scale);
        drawList.AddRect(rectMin, rectMax, outlineColor, AxisLabelRoundness * scale, ImDrawFlags.None, 1.05f * scale);
        drawList.AddText(center - (textSize * 0.5f), ImGui.GetColorU32(textColor), label);
    }

    private static Vector2 GetTrimmedEndpoint(in AxisVisualState state, float scale, LinearMode mode)
    {
        if (mode != LinearMode.Translation)
            return state.ScreenEnd;

        var direction = state.ScreenDirection;
        if (direction.LengthSquared() <= float.Epsilon)
            return state.ScreenEnd;

        var normalized = Vector2.Normalize(direction);
        var desiredTrim = AxisArrowLength * scale;
        var maxTrim = MathF.Max(0f, state.ScreenLength - 1f);
        var trimAmount = MathF.Min(desiredTrim, maxTrim);
        return trimAmount <= 0f ? state.ScreenEnd : state.ScreenEnd - (normalized * trimAmount);
    }

    private enum LinearMode
    {
        Translation,
        Scale,
    }

    private struct AxisVisualState
    {
        public GizmoAxis Axis;
        public Vector2 ScreenStart;
        public Vector2 ScreenEnd;
        public Vector3 WorldDirection;
        public float WorldLength;
        public Vector2 ScreenDirection;
        public float ScreenLength;
        public float LineDistance;
        public float HandleDistance;
        public float MinDistance => MathF.Min(LineDistance, HandleDistance);
    }
}
