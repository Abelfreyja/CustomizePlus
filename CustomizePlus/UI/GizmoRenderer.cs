using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using CustomizePlus.Game.Services;
using CustomizePlus.Templates;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using OtterGui.Log;
using Penumbra.GameData.Actors;
using PenumbraActor = Penumbra.GameData.Interop.Actor;
using System;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary>
/// Draws a simple overlay gizmo at the location of the currently selected bone in the template editor.
/// </summary>
public sealed partial class GizmoRenderer : IDisposable
{
    private readonly IUiBuilder _uiBuilder;
    private readonly TemplateEditorManager _editorManager;
    private readonly TemplateEditorService _gizmoService;
    private readonly PluginConfiguration _configuration;
    private readonly ArmatureManager _armatureManager;
    private readonly GameObjectService _gameObjectService;
    private readonly CameraService _cameraService;
    private readonly IGameGui _gameGui;
    private readonly Logger _logger;

    private const float AxisBaseLength = 0.08f;
    private const float AxisMinScreenLength = 30f;
    private const float AxisHoverDistance = 18f;
    private const float AxisFallbackDistance = 48f;
    private const float AxisLineThickness = 2.5f;
    private const float CenterPointRadius = 4f;
    private const float AxisMaxScreenLength = 220f;
    private const float RotationRadiansPerPixel = 1f / 150f;
    private const float RotationStepDegrees = 0.1f;
    private const float ScaleUnitsPerPixel = 0.003f;
    private const float RotationRingBaseRadius = 100f;
    private const float RotationRingThickness = 2.6f;
    private const float RotationHoverTolerance = 14f;
    private const float ScaleHandleSize = 8f;
    private const int RotationRingSegments = 96;
    private const float AxisFacingCameraDisableScalingThreshold = 0.975f;
    private const float OptionWheelBaseRadius = 76f;
    private const float OptionWheelInnerRadiusFraction = 0.55f;
    private const float TwoPi = MathF.PI * 2f;
    private const float AxisGlowThicknessMultiplier = 3f;
    private const float AxisArrowLength = 18f;
    private const float AxisArrowWidth = 6.5f;
    private const float AxisLabelDistance = 26f;
    private const float AxisLabelPadding = 4f;
    private const float AxisLabelRoundness = 3.5f;
    private const float CenterGlowRadiusMultiplier = 2.2f;
    private const float CenterGlowOpacity = 0.35f;
    private const float CenterClickRadiusMultiplier = 1.6f;
    private const float CenterClickMinRadius = 10f;
    private const float CenterClickHitPadding = 3f;
    private const float CenterHighlightDuration = 0.45f;
    private const float CenterBoneSelectorBaseRadius = 90f;
    private const float CenterBoneSelectorArcSpan = MathF.PI * 0.9f;
    private const float CenterBoneSelectorButtonGap = 12f;
    private const float CenterBoneSelectorDeadZoneRadius = 36f;
    private const float CenterBoneSelectorButtonHorizontalPadding = 10f;
    private const float CenterBoneSelectorButtonVerticalPadding = 6f;
    private const float CenterBoneSelectorConnectorThickness = 1.6f;
    private const float CenterBoneSelectorHighlightThickness = 2.2f;
    private const float RotationBackgroundAlpha = 0.31f;
    private const float RotationHoverIndicatorRadius = 10f;
    private const float RotationHoverIndicatorThickness = 2.2f;
    private const float RotationDragIndicatorRadius = 4f;
    private const float TranslationDragPathThickness = 2.6f;
    private const float TranslationDragStartMarkerRadius = 3.5f;
    private const float RotationHighlightThicknessMultiplier = 1.75f;
    private const float SlowDragMultiplier = 1f / 5f;
    private const float GizmoActiveAlpha = 1f;
    private const float GizmoIdleAlpha = 0.45f;
    private const float GizmoSliderAlpha = 0.12f;
    private const string OptionWheelPopupId = "TemplateEditorGizmoWheel";

    private static readonly Vector3[] AxisDirections =
    {
        Vector3.UnitX,
        Vector3.UnitY,
        Vector3.UnitZ,
    };

    private readonly AxisVisualState[] _axisVisualStates = new AxisVisualState[3];
    private readonly GizmoDragState _dragState = new();

    private Vector2 _optionWheelCenter;
    private Vector2 _centerBoneSelectorAnchor;
    private bool _centerBoneSelectorActive;
    private string? _centerBoneSelectorBoneName;
    private bool _wheelSuppressNextToggle;
    private bool _radialActionsPage;
    private RadialTooltipInfo? _pendingRadialTooltip;
    private double _centerHighlightUntil;

    public GizmoRenderer(
        IUiBuilder uiBuilder,
        TemplateEditorManager editorManager,
        TemplateEditorService gizmoService,
        PluginConfiguration configuration,
        ArmatureManager armatureManager,
        GameObjectService gameObjectService,
        CameraService cameraService,
        IGameGui gameGui,
        Logger logger)
    {
        _uiBuilder = uiBuilder;
        _editorManager = editorManager;
        _gizmoService = gizmoService;
        _configuration = configuration;
        _armatureManager = armatureManager;
        _gameObjectService = gameObjectService;
        _cameraService = cameraService;
        _gameGui = gameGui;
        _logger = logger;

        _uiBuilder.Draw += OnUiDraw;
    }

    public void Dispose()
    {
        _uiBuilder.Draw -= OnUiDraw;
    }

    private void OnUiDraw()
    {
        try
        {
            DrawGizmo();
        }
        catch (Exception ex)
        {
            _logger.Error($"Exception while rendering template editor gizmo:\n\t{ex}");
        }
        finally
        {
            if (_pendingRadialTooltip.HasValue)
            {
                DrawRadialTooltip(_pendingRadialTooltip.Value);
                _pendingRadialTooltip = null;
            }
        }
    }

    private unsafe void DrawGizmo()
    {
        if (!TryBuildRenderContext(out var context))
        {
            CancelDrag();
            return;
        }

        if (_dragState.ActiveBone != null && !string.Equals(_dragState.ActiveBone, context.BoneName, StringComparison.Ordinal))
            CancelDrag();

        if (_dragState.IsDragging && _dragState.ActiveAttribute != context.GizmoAttribute)
            CancelDrag();

        DrawGizmoContext(context);
        DrawOptionWheel();
    }

    private void DrawGizmoContext(in GizmoRenderContext context)
    {
        var options = GizmoRenderOptions.Default(context.GizmoAttribute);
        switch (context.GizmoAttribute)
        {
            case BoneAttribute.Position:
            case BoneAttribute.Scale:
                var mode = context.GizmoAttribute == BoneAttribute.Position
                    ? LinearMode.Translation
                    : LinearMode.Scale;
                DrawLinearGizmo(
                    context.BoneName,
                    context.Bone,
                    context.LocalPosition,
                    context.WorldPosition,
                    context.ScreenPos,
                    context.RootTransform,
                    context.BoneRotation,
                    context.TemplateTransform,
                    mode,
                    context.CameraViewDirection,
                    context.UseWorldSpace,
                    options);
                break;
            case BoneAttribute.Rotation:
                DrawRotationGizmo(
                    context.BoneName,
                    context.Bone,
                    context.RootTransform,
                    context.BoneRotation,
                    context.ScreenPos,
                    context.TemplateTransform,
                    context.CameraInfo,
                    context.UseWorldSpace,
                    options);
                break;
        }
    }

    private unsafe bool TryBuildRenderContext(out GizmoRenderContext context)
    {
        context = default;

        if (!_editorManager.IsEditorActive)
            return false;

        if (!_gizmoService.GizmoEnabled)
            return false;

        if (!_gizmoService.HasValidContext || string.IsNullOrEmpty(_gizmoService.SelectedBone))
            return false;

        var template = _editorManager.CurrentlyEditedTemplate;
        if (template == null)
            return false;

        if (!TryGetArmature(_gizmoService.CurrentActor, out var armature))
            return false;

        var bone = FindModelBone(armature, _gizmoService.SelectedBone!);
        if (bone == null)
            return false;

        var actor = FindActor(_gizmoService.CurrentActor);
        if (!actor.Valid)
            return false;

        var cBase = actor.Model.AsCharacterBase;
        if (cBase == null)
            return false;

        if (!TryGetActorRootTransform(cBase, out var rootTransform))
            return false;

        var transformAccess = bone.GetGameTransformAccess(cBase, ModelBone.PoseType.Model);
        if (transformAccess == null)
            return false;

        var transform = *transformAccess;
        var localPosition = transform.Translation.ToVector3();
        var boneRotation = NormalizeOrIdentity(transform.Rotation.ToQuaternion());

        var worldPosition = TransformPointToWorld(rootTransform, localPosition);
        if (!_gameGui.WorldToScreen(worldPosition, out var screenPos))
            return false;

        var boneName = bone.BoneName;
        template.Bones.TryGetValue(boneName, out var templateTransform);

        CameraService.CameraInfo? cameraInfo = null;
        Vector3? cameraViewDirection = null;
        if (_cameraService.TryGetCameraInfo(out var camInfo))
        {
            cameraInfo = camInfo;
            var toBone = worldPosition - camInfo.Position;
            if (toBone.LengthSquared() > float.Epsilon)
                cameraViewDirection = Vector3.Normalize(toBone);
        }

        context = new GizmoRenderContext
        {
            Bone = bone,
            BoneName = boneName,
            LocalPosition = localPosition,
            WorldPosition = worldPosition,
            ScreenPos = screenPos,
            RootTransform = rootTransform,
            BoneRotation = boneRotation,
            TemplateTransform = templateTransform,
            GizmoAttribute = _gizmoService.ActiveAttribute,
            UseWorldSpace = _gizmoService.UseWorldSpace,
            CameraViewDirection = cameraViewDirection,
            CameraInfo = cameraInfo,
        };
        return true;
    }

    private bool TryGetArmature(ActorIdentifier identifier, out Armature armature)
    {
        if (_armatureManager.Armatures.TryGetValue(identifier, out armature))
            return true;

        foreach (var pair in _armatureManager.Armatures)
        {
            if (pair.Key.Matches(identifier) || identifier.Matches(pair.Key))
            {
                armature = pair.Value;
                return true;
            }
        }

        armature = null!;
        return false;
    }

    private static ModelBone? FindModelBone(Armature armature, string boneName)
        => armature.GetAllBones().FirstOrDefault(b => string.Equals(b.BoneName, boneName, StringComparison.Ordinal));

    private PenumbraActor FindActor(ActorIdentifier identifier)
        => _gameObjectService.FindActorsByIdentifierIgnoringOwnership(identifier).Select(a => a.Item2).FirstOrDefault();

    private struct GizmoRenderContext
    {
        public ModelBone Bone;
        public string BoneName;
        public Vector3 LocalPosition;
        public Vector3 WorldPosition;
        public Vector2 ScreenPos;
        public ActorRootTransform RootTransform;
        public Quaternion BoneRotation;
        public BoneTransform? TemplateTransform;
        public BoneAttribute GizmoAttribute;
        public bool UseWorldSpace;
        public Vector3? CameraViewDirection;
        public CameraService.CameraInfo? CameraInfo;
    }

    private sealed class GizmoDragState
    {
        public bool IsDragging { get; private set; }
        public GizmoAxis ActiveAxis { get; private set; } = GizmoAxis.None;
        public string? ActiveBone { get; private set; }
        public bool AppliedChange { get; private set; }
        public Vector2 StartMouse { get; private set; }
        public Vector3 StartTranslation { get; private set; }
        public Vector3 LastAppliedTranslation { get; private set; }
        public Vector3 StartRotation { get; private set; }
        public Vector3 LastAppliedRotation { get; private set; }
        public Vector3 StartScale { get; private set; }
        public Vector3 LastAppliedScale { get; private set; }
        public Quaternion BoneRotation { get; private set; } = Quaternion.Identity;
        public ActorRootTransform ActorTransform { get; private set; }
        public Vector3 AxisWorldDirection { get; private set; }
        public float AxisWorldLength { get; private set; }
        public Vector2 AxisScreenDirection { get; private set; }
        public float AxisScreenLength { get; private set; }
        public BoneTransform? OriginalTransformSnapshot { get; private set; }
        public BoneAttribute ActiveAttribute { get; private set; } = BoneAttribute.Position;
        public string? MirrorTwinBoneName { get; private set; }
        public bool MirrorTwinIsSpecial { get; private set; }
        public Vector2? RotationDragStartScreenPoint { get; private set; }
        public float? RotationDragStartAngle { get; private set; }
        public float? RotationDragLastAngle { get; private set; }
        public bool HasRotationProjection { get; private set; }
        public Vector2 RotationProjectionCenter { get; private set; }
        public float RotationProjectionRadius { get; private set; }
        public Matrix4x4 RotationProjectionTransformMatrix { get; private set; } = Matrix4x4.Identity;
        public Matrix4x4 RotationProjectionViewMatrix { get; private set; } = Matrix4x4.Identity;
        public float RotationDragDistance { get; private set; }
        public Vector2? TranslationDragStartScreenPos { get; private set; }
        public Vector3? TranslationDragStartWorldPos { get; private set; }
        public Vector3? TranslationDragStartLocalPos { get; private set; }
        public float RotationDragDeltaRadians { get; private set; }

        public bool IsDraggingBone(string boneName)
            => IsDragging && string.Equals(ActiveBone, boneName, StringComparison.Ordinal);

        public void Begin(
            GizmoAxis axis,
            string boneName,
            BoneAttribute attribute,
            Vector2 startMouse,
            in ActorRootTransform actorTransform,
            Quaternion boneRotation,
            Vector2 axisScreenDirection,
            float axisScreenLength,
            Vector3 axisWorldDirection,
            float axisWorldLength,
            BoneTransform? templateTransform)
        {
            IsDragging = true;
            ActiveAxis = axis;
            ActiveBone = boneName;
            ActiveAttribute = attribute;
            AppliedChange = false;

            StartMouse = startMouse;
            BoneRotation = boneRotation;
            ActorTransform = actorTransform;
            AxisWorldDirection = axisWorldDirection;
            AxisWorldLength = axisWorldLength;
            AxisScreenDirection = axisScreenDirection;
            AxisScreenLength = axisScreenLength;

            OriginalTransformSnapshot = templateTransform != null ? new BoneTransform(templateTransform) : new BoneTransform();
            StartTranslation = OriginalTransformSnapshot.Translation;
            LastAppliedTranslation = StartTranslation;
            StartRotation = OriginalTransformSnapshot.Rotation;
            LastAppliedRotation = StartRotation;
            StartScale = OriginalTransformSnapshot.Scaling;
            LastAppliedScale = StartScale;
        }

        public void SetMirrorTwin(string? mirrorTwinBoneName, bool mirrorTwinIsSpecial)
        {
            MirrorTwinBoneName = mirrorTwinBoneName;
            MirrorTwinIsSpecial = mirrorTwinIsSpecial;
        }

        public void SetRotationAnchor(Vector2? startScreenPoint, float? startAngle)
        {
            RotationDragStartScreenPoint = startScreenPoint;
            RotationDragStartAngle = startAngle;
            RotationDragLastAngle = startAngle;
            RotationDragDistance = 0f;
            RotationDragDeltaRadians = 0f;
        }

        public void ClearRotationAnchor()
            => SetRotationAnchor(null, null);

        public void SetRotationDeltaRadians(float deltaRadians)
            => RotationDragDeltaRadians = deltaRadians;

        public void SetRotationLastAngle(float? angle)
            => RotationDragLastAngle = angle;

        public void SetRotationAnchorScreenPoint(Vector2? screenPoint)
            => RotationDragStartScreenPoint = screenPoint;

        public void SetRotationProjection(Vector2 center, float radius, Matrix4x4 transformMatrix, Matrix4x4 viewMatrix)
        {
            RotationProjectionCenter = center;
            RotationProjectionRadius = radius;
            RotationProjectionTransformMatrix = transformMatrix;
            RotationProjectionViewMatrix = viewMatrix;
            HasRotationProjection = radius > float.Epsilon;
        }

        public void ClearRotationProjection()
        {
            HasRotationProjection = false;
            RotationProjectionRadius = 0f;
            RotationProjectionCenter = default;
            RotationProjectionTransformMatrix = Matrix4x4.Identity;
            RotationProjectionViewMatrix = Matrix4x4.Identity;
        }

        public void SetRotationDragDistance(float dragDistance)
            => RotationDragDistance = dragDistance;

        public void SetTranslationStart(Vector2? screenPos, Vector3? worldPos, Vector3? localPos)
        {
            TranslationDragStartScreenPos = screenPos;
            TranslationDragStartWorldPos = worldPos;
            TranslationDragStartLocalPos = localPos;
        }

        public void ClearTranslationStart()
            => SetTranslationStart(null, null, null);

        public void SetTranslationWorldStart(Vector3? worldPos)
            => TranslationDragStartWorldPos = worldPos;

        public void SetTranslationScreenStart(Vector2? screenPos)
            => TranslationDragStartScreenPos = screenPos;

        public void MarkTranslationApplied(Vector3 translation)
        {
            LastAppliedTranslation = translation;
            AppliedChange = true;
        }

        public void MarkRotationApplied(Vector3 rotation)
        {
            LastAppliedRotation = rotation;
            AppliedChange = true;
        }

        public void MarkScaleApplied(Vector3 scale)
        {
            LastAppliedScale = scale;
            AppliedChange = true;
        }

        public void Reset(BoneAttribute fallbackAttribute)
        {
            IsDragging = false;
            ActiveAxis = GizmoAxis.None;
            ActiveBone = null;
            OriginalTransformSnapshot = null;
            ActiveAttribute = fallbackAttribute;
            AppliedChange = false;
            ClearRotationAnchor();
            ClearRotationProjection();
            ClearTranslationStart();
            SetMirrorTwin(null, false);
        }
    }
}
