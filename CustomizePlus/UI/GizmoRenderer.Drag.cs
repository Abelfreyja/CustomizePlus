using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Templates;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary> drag handling for <see cref="GizmoRenderer"/>.</summary>
public sealed partial class GizmoRenderer
{
    private void BeginDrag(
        GizmoAxis axis,
        AxisVisualState visual,
        string boneName,
        ModelBone bone,
        Quaternion boneRotation,
        in ActorRootTransform rootTransform,
        BoneTransform? templateTransform,
        BoneAttribute attribute,
        Vector2 screenCenter,
        Vector3 localPosition,
        Vector3 worldPosition)
    {
        if (attribute != BoneAttribute.Rotation)
            _dragState.ClearRotationAnchor();

        if (attribute == BoneAttribute.Position)
            _dragState.SetTranslationStart(screenCenter, worldPosition, localPosition);
        else
            _dragState.ClearTranslationStart();

        InitializeDragState(
            axis,
            boneName,
            bone,
            templateTransform,
            rootTransform,
            boneRotation,
            attribute,
            visual.ScreenDirection,
            visual.ScreenLength,
            visual.WorldDirection,
            visual.WorldLength);
    }

    private void BeginRotationDrag(
        GizmoAxis axis,
        Vector2 tangentDirection,
        Vector3 worldAxis,
        string boneName,
        BoneTransform? templateTransform,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        ModelBone bone,
        Vector2? dragStartScreenPoint = null,
        float? dragStartAngle = null)
    {
        if (axis == GizmoAxis.None)
        {
            _dragState.ClearRotationAnchor();
            _dragState.ClearTranslationStart();
            return;
        }

        if (tangentDirection.LengthSquared() <= 0.0001f)
            tangentDirection = Vector2.UnitX;
        tangentDirection = Vector2.Normalize(tangentDirection);

        var localAxis = ConvertWorldAxisToBoneAxis(worldAxis, rootTransform, boneRotation, adjustForRotation: true);
        if (localAxis.LengthSquared() <= float.Epsilon)
            localAxis = RotationAxisFallbackVector(axis);
        var anchorPoint = dragStartScreenPoint ?? ImGui.GetIO().MousePos;
        _dragState.SetRotationAnchor(anchorPoint, dragStartAngle);
        var initialDistance = Vector2.Dot(ImGui.GetIO().MousePos - anchorPoint, tangentDirection);
        _dragState.SetRotationDragDistance(initialDistance);
        _dragState.ClearTranslationStart();

        InitializeDragState(
            axis,
            boneName,
            bone,
            templateTransform,
            rootTransform,
            boneRotation,
            BoneAttribute.Rotation,
            tangentDirection,
            1f,
            localAxis,
            1f);
    }

    private void InitializeDragState(
        GizmoAxis axis,
        string boneName,
        ModelBone bone,
        BoneTransform? templateTransform,
        in ActorRootTransform rootTransform,
        Quaternion boneRotation,
        BoneAttribute attribute,
        Vector2 screenDirection,
        float screenLength,
        Vector3 worldDirection,
        float worldLength)
    {
        _dragState.Begin(
            axis,
            boneName,
            attribute,
            ImGui.GetIO().MousePos,
            rootTransform,
            boneRotation,
            screenDirection,
            screenLength,
            worldDirection,
            worldLength,
            templateTransform);
        SetMirrorTwinState(bone);
        _gizmoService.NotifyEditStateChange(TemplateEditorService.EditState.Start);
    }

    private void SetMirrorTwinState(ModelBone bone)
    {
        var twin = bone.TwinBone;
        _dragState.SetMirrorTwin(twin?.BoneName, twin != null && BoneData.IsIVCSCompatibleBone(bone.BoneName));
    }

    private void UpdateDrag(string boneName)
    {
        if (!_dragState.IsDragging || _dragState.ActiveAxis == GizmoAxis.None || _dragState.OriginalTransformSnapshot == null)
            return;

        var mouseDelta = ImGui.GetIO().MousePos - _dragState.StartMouse;

        switch (_dragState.ActiveAttribute)
        {
            case BoneAttribute.Position:
                UpdateTranslationDrag(boneName, mouseDelta);
                break;
            case BoneAttribute.Rotation:
                UpdateRotationDrag(boneName);
                break;
            case BoneAttribute.Scale:
                UpdateScaleDrag(boneName, mouseDelta);
                break;
        }
    }

    private void UpdateTranslationDrag(string boneName, Vector2 mouseDelta)
    {
        if (_dragState.AxisScreenLength <= float.Epsilon || _dragState.AxisWorldLength <= float.Epsilon)
            return;

        var axisPixels = Vector2.Dot(mouseDelta, _dragState.AxisScreenDirection);
        axisPixels *= GetDragSpeedMultiplier();
        var pixelsToWorld = _dragState.AxisWorldLength / _dragState.AxisScreenLength;
        var worldDelta = _dragState.AxisWorldDirection * (axisPixels * pixelsToWorld);
        var translationDelta = ConvertWorldDeltaToBoneTranslationDelta(worldDelta, _dragState.ActorTransform, _dragState.BoneRotation);
        var newTranslation = _dragState.StartTranslation + translationDelta;
        newTranslation = ApplyPrecisionSnap(newTranslation);
        newTranslation = ClampToEditorRange(BoneAttribute.Position, newTranslation);

        if (Vector3.DistanceSquared(newTranslation, _dragState.LastAppliedTranslation) < 0.000001f)
            return;

        var updated = new BoneTransform(_dragState.OriginalTransformSnapshot)
        {
            Translation = newTranslation
        };

        if (!TryApplyUpdatedTransform(boneName, updated))
            return;

        _dragState.MarkTranslationApplied(updated.Translation);
    }

    private void UpdateRotationDrag(string boneName)
    {
        if (!TryResolveRotationDragDeltaRadians(out var radiansDelta))
            return;

        var rawDegreesDelta = radiansDelta * (180f / MathF.PI);
        var rotationStepDegrees = ResolveRotationStepDegrees();
        var steppedDegreesDelta = MathF.Round(rawDegreesDelta / rotationStepDegrees) * rotationStepDegrees;
        steppedDegreesDelta = WrapSignedDegrees180(steppedDegreesDelta);
        if (Math.Abs(steppedDegreesDelta) <= 0.0001f)
            return;

        var steppedRadiansDelta = steppedDegreesDelta * (MathF.PI / 180f);

        var axisVector = _dragState.AxisWorldDirection;
        if (axisVector.LengthSquared() <= float.Epsilon)
            axisVector = RotationAxisFallbackVector(_dragState.ActiveAxis);
        if (axisVector == Vector3.Zero)
            return;

        var rotationDelta = axisVector * steppedDegreesDelta;
        var newRotation = _dragState.StartRotation + rotationDelta;
        newRotation = ApplyPrecisionSnap(newRotation);
        newRotation = ClampToEditorRange(BoneAttribute.Rotation, newRotation);

        var updated = new BoneTransform(_dragState.OriginalTransformSnapshot)
        {
            Rotation = newRotation
        };

        if (Vector3.DistanceSquared(updated.Rotation, _dragState.LastAppliedRotation) < 0.0001f)
            return;

        _dragState.SetRotationDeltaRadians(steppedRadiansDelta);

        if (!TryApplyUpdatedTransform(boneName, updated))
            return;

        _dragState.MarkRotationApplied(updated.Rotation);
    }

    private bool TryResolveRotationDragDeltaRadians(out float radiansDelta)
    {
        if (_dragState.RotationDragStartAngle.HasValue)
        {
            if (TryResolveRotationRingDeltaRadians(out radiansDelta))
                return true;

            radiansDelta = 0f;
            return false;
        }

        if (_dragState.ActiveAxis == GizmoAxis.None)
        {
            radiansDelta = 0f;
            return false;
        }

        if (!_dragState.RotationDragStartScreenPoint.HasValue)
            _dragState.SetRotationAnchorScreenPoint(_dragState.StartMouse);

        var tangent = _dragState.AxisScreenDirection;
        if (tangent.LengthSquared() <= 0.0001f)
            tangent = Vector2.UnitX;
        else
            tangent = Vector2.Normalize(tangent);

        var anchor = _dragState.RotationDragStartScreenPoint ?? _dragState.StartMouse;
        var newDistance = Vector2.Dot(ImGui.GetIO().MousePos - anchor, tangent);
        var dragDelta = newDistance - _dragState.RotationDragDistance;
        _dragState.SetRotationDragDistance(newDistance);

        var stepRadians = dragDelta * RotationRadiansPerPixel * GetDragSpeedMultiplier();
        stepRadians = RemapRotationDeltaForAxis(_dragState.ActiveAxis, stepRadians);

        _dragState.SetRotationLastAngle(null);
        radiansDelta = _dragState.RotationDragDeltaRadians + stepRadians;
        return true;
    }

    private bool TryResolveRotationRingDeltaRadians(out float radiansDelta)
    {
        radiansDelta = 0f;
        if (!_dragState.HasRotationProjection || _dragState.ActiveAxis == GizmoAxis.None)
            return false;

        var referenceAngle = _dragState.RotationDragLastAngle ?? _dragState.RotationDragStartAngle;
        if (!TryFindClosestRotationRingAngle(
                _dragState.ActiveAxis,
                _dragState.RotationProjectionTransformMatrix,
                _dragState.RotationProjectionViewMatrix,
                _dragState.RotationProjectionCenter,
                _dragState.RotationProjectionRadius,
                ImGui.GetIO().MousePos,
                referenceAngle,
                out var currentAngle))
        {
            return false;
        }

        var priorAngle = _dragState.RotationDragLastAngle ?? _dragState.RotationDragStartAngle;
        if (!priorAngle.HasValue)
        {
            _dragState.SetRotationLastAngle(currentAngle);
            return false;
        }

        var angleStep = SignedAngleDelta(priorAngle.Value, currentAngle);
        var maxAngleStep = MathF.PI / 6f;
        if (Math.Abs(angleStep) > maxAngleStep)
            angleStep = MathF.Sign(angleStep) * maxAngleStep;

        var pixelStep = angleStep * _dragState.RotationProjectionRadius;
        var step = pixelStep * RotationRadiansPerPixel * GetDragSpeedMultiplier();
        step = RemapRotationDeltaForAxis(_dragState.ActiveAxis, step);

        _dragState.SetRotationLastAngle(currentAngle);
        radiansDelta = _dragState.RotationDragDeltaRadians + step;
        return true;
    }

    private void UpdateScaleDrag(string boneName, Vector2 mouseDelta)
    {
        if (_dragState.AxisScreenLength <= float.Epsilon)
            return;

        var axisPixels = Vector2.Dot(mouseDelta, _dragState.AxisScreenDirection);
        axisPixels *= GetDragSpeedMultiplier();
        var scaleDelta = axisPixels * ScaleUnitsPerPixel;
        if (Math.Abs(scaleDelta) <= 0.00001f)
            return;

        var axisVector = _dragState.AxisWorldDirection;
        if (axisVector.LengthSquared() > float.Epsilon)
            axisVector = ConvertWorldAxisToBoneAxis(axisVector, _dragState.ActorTransform, _dragState.BoneRotation, adjustForRotation: false);

        if (axisVector.LengthSquared() <= float.Epsilon)
            axisVector = AxisUnitVector(_dragState.ActiveAxis);
        if (axisVector == Vector3.Zero)
            return;

        var newScale = _dragState.StartScale + (axisVector * scaleDelta);
        newScale = ApplyPrecisionSnap(newScale);
        newScale = ClampToEditorRange(BoneAttribute.Scale, newScale);

        if (Vector3.DistanceSquared(newScale, _dragState.LastAppliedScale) < 0.000001f)
            return;

        var updated = new BoneTransform(_dragState.OriginalTransformSnapshot)
        {
            Scaling = newScale
        };

        if (!TryApplyUpdatedTransform(boneName, updated))
            return;

        _dragState.MarkScaleApplied(updated.Scaling);
    }

    private void ApplyMirrorUpdate(BoneTransform updatedTransform)
    {
        if (!_gizmoService.MirrorMode || string.IsNullOrEmpty(_dragState.MirrorTwinBoneName))
            return;

        var mirrored = _dragState.MirrorTwinIsSpecial
            ? updatedTransform.GetSpecialReflection()
            : updatedTransform.GetStandardReflection();

        _editorManager.ModifyBoneTransform(_dragState.MirrorTwinBoneName!, mirrored);
    }

    private bool TryApplyUpdatedTransform(string boneName, BoneTransform updated)
    {
        if (!_editorManager.ModifyBoneTransform(boneName, updated))
            return false;

        ApplyMirrorUpdate(updated);
        return true;
    }

    private void CancelDrag(bool commit = false)
    {
        var wasDragging = _dragState.IsDragging;
        var appliedChange = _dragState.AppliedChange;
        _dragState.Reset(_gizmoService.ActiveAttribute);

        if (wasDragging)
        {
            if (commit && appliedChange)
                _gizmoService.NotifyEditStateChange(TemplateEditorService.EditState.Commit);
            else
                _gizmoService.NotifyEditStateChange(TemplateEditorService.EditState.Cancel);
        }
    }

    private bool HandleDragLifecycleForBone(string boneName, Vector2 screenPos)
    {
        if (_dragState.IsDragging)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                CancelDrag(true);
            }
            else if (_dragState.IsDraggingBone(boneName))
            {
                UpdateDrag(boneName);
            }
        }

        if (!_dragState.IsDraggingBone(boneName))
            return false;

        ImGui.SetNextFrameWantCaptureMouse(true);
        DrawDragMetrics(screenPos);
        return true;
    }

    private void DrawDragMetrics(Vector2 referencePosition)
    {
        var text = BuildDragMetricText();
        if (string.IsNullOrEmpty(text))
            return;

        var drawList = GetGizmoDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(6f * scale, 4f * scale);
        var textSize = ImGui.CalcTextSize(text);
        var iconRowHeight = GetModifierIndicatorRowHeight(_dragState.ActiveAttribute, scale);
        var boxSize = textSize + (padding * 2f) + new Vector2(0f, iconRowHeight);
        var position = ResolveDragMetricsPosition(referencePosition, boxSize, scale);
        var rectMin = position;
        var rectMax = position + boxSize;
        var backgroundColor = ImGui.GetColorU32(GizmoColors.DragMetricsBackground);
        drawList.AddRectFilled(rectMin, rectMax, backgroundColor, 4f * scale);
        var textPos = rectMin + padding;
        drawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), text);
        DrawModifierIndicatorRow(drawList, textPos, textSize, scale);
    }

    private void DrawTranslationDragPath(Vector2 currentScreenPos, Vector3 currentWorldPos, float scale, in ActorRootTransform rootTransform)
    {
        Vector2? startPos = null;
        Vector3? startWorld = null;
        if (_dragState.TranslationDragStartLocalPos.HasValue)
        {
            startWorld = TransformPointToWorld(rootTransform, _dragState.TranslationDragStartLocalPos.Value);
            _dragState.SetTranslationWorldStart(startWorld);
        }
        else if (_dragState.TranslationDragStartWorldPos.HasValue)
        {
            startWorld = _dragState.TranslationDragStartWorldPos.Value;
        }

        if (startWorld.HasValue)
        {
            var translationDelta = _dragState.LastAppliedTranslation - _dragState.StartTranslation;
            if (translationDelta.LengthSquared() > float.Epsilon)
            {
                var worldDelta = ConvertBoneTranslationDeltaToWorld(translationDelta, rootTransform, _dragState.BoneRotation);
                startWorld = currentWorldPos - worldDelta;
            }

            if (_gameGui.WorldToScreen(startWorld.Value, out var screen))
            {
                startPos = screen;
                _dragState.SetTranslationScreenStart(screen);
            }
        }

        if (!startPos.HasValue && _dragState.TranslationDragStartScreenPos.HasValue)
            startPos = _dragState.TranslationDragStartScreenPos.Value;

        if (!startPos.HasValue)
            return;

        var drawList = GetGizmoDrawList();
        var color = ImGui.GetColorU32(GizmoColors.TranslationDragPath);
        drawList.AddLine(startPos.Value, currentScreenPos, color, TranslationDragPathThickness * scale);
        drawList.AddCircleFilled(startPos.Value, TranslationDragStartMarkerRadius * scale, color, 32);
    }

    private string? BuildDragMetricText()
    {
        if (!_dragState.IsDragging || _dragState.ActiveAxis == GizmoAxis.None || _dragState.OriginalTransformSnapshot == null)
            return null;

        var axisIndex = GetMetricComponentIndex(_dragState.ActiveAttribute, _dragState.ActiveAxis);
        if (axisIndex < 0)
            return null;

        var axisLabel = AxisLabel(_dragState.ActiveAxis);

        switch (_dragState.ActiveAttribute)
        {
            case BoneAttribute.Position:
            {
                var current = GetAxisComponent(_dragState.LastAppliedTranslation, axisIndex);
                var start = GetAxisComponent(_dragState.StartTranslation, axisIndex);
                return BuildLinearMetricText(axisLabel, current, start);
            }
            case BoneAttribute.Rotation:
            {
                var current = GetAxisComponent(_dragState.LastAppliedRotation, axisIndex);
                var start = GetAxisComponent(_dragState.StartRotation, axisIndex);
                var delta = WrapSignedDegrees180(current - start);
                return $"{axisLabel} {current:0.0}° (Δ {delta:+0.0;-0.0;0.0}°)";
            }
            case BoneAttribute.Scale:
            {
                var current = GetAxisComponent(_dragState.LastAppliedScale, axisIndex);
                var start = GetAxisComponent(_dragState.StartScale, axisIndex);
                return BuildLinearMetricText(axisLabel, current, start);
            }
            default:
                return null;
        }
    }

    private static float GetAxisComponent(in Vector3 vector, int axisIndex)
        => axisIndex switch
        {
            0 => vector.X,
            1 => vector.Y,
            2 => vector.Z,
            _ => 0f,
        };

    private static string BuildLinearMetricText(string axisLabel, float current, float start)
    {
        var delta = current - start;
        return $"{axisLabel} {current:0.000} (Δ {delta:+0.000;-0.000;0.000})";
    }

    private static int GetMetricComponentIndex(BoneAttribute attribute, GizmoAxis axis)
    {
        var axisIndex = AxisToIndex(axis);
        if (axisIndex < 0)
            return axisIndex;

        return attribute switch
        {
            BoneAttribute.Rotation => RotationMetricComponentIndex(axis),
            _ => axisIndex,
        };
    }
}
