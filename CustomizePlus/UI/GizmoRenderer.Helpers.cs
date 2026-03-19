using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary> helpers for <see cref="GizmoRenderer"/>.</summary>
public sealed partial class GizmoRenderer
{
    private const int CharacterScaleFactor1Offset = 0x2A0;
    private const int CharacterScaleFactor2Offset = 0x2A4;

    private static Quaternion NormalizeOrIdentity(Quaternion rotation)
        => rotation.LengthSquared() <= float.Epsilon ? Quaternion.Identity : Quaternion.Normalize(rotation);

    private static Vector3 TransformPointToWorld(in ActorRootTransform rootTransform, Vector3 localPosition)
    {
        var scaled = new Vector3(
            localPosition.X * rootTransform.Scale.X,
            localPosition.Y * rootTransform.Scale.Y,
            localPosition.Z * rootTransform.Scale.Z);

        var rotated = Vector3.Transform(scaled, rootTransform.Rotation);
        return rootTransform.Position + rotated;
    }

    private static Vector3 TransformWorldVectorToLocal(in ActorRootTransform rootTransform, Vector3 worldVector)
    {
        if (worldVector.LengthSquared() <= float.Epsilon)
            return Vector3.Zero;

        var inverseRotation = Quaternion.Inverse(rootTransform.Rotation);
        var rotated = Vector3.Transform(worldVector, inverseRotation);
        return SafeDivide(rotated, rootTransform.Scale);
    }

    private static Vector3 TransformWorldVectorToBoneSpace(in ActorRootTransform rootTransform, Quaternion boneRotation, Vector3 worldVector)
    {
        var modelVector = TransformWorldVectorToLocal(rootTransform, worldVector);
        if (modelVector.LengthSquared() <= float.Epsilon)
            return Vector3.Zero;

        var inverseBoneRotation = Quaternion.Inverse(boneRotation);
        return Vector3.Transform(modelVector, inverseBoneRotation);
    }

    private static Vector3 ConvertWorldDeltaToBoneTranslationDelta(Vector3 worldDelta, in ActorRootTransform rootTransform, Quaternion boneRotation)
        => TransformWorldVectorToBoneSpace(rootTransform, boneRotation, worldDelta);

    private static Vector3 ConvertWorldAxisToBoneAxis(
        Vector3 worldAxis,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        bool adjustForRotation = true)
    {
        var boneAxis = TransformWorldVectorToBoneSpace(rootTransform, boneRotation, worldAxis);
        if (boneAxis.LengthSquared() <= float.Epsilon)
            return Vector3.Zero;

        var normalized = Vector3.Normalize(boneAxis);
        if (!adjustForRotation)
            return normalized;

        var adjusted = new Vector3(normalized.Y, normalized.X, normalized.Z);
        return adjusted.LengthSquared() <= float.Epsilon ? Vector3.Zero : Vector3.Normalize(adjusted);
    }

    private static Vector3 ConvertBoneTranslationDeltaToWorld(Vector3 boneDelta, in ActorRootTransform rootTransform, Quaternion boneRotation)
    {
        if (boneDelta.LengthSquared() <= float.Epsilon)
            return Vector3.Zero;

        var rotated = Vector3.Transform(boneDelta, boneRotation);
        return TransformDirectionToWorld(rootTransform, rotated, applyRootScale: true);
    }

    private static Vector3 ResolveAxisLocalDirection(Vector3 axisDirection, Quaternion boneRotation, bool useBoneLocal)
        => useBoneLocal ? Vector3.Transform(axisDirection, boneRotation) : axisDirection;

    private static Vector3 TransformDirectionToWorld(in ActorRootTransform rootTransform, Vector3 direction, bool applyRootScale)
    {
        var transformed = applyRootScale
            ? new Vector3(
                direction.X * rootTransform.Scale.X,
                direction.Y * rootTransform.Scale.Y,
                direction.Z * rootTransform.Scale.Z)
            : direction;

        return Vector3.Transform(transformed, rootTransform.Rotation);
    }

    private static Vector3 ResolveAxisWorldDirection(
        GizmoAxis axis,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        bool useWorldSpace,
        bool useBoneLocal,
        bool applyRootScale)
    {
        var axisDirection = AxisUnitVector(axis);
        if (axisDirection.LengthSquared() <= float.Epsilon)
            return Vector3.Zero;

        if (useWorldSpace)
            return axisDirection;

        var localDirection = ResolveAxisLocalDirection(axisDirection, boneRotation, useBoneLocal);
        var worldDirection = TransformDirectionToWorld(rootTransform, localDirection, applyRootScale);
        return worldDirection.LengthSquared() <= float.Epsilon ? axisDirection : Vector3.Normalize(worldDirection);
    }

    private static Vector3 ResolveAxisDrawDirection(
        GizmoAxis axis,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        bool useWorldSpace,
        bool useBoneLocal,
        bool faceCamera,
        Vector3? cameraViewDirection)
    {
        var axisDirection = AxisUnitVector(axis);
        if (axisDirection.LengthSquared() <= float.Epsilon)
            return Vector3.Zero;

        if (!faceCamera || !cameraViewDirection.HasValue)
            return axisDirection;

        var worldDirection = ResolveAxisWorldDirection(
            axis,
            rootTransform,
            boneRotation,
            useWorldSpace,
            useBoneLocal,
            applyRootScale: true);

        if (worldDirection.LengthSquared() > float.Epsilon && Vector3.Dot(worldDirection, cameraViewDirection.Value) > 0f)
            axisDirection = -axisDirection;

        return axisDirection;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSq = segment.LengthSquared();
        if (lengthSq <= float.Epsilon)
            return (point - start).Length();

        var t = Vector2.Dot(point - start, segment) / lengthSq;
        t = Math.Clamp(t, 0f, 1f);
        var projection = start + (segment * t);
        return (point - projection).Length();
    }

    private static float NormalizeAngle(float angle)
    {
        var normalized = angle % TwoPi;
        if (normalized < 0f)
            normalized += TwoPi;
        return normalized;
    }

    private static float SignedAngleDelta(float fromAngle, float toAngle)
    {
        var delta = NormalizeAngle(toAngle - fromAngle);
        if (delta > MathF.PI)
            delta -= TwoPi;
        return delta;
    }

    private static bool TryFindClosestRotationRingAngle(
        GizmoAxis axis,
        Matrix4x4 transformMatrix,
        Matrix4x4 viewMatrix,
        Vector2 center,
        float radius,
        Vector2 mousePos,
        float? referenceAngle,
        out float angle)
    {
        if (referenceAngle.HasValue)
        {
            ReadOnlySpan<float> angleWindows = stackalloc float[]
            {
                MathF.PI / 8f,
                MathF.PI / 4f,
                MathF.PI / 2f,
                MathF.PI,
                0f,
            };
            foreach (var angleWindow in angleWindows)
            {
                if (TryFindClosestRotationRingAngle(axis, transformMatrix, viewMatrix, center, radius, mousePos, referenceAngle, angleWindow, out angle))
                    return true;
            }
        }

        return TryFindClosestRotationRingAngle(axis, transformMatrix, viewMatrix, center, radius, mousePos, referenceAngle: null, angleWindow: 0f, out angle);
    }

    private static bool TryFindClosestRotationRingAngle(
        GizmoAxis axis,
        Matrix4x4 transformMatrix,
        Matrix4x4 viewMatrix,
        Vector2 center,
        float radius,
        Vector2 mousePos,
        float? referenceAngle,
        float angleWindow,
        out float angle)
    {
        angle = 0f;
        var found = false;
        var bestScore = float.MaxValue;
        var continuityWeight = radius * radius * 0.2f;
        if (continuityWeight < 1f)
            continuityWeight = 1f;

        for (var i = 0; i < RotationRingSegments; i++)
        {
            var a0 = (TwoPi * i) / RotationRingSegments;
            var a1 = (TwoPi * (i + 1)) / RotationRingSegments;
            var mid = NormalizeAngle((a0 + a1) * 0.5f);
            if (referenceAngle.HasValue && angleWindow > 0f)
            {
                if (Math.Abs(SignedAngleDelta(referenceAngle.Value, mid)) > angleWindow)
                    continue;
            }

            var p0Base = BuildRotationAxisPoint(axis, radius, a0);
            var p1Base = BuildRotationAxisPoint(axis, radius, a1);

            var p0Transformed = Vector3.Transform(Vector3.Transform(p0Base, transformMatrix), viewMatrix);
            var p1Transformed = Vector3.Transform(Vector3.Transform(p1Base, transformMatrix), viewMatrix);

            var p0 = center + new Vector2(p0Transformed.X, p0Transformed.Y);
            var p1 = center + new Vector2(p1Transformed.X, p1Transformed.Y);
            var segment = p1 - p0;
            var lengthSq = segment.LengthSquared();
            if (lengthSq <= float.Epsilon)
                continue;

            var t = Vector2.Dot(mousePos - p0, segment) / lengthSq;
            t = Math.Clamp(t, 0f, 1f);
            var projected = p0 + (segment * t);
            var distanceSq = (mousePos - projected).LengthSquared();
            var candidateAngle = NormalizeAngle(a0 + ((a1 - a0) * t));
            var score = distanceSq;
            if (referenceAngle.HasValue)
            {
                var delta = SignedAngleDelta(referenceAngle.Value, candidateAngle);
                score += delta * delta * continuityWeight;
            }

            if (score >= bestScore)
                continue;

            bestScore = score;
            angle = candidateAngle;
            found = true;
        }

        return found;
    }

    private static bool IsPointInRingSegment(Vector2 point, Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle)
    {
        var diff = point - center;
        var distanceSquared = diff.LengthSquared();
        var innerSq = innerRadius * innerRadius;
        var outerSq = outerRadius * outerRadius;
        if (distanceSquared < innerSq || distanceSquared > outerSq)
            return false;

        var angle = MathF.Atan2(diff.Y, diff.X);
        var normalizedAngle = NormalizeAngle(angle);
        var start = NormalizeAngle(startAngle);
        var end = NormalizeAngle(endAngle);
        if (start <= end)
            return normalizedAngle >= start && normalizedAngle <= end;

        return normalizedAngle >= start || normalizedAngle <= end;
    }

    private static Vector3 BuildRotationAxisPoint(GizmoAxis axis, float radius, float angle)
        => axis switch
        {
            GizmoAxis.X => new Vector3(0f, radius * MathF.Cos(angle), radius * MathF.Sin(angle)),
            GizmoAxis.Y => new Vector3(radius * MathF.Cos(angle), 0f, radius * MathF.Sin(angle)),
            GizmoAxis.Z => new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), 0f),
            _ => Vector3.Zero,
        };

    private static Vector2 ProjectRotationAxisPoint(
        Vector2 center,
        float radius,
        GizmoAxis axis,
        float angle,
        Matrix4x4 transformMatrix,
        Matrix4x4 viewMatrix)
    {
        var basePoint = BuildRotationAxisPoint(axis, radius, angle);
        var transformed = Vector3.Transform(basePoint, transformMatrix);
        transformed = Vector3.Transform(transformed, viewMatrix);
        return center + new Vector2(transformed.X, transformed.Y);
    }

    private static bool IsMouseWithinRect(Vector2 min, Vector2 max)
    {
        var mouse = ImGui.GetIO().MousePos;
        return mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
    }

    private static bool IsMouseWithinCircle(Vector2 center, float radius)
    {
        var mouse = ImGui.GetIO().MousePos;
        return Vector2.Distance(mouse, center) <= radius;
    }

    private static float ResolveCenterClickRadius(float scale)
        => MathF.Max(CenterPointRadius * scale * CenterClickRadiusMultiplier, CenterClickMinRadius * scale);

    private static void DrawCircularCenterHandle(
        ImDrawListPtr drawList,
        Vector2 screenPos,
        float scale,
        float centerRadius,
        bool highlightActive,
        float outerRingRadius = 0f)
    {
        if (highlightActive)
        {
            var highlightColor = ImGui.GetColorU32(GizmoColors.CenterHandleHighlight);
            drawList.AddCircle(screenPos, centerRadius, highlightColor, 48, 2f * scale);
        }

        var centerColor = ImGui.GetColorU32(GizmoColors.CenterHandleFill);
        drawList.AddCircleFilled(screenPos, CenterPointRadius * scale, centerColor);

        if (outerRingRadius > 0f)
        {
            var ringColor = ImGui.GetColorU32(GizmoColors.CenterHandleOuterRing);
            drawList.AddCircle(screenPos, outerRingRadius, ringColor, 48, 1.6f * scale);
        }
    }

    private ImDrawListPtr GetGizmoDrawList()
        => ImGui.GetForegroundDrawList();

    private static string BuildBoneLabelText(string baseLabel, BoneAttribute? labelAttribute)
    {
        if (!labelAttribute.HasValue)
            return baseLabel;

        var attributeLabel = labelAttribute.Value.GetShortLabel();
        return string.IsNullOrEmpty(attributeLabel) ? baseLabel : $"{baseLabel} [{attributeLabel}]";
    }

    private static void DrawBoneLabel(ImDrawListPtr drawList, string text, Vector2 screenPos, float scale)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var textSize = ImGui.CalcTextSize(text);
        var textPos = screenPos + new Vector2((CenterPointRadius * scale) + (6f * scale), -(textSize.Y * 0.5f));
        drawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), text);
    }

    private static void DrawBoneLabelIfEnabled(
        GizmoRenderOptions options,
        BoneAttribute attribute,
        string boneName,
        ImDrawListPtr drawList,
        Vector2 screenPos,
        float scale)
    {
        if (!options.DrawLabel)
            return;

        var labelAttribute = options.LabelAttribute ?? attribute;
        var displayName = BoneData.GetBoneDisplayName(boneName);
        DrawBoneLabel(drawList, BuildBoneLabelText(displayName, labelAttribute), screenPos, scale);
    }

    private static string AxisLabel(GizmoAxis axis)
        => axis switch
        {
            GizmoAxis.X => "X",
            GizmoAxis.Y => "Y",
            GizmoAxis.Z => "Z",
            _ => string.Empty,
        };

    private static uint GetAxisColor(GizmoAxis axis, bool isActive, bool isHovered)
        => ImGui.GetColorU32(GetAxisColorVector(axis, isActive, isHovered));

    private static Vector4 GetAxisColorVector(GizmoAxis axis, bool isActive, bool isHovered)
        => GizmoColors.AxisLine(axis, isActive, isHovered);

    private static uint GetAxisGlowColor(GizmoAxis axis, bool isActive)
        => GizmoColors.AxisGlowU32(axis, isActive);

    private static uint GetAxisBackgroundColor(GizmoAxis axis, bool isActive)
        => GizmoColors.AxisBackgroundU32(axis, isActive);

    private static int AxisToIndex(GizmoAxis axis)
        => axis switch
        {
            GizmoAxis.X => 0,
            GizmoAxis.Y => 1,
            GizmoAxis.Z => 2,
            _ => -1,
        };

    private static Vector3 AxisUnitVector(GizmoAxis axis)
        => axis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

    private static Vector3 RotationAxisFallbackVector(GizmoAxis axis)
        => axis switch
        {
            GizmoAxis.X => Vector3.UnitY,
            GizmoAxis.Y => Vector3.UnitX,
            _ => AxisUnitVector(axis),
        };

    private static float RemapRotationDeltaForAxis(GizmoAxis axis, float delta)
        => axis == GizmoAxis.Y ? -delta : delta;

    private static int RotationMetricComponentIndex(GizmoAxis axis)
    {
        var axisIndex = AxisToIndex(axis);
        if (axisIndex < 0)
            return axisIndex;

        return axis switch
        {
            GizmoAxis.X => AxisToIndex(GizmoAxis.Y),
            GizmoAxis.Y => AxisToIndex(GizmoAxis.X),
            _ => axisIndex,
        };
    }

    private static Vector3 SafeDivide(Vector3 numerator, Vector3 denominator)
    {
        float Safe(float value, float div)
            => Math.Abs(div) <= float.Epsilon ? 0f : value / div;

        return new Vector3(
            Safe(numerator.X, denominator.X),
            Safe(numerator.Y, denominator.Y),
            Safe(numerator.Z, denominator.Z));
    }

    private static Vector3 ClampToEditorRange(BoneAttribute attribute, Vector3 value)
    {
        if (attribute == BoneAttribute.Rotation)
            return WrapRotationToEditorRange(value);

        var (min, max) = GetEditorRange(attribute);
        return new Vector3(
            Math.Clamp(value.X, min, max),
            Math.Clamp(value.Y, min, max),
            Math.Clamp(value.Z, min, max));
    }

    private static Vector3 WrapRotationToEditorRange(Vector3 value)
        => new(
            WrapSignedDegrees180(value.X),
            WrapSignedDegrees180(value.Y),
            WrapSignedDegrees180(value.Z));

    private static float WrapSignedDegrees180(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;

        value %= 360f;
        if (value > 180f)
            value -= 360f;
        else if (value < -180f)
            value += 360f;

        return value;
    }

    private static (float Min, float Max) GetEditorRange(BoneAttribute attribute)
        => attribute == BoneAttribute.Rotation
            ? (-360f, 360f)
            : (-10f, 10f);

    private static bool SupportsModifierIndicators(BoneAttribute attribute)
        => attribute is BoneAttribute.Position or BoneAttribute.Rotation or BoneAttribute.Scale;

    private static bool HasActiveModifierIndicators()
        => IsSlowDragModifierActive() || IsPrecisionSnapModifierActive();

    private static float GetModifierIndicatorFontSize()
        => ImGui.GetFontSize() * 0.72f;

    private static float GetModifierIndicatorGap(float scale)
        => 3f * scale;

    private float GetModifierIndicatorRowHeight(BoneAttribute attribute, float scale)
    {
        if (!SupportsModifierIndicators(attribute) || !HasActiveModifierIndicators())
            return 0f;

        return GetModifierIndicatorFontSize() + GetModifierIndicatorGap(scale);
    }

    private void DrawModifierIndicatorRow(ImDrawListPtr drawList, Vector2 textPos, Vector2 textSize, float scale)
    {
        if (!SupportsModifierIndicators(_dragState.ActiveAttribute) || !HasActiveModifierIndicators())
            return;

        var iconY = textPos.Y + textSize.Y + GetModifierIndicatorGap(scale);
        var iconPos = new Vector2(textPos.X, iconY);
        var iconSize = GetModifierIndicatorFontSize();
        var iconSpacing = iconSize + (4f * scale);

        if (IsSlowDragModifierActive())
        {
            DrawModifierIndicatorIcon(drawList, iconPos, iconSize, FontAwesomeIcon.SyncAlt.ToIconString());
            iconPos.X += iconSpacing;
        }

        if (IsPrecisionSnapModifierActive())
            DrawModifierIndicatorIcon(drawList, iconPos, iconSize, FontAwesomeIcon.Crosshairs.ToIconString());
    }

    private static void DrawModifierIndicatorIcon(ImDrawListPtr drawList, Vector2 position, float fontSize, string icon)
    {
        var color = ImGui.GetColorU32(GizmoColors.ModifierIndicator);
        var iconFont = UiBuilder.IconFont;
        drawList.AddText(iconFont, fontSize, position, color, icon);
    }

    private bool IsDragActiveFor(string boneName, BoneAttribute attribute)
        => _dragState.IsDragging
           && _dragState.ActiveAttribute == attribute
           && string.Equals(_dragState.ActiveBone, boneName, StringComparison.Ordinal);

    private static Vector2 ResolveDragMetricsPosition(Vector2 referencePosition, Vector2 boxSize, float scale)
    {
        var io = ImGui.GetIO();
        var margin = 8f * scale;
        var horizontalOffset = 18f * scale;
        var verticalOffset = 32f * scale;
        var rightPos = referencePosition + new Vector2(horizontalOffset, verticalOffset);
        var leftPos = referencePosition + new Vector2(-(boxSize.X + horizontalOffset), verticalOffset);

        var rightMin = rightPos;
        var rightMax = rightPos + boxSize;
        var mousePadding = 10f * scale;
        var rightOverlapsMouse = io.MousePos.X >= rightMin.X - mousePadding
                                 && io.MousePos.X <= rightMax.X + mousePadding
                                 && io.MousePos.Y >= rightMin.Y - mousePadding
                                 && io.MousePos.Y <= rightMax.Y + mousePadding;

        var useLeft = rightOverlapsMouse || rightMax.X > io.DisplaySize.X - margin;
        var position = useLeft ? leftPos : rightPos;

        if (position.X < margin)
            position.X = margin;

        var maxX = io.DisplaySize.X - boxSize.X - margin;
        if (position.X > maxX)
            position.X = Math.Max(margin, maxX);

        if (position.Y < margin)
            position.Y = margin;

        var maxY = io.DisplaySize.Y - boxSize.Y - margin;
        if (position.Y > maxY)
            position.Y = Math.Max(margin, maxY);

        return position;
    }

    private static bool IsSlowDragModifierActive()
        => ImGui.GetIO().KeyShift;

    private static bool IsPrecisionSnapModifierActive()
        => ImGui.GetIO().KeyCtrl;

    private float GetDragSpeedMultiplier()
        => IsSlowDragModifierActive() ? SlowDragMultiplier : 1f;

    private float ResolveRotationStepDegrees()
    {
        if (!IsPrecisionSnapModifierActive())
            return RotationStepDegrees;

        var step = ResolvePrecisionSnapStep();
        return step <= float.Epsilon ? RotationStepDegrees : step;
    }

    private float ResolvePrecisionSnapStep()
    {
        var precision = Math.Clamp(_configuration.EditorConfiguration.EditorValuesPrecision, 0, 6);
        return MathF.Pow(10f, -precision);
    }

    private Vector3 ApplyPrecisionSnap(Vector3 value)
    {
        if (!IsPrecisionSnapModifierActive())
            return value;

        return SnapVectorToStep(value, ResolvePrecisionSnapStep());
    }

    private static Vector3 SnapVectorToStep(Vector3 value, float step)
        => new(
            SnapValueToStep(value.X, step),
            SnapValueToStep(value.Y, step),
            SnapValueToStep(value.Z, step));

    private static float SnapValueToStep(float value, float step)
    {
        if (step <= float.Epsilon)
            return value;

        return MathF.Round(value / step) * step;
    }

    private float ResolveGizmoAlpha(bool isFocused)
    {
        if (_dragState.IsDragging)
            return GizmoActiveAlpha;

        if (_gizmoService.IsBoneEditorSliderActive)
            return GizmoSliderAlpha;

        return isFocused ? GizmoActiveAlpha : GizmoIdleAlpha;
    }

    private StyleAlphaScope PushGizmoAlpha(bool isFocused)
    {
        var targetAlpha = ResolveGizmoAlpha(isFocused);
        var adjusted = ClampAlpha(ImGui.GetStyle().Alpha * targetAlpha);
        return new StyleAlphaScope(adjusted);
    }

    private static float ClampAlpha(float value)
    {
        if (value < 0f)
            return 0f;
        if (value > 1f)
            return 1f;
        return value;
    }

    private bool TryBeginCenterInteraction(bool centerInteractionActive, bool allowCenterInteraction, bool centerHovered, Vector2 screenPos, float centerInteractionRadius, string boneName)
    {
        if (IsOptionWheelOpen())
            return false;

        var centerActivatedFromHotspot = allowCenterInteraction
                                         && !_dragState.IsDragging
                                         && IsCenterHotspotClicked(screenPos, centerInteractionRadius);
        var centerActivated = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if ((allowCenterInteraction && centerHovered && centerActivated) || centerActivatedFromHotspot)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            _wheelSuppressNextToggle = true;

            if (centerInteractionActive)
            {
                _centerBoneSelectorActive = false;
                _centerBoneSelectorBoneName = null;
                return false;
            }

            TriggerCenterHighlight();
            _centerBoneSelectorAnchor = screenPos;
            _centerBoneSelectorActive = true;
            _centerBoneSelectorBoneName = boneName;
            return true;
        }

        return centerInteractionActive || (allowCenterInteraction && centerHovered && !_dragState.IsDragging);
    }

    private bool BeginCenterInteractionIfNeeded(
        bool centerInteractionActive,
        bool allowCenterInteraction,
        bool centerHovered,
        Vector2 screenPos,
        float centerInteractionRadius,
        string boneName)
    {
        if (_dragState.IsDragging)
            return centerInteractionActive;

        return TryBeginCenterInteraction(centerInteractionActive, allowCenterInteraction, centerHovered, screenPos, centerInteractionRadius, boneName);
    }

    private bool HandleCenterInteraction(
        string boneName,
        ModelBone bone,
        float scale,
        bool allowCenterInteraction,
        bool centerHovered,
        Vector2 screenPos,
        float centerInteractionRadius)
    {
        var centerInteractionActive = BeginCenterInteractionIfNeeded(
            _centerBoneSelectorActive,
            allowCenterInteraction,
            centerHovered,
            screenPos,
            centerInteractionRadius,
            boneName);

        if (centerInteractionActive)
            centerInteractionActive = DrawCenterBoneSelector(boneName, bone, scale);

        return centerInteractionActive;
    }

    private void HandleRadialInputIfEnabled(bool enabled, bool pointerInRegion, bool centerHoverActive, bool axisHoverActive)
    {
        if (!enabled)
            return;

        HandleOptionWheelInput(pointerInRegion || centerHoverActive || axisHoverActive || _dragState.IsDragging);
    }

    private bool IsOptionWheelOpen()
        => ImGui.IsPopupOpen(OptionWheelPopupId);

    private static bool IsCenterHotspotClicked(Vector2 center, float radius)
    {
        var diameter = MathF.Max(radius * 2f, 1f);
        var hotspotSize = new Vector2(diameter, diameter);
        var hotspotPos = center - (hotspotSize * 0.5f);
        var flags = ImGuiWindowFlags.NoDecoration
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoBackground
                    | ImGuiWindowFlags.NoNav
                    | ImGuiWindowFlags.NoBringToFrontOnFocus
                    | ImGuiWindowFlags.NoFocusOnAppearing;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(hotspotPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(hotspotSize, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var clicked = false;
        if (ImGui.Begin("##TemplateEditorGizmoCenterHotspot", flags))
        {
            ImGui.InvisibleButton("##TemplateEditorGizmoCenterHotspotButton", hotspotSize);
            clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        }

        ImGui.End();
        ImGui.PopStyleVar();
        return clicked;
    }

    private bool DrawCenterBoneSelector(string boneName, ModelBone bone, float scale)
    {
        if (!_centerBoneSelectorActive)
            return false;

        if (!string.Equals(_centerBoneSelectorBoneName, boneName, StringComparison.Ordinal))
        {
            _centerBoneSelectorActive = false;
            _centerBoneSelectorBoneName = null;
            return false;
        }

        var parentBones = BoneRelationHelper.EnumerateAncestors(bone, boneName);
        var childBones = BoneRelationHelper.EnumerateDescendants(bone, boneName);
        var entries = BuildCenterBoneSelectorEntries(parentBones, childBones);
        var drawList = GetGizmoDrawList();
        var mousePos = ImGui.GetIO().MousePos;
        var radius = ResolveCenterBoneSelectorRadius(entries, parentBones.Count, childBones.Count, scale);
        radius = ClampCenterBoneSelectorRadiusToViewport(entries, _centerBoneSelectorAnchor, radius, scale);
        var deadZoneRadius = CenterBoneSelectorDeadZoneRadius * scale;
        var hoveredIndex = ResolveCenterBoneSelectorHover(entries, _centerBoneSelectorAnchor, radius, mousePos, deadZoneRadius);

        DrawCenterBoneSelectorEntries(drawList, entries, _centerBoneSelectorAnchor, radius, hoveredIndex, scale);
        DrawCenterBoneSelectorCenter(drawList, _centerBoneSelectorAnchor, deadZoneRadius, hoveredIndex >= 0, scale);

        ImGui.SetNextFrameWantCaptureMouse(true);
        if (hoveredIndex >= 0)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            _pendingRadialTooltip = new RadialTooltipInfo(mousePos, entries[hoveredIndex].TooltipTitle);
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (hoveredIndex >= 0)
            {
                _gizmoService.SetSelectedBone(entries[hoveredIndex].BoneName);
                TriggerCenterHighlight();
            }

            _centerBoneSelectorActive = false;
            _centerBoneSelectorBoneName = null;
            return false;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _centerBoneSelectorActive = false;
            _centerBoneSelectorBoneName = null;
            return false;
        }

        return true;
    }

    private static List<CenterBoneSelectorEntry> BuildCenterBoneSelectorEntries(IReadOnlyList<string> parentBones, IReadOnlyList<string> childBones)
    {
        var entries = new List<CenterBoneSelectorEntry>(parentBones.Count + childBones.Count);
        AddCenterBoneSelectorEntries(entries, parentBones, isParent: true);
        AddCenterBoneSelectorEntries(entries, childBones, isParent: false);
        return entries;
    }

    private static void AddCenterBoneSelectorEntries(List<CenterBoneSelectorEntry> entries, IReadOnlyList<string> bones, bool isParent)
    {
        for (var i = 0; i < bones.Count; i++)
        {
            var boneName = bones[i];
            var displayName = BoneData.GetBoneDisplayName(boneName);
            entries.Add(new CenterBoneSelectorEntry(
                boneName,
                displayName,
                isParent,
                $"{displayName} ({boneName})"));
        }
    }

    private static float ResolveCenterBoneSelectorRadius(IReadOnlyList<CenterBoneSelectorEntry> entries, int parentCount, int childCount, float scale)
    {
        var maxButtonWidth = 0f;
        for (var i = 0; i < entries.Count; i++)
        {
            var size = ResolveCenterBoneSelectorButtonSize(entries[i].Label, scale);
            if (size.X > maxButtonWidth)
                maxButtonWidth = size.X;
        }

        var parentRadius = ResolveCenterBoneSelectorGroupRadius(parentCount, maxButtonWidth, scale);
        var childRadius = ResolveCenterBoneSelectorGroupRadius(childCount, maxButtonWidth, scale);
        return Math.Max(parentRadius, childRadius);
    }

    private static float ResolveCenterBoneSelectorGroupRadius(int count, float buttonWidth, float scale)
    {
        var minRadius = CenterBoneSelectorBaseRadius * scale;
        if (count <= 1)
            return minRadius;

        var spacing = buttonWidth + (CenterBoneSelectorButtonGap * scale);
        var requiredRadius = (spacing * (count - 1)) / CenterBoneSelectorArcSpan;
        return Math.Max(minRadius, requiredRadius);
    }

    private static float ClampCenterBoneSelectorRadiusToViewport(
        IReadOnlyList<CenterBoneSelectorEntry> entries,
        Vector2 anchor,
        float radius,
        float scale)
    {
        if (entries.Count == 0)
            return radius;

        var io = ImGui.GetIO();
        var margin = 12f * scale;
        var maxRadius = float.MaxValue;
        for (var i = 0; i < entries.Count; i++)
        {
            var direction = ResolveCenterBoneSelectorDirection(entries, i);
            var halfSize = ResolveCenterBoneSelectorButtonSize(entries[i].Label, scale) * 0.5f;

            maxRadius = MathF.Min(maxRadius, ResolveCenterBoneSelectorAxisRadiusLimit(direction.X, anchor.X, halfSize.X, margin, io.DisplaySize.X));
            maxRadius = MathF.Min(maxRadius, ResolveCenterBoneSelectorAxisRadiusLimit(direction.Y, anchor.Y, halfSize.Y, margin, io.DisplaySize.Y));
        }

        if (maxRadius == float.MaxValue)
            return radius;

        var minRadius = (CenterBoneSelectorDeadZoneRadius + 12f) * scale;
        return Math.Clamp(radius, minRadius, Math.Max(minRadius, maxRadius));
    }

    private static float ResolveCenterBoneSelectorAxisRadiusLimit(float directionComponent, float anchorComponent, float halfSize, float margin, float displayLimit)
    {
        if (Math.Abs(directionComponent) <= 0.0001f)
            return float.MaxValue;

        if (directionComponent > 0f)
            return (displayLimit - margin - halfSize - anchorComponent) / directionComponent;

        return (margin + halfSize - anchorComponent) / directionComponent;
    }

    private static int ResolveCenterBoneSelectorHover(
        IReadOnlyList<CenterBoneSelectorEntry> entries,
        Vector2 anchor,
        float radius,
        Vector2 mousePos,
        float deadZoneRadius)
    {
        if (Vector2.DistanceSquared(mousePos, anchor) <= deadZoneRadius * deadZoneRadius)
            return -1;

        var bestIndex = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < entries.Count; i++)
        {
            var entryCenter = ResolveCenterBoneSelectorEntryCenter(entries, i, anchor, radius);
            var distance = Vector2.DistanceSquared(mousePos, entryCenter);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static void DrawCenterBoneSelectorEntries(
        ImDrawListPtr drawList,
        IReadOnlyList<CenterBoneSelectorEntry> entries,
        Vector2 anchor,
        float radius,
        int hoveredIndex,
        float scale)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var center = ResolveCenterBoneSelectorEntryCenter(entries, i, anchor, radius);
            var buttonSize = ResolveCenterBoneSelectorButtonSize(entry.Label, scale);
            var rectMin = center - (buttonSize * 0.5f);
            var rectMax = center + (buttonSize * 0.5f);
            var isHovered = i == hoveredIndex;

            var connectorColor = GizmoColors.CenterSelectorConnector(entry.IsParent, isHovered);
            drawList.AddLine(anchor, center, ImGui.GetColorU32(connectorColor), CenterBoneSelectorConnectorThickness * scale);

            var backgroundColor = GizmoColors.CenterSelectorBackground(entry.IsParent, isHovered);
            var borderColor = GizmoColors.CenterSelectorBorder(entry.IsParent, isHovered);
            var textColor = GizmoColors.CenterSelectorText(entry.IsParent);

            drawList.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(backgroundColor), 6f * scale);
            drawList.AddRect(rectMin, rectMax, ImGui.GetColorU32(borderColor), 6f * scale, ImDrawFlags.None, (isHovered ? CenterBoneSelectorHighlightThickness : 1.2f) * scale);

            var textSize = ImGui.CalcTextSize(entry.Label);
            var textPos = center - (textSize * 0.5f);
            drawList.AddText(textPos, ImGui.GetColorU32(textColor), entry.Label);
        }
    }

    private static void DrawCenterBoneSelectorCenter(ImDrawListPtr drawList, Vector2 anchor, float deadZoneRadius, bool hasHoverSelection, float scale)
    {
        var ringColor = hasHoverSelection
            ? GizmoColors.CenterSelectorDeadZoneHovered
            : GizmoColors.CenterSelectorDeadZoneIdle;
        drawList.AddCircle(anchor, deadZoneRadius, ImGui.GetColorU32(ringColor), 64, 2f * scale);
        drawList.AddCircleFilled(anchor, CenterPointRadius * scale, ImGui.GetColorU32(GizmoColors.CenterSelectorCenterFill), 32);
    }

    private static Vector2 ResolveCenterBoneSelectorButtonSize(string label, float scale)
    {
        var textSize = ImGui.CalcTextSize(label);
        return textSize + new Vector2(CenterBoneSelectorButtonHorizontalPadding * scale * 2f, CenterBoneSelectorButtonVerticalPadding * scale * 2f);
    }

    private static Vector2 ResolveCenterBoneSelectorEntryCenter(IReadOnlyList<CenterBoneSelectorEntry> entries, int index, Vector2 anchor, float radius)
        => anchor + (ResolveCenterBoneSelectorDirection(entries, index) * radius);

    private static Vector2 ResolveCenterBoneSelectorDirection(IReadOnlyList<CenterBoneSelectorEntry> entries, int index)
    {
        var angle = ResolveCenterBoneSelectorEntryAngle(entries, index);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static float ResolveCenterBoneSelectorEntryAngle(IReadOnlyList<CenterBoneSelectorEntry> entries, int index)
    {
        var entry = entries[index];
        var groupIndex = 0;
        var groupCount = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].IsParent != entry.IsParent)
                continue;

            if (i == index)
                groupIndex = groupCount;

            groupCount++;
        }

        var centerAngle = entry.IsParent ? (-MathF.PI / 2f) : (MathF.PI / 2f);
        return ResolveCenterBoneSelectorAngle(groupIndex, groupCount, centerAngle);
    }

    private static float ResolveCenterBoneSelectorAngle(int index, int count, float centerAngle)
    {
        if (count <= 1)
            return centerAngle;

        var startAngle = centerAngle - (CenterBoneSelectorArcSpan * 0.5f);
        var step = CenterBoneSelectorArcSpan / (count - 1);
        return startAngle + (step * index);
    }

    private readonly struct CenterBoneSelectorEntry
    {
        public CenterBoneSelectorEntry(string boneName, string label, bool isParent, string tooltipTitle)
        {
            BoneName = boneName;
            Label = label;
            IsParent = isParent;
            TooltipTitle = tooltipTitle;
        }

        public string BoneName { get; }
        public string Label { get; }
        public bool IsParent { get; }
        public string TooltipTitle { get; }
    }

    private struct GizmoRenderOptions
    {
        public bool DrawCenter;
        public bool DrawLabel;
        public bool HandleRadialInput;
        public bool AllowCenterInteraction;
        public BoneAttribute? LabelAttribute;

        public static GizmoRenderOptions Default(BoneAttribute attribute)
            => new()
            {
                DrawCenter = true,
                DrawLabel = true,
                HandleRadialInput = true,
                AllowCenterInteraction = true,
                LabelAttribute = attribute,
            };
    }

    private unsafe bool TryGetActorRootTransform(CharacterBase* cBase, out ActorRootTransform transform)
    {
        transform = default;
        if (cBase == null)
            return false;

        var drawObject = cBase->DrawObject.Object;

        var position = new Vector3(drawObject.Position.X, drawObject.Position.Y, drawObject.Position.Z);
        var rotation = NormalizeOrIdentity(new Quaternion(drawObject.Rotation.X, drawObject.Rotation.Y, drawObject.Rotation.Z, drawObject.Rotation.W));

        var scale = new Vector3(
            Math.Abs(drawObject.Scale.X) <= float.Epsilon ? 1f : drawObject.Scale.X,
            Math.Abs(drawObject.Scale.Y) <= float.Epsilon ? 1f : drawObject.Scale.Y,
            Math.Abs(drawObject.Scale.Z) <= float.Epsilon ? 1f : drawObject.Scale.Z);
        scale *= GetCharacterScaleFactor(cBase);

        transform = new ActorRootTransform(position, rotation, scale);
        return true;
    }

    private unsafe float GetCharacterScaleFactor(CharacterBase* cBase)
    {
        if (cBase == null)
            return 1f;

        var basePtr = (byte*)cBase;
        var scale1 = *(float*)(basePtr + CharacterScaleFactor1Offset);
        var scale2 = *(float*)(basePtr + CharacterScaleFactor2Offset);
        var scale = scale1 * scale2;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            return 1f;

        return scale;
    }

    private readonly struct StyleAlphaScope : IDisposable
    {
        public StyleAlphaScope(float alpha)
            => ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);

        public void Dispose()
            => ImGui.PopStyleVar();
    }

    private enum GizmoAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 3,
    }

    private readonly struct ActorRootTransform
    {
        public ActorRootTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
    }
}
