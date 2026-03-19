using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates.Events;
using OtterGui.Services;
using Penumbra.GameData.Actors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Templates;

/// <summary>
/// Owns template editor interaction state, history, and bone editing commands shared by the panel and gizmo UI.
/// </summary>
public sealed class TemplateEditorService : IDisposable, IService
{
    private readonly TemplateChanged _templateChanged;
    private readonly TemplateEditorManager _editorManager;
    private readonly PluginConfiguration _configuration;

    private readonly HashSet<string> _favoriteBones;
    private readonly Stack<Dictionary<string, BoneTransform>> _undoStack = new();
    private readonly Stack<Dictionary<string, BoneTransform>> _redoStack = new();

    private Template? _currentTemplate;
    private ActorIdentifier _currentActor = ActorIdentifier.Invalid;
    private Dictionary<string, BoneTransform>? _pendingUndoSnapshot;
    private string? _selectedBone;
    private BoneAttribute _activeAttribute;
    private bool _useWorldSpace;
    private bool _mirrorMode;
    private bool _boneEditorSliderActive;
    private bool _gizmoEnabled;

    public Template? CurrentTemplate => _currentTemplate;
    public ActorIdentifier CurrentActor => _currentActor;
    public string? SelectedBone => _selectedBone;
    public BoneAttribute ActiveAttribute => _activeAttribute;
    public bool UseWorldSpace => _useWorldSpace;
    public bool MirrorMode => _mirrorMode;
    public bool IsBoneEditorSliderActive => _boneEditorSliderActive;
    public bool GizmoEnabled => _gizmoEnabled;
    public IReadOnlySet<string> FavoriteBones => _favoriteBones;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public bool HasValidContext => _currentTemplate != null && _currentActor.IsValid;

    public event Action? ContextChanged;

    public TemplateEditorService(TemplateChanged templateChanged, TemplateEditorManager editorManager, PluginConfiguration configuration)
    {
        _templateChanged = templateChanged;
        _editorManager = editorManager;
        _configuration = configuration;
        _templateChanged.Subscribe(OnTemplateChanged, TemplateChanged.Priority.TemplateEditorService);
        _activeAttribute = _configuration.EditorConfiguration.EditorMode;
        _mirrorMode = _configuration.EditorConfiguration.BoneMirroringEnabled;
        _gizmoEnabled = _configuration.EditorConfiguration.GizmoEnabled;
        _favoriteBones = new HashSet<string>(_configuration.EditorConfiguration.FavoriteBones, StringComparer.Ordinal);
    }

    public void Dispose()
        => _templateChanged.Unsubscribe(OnTemplateChanged);

    public enum EditorCommand
    {
        Undo,
        Redo,
        ToggleFavorite,
        TogglePropagation,
        ResetAttribute,
        RevertAttribute,
    }

    public enum EditState
    {
        Start,
        Commit,
        Cancel,
    }

    public void SetSelectedBone(string? boneName)
    {
        var sanitized = string.IsNullOrWhiteSpace(boneName) ? null : boneName;
        if (_selectedBone == sanitized)
            return;

        _selectedBone = sanitized;
        _templateChanged.Invoke(TemplateChanged.Type.EditorBoneSelectionChanged, _currentTemplate, sanitized);
        Notify();
    }

    public void SetActiveAttribute(BoneAttribute attribute)
    {
        if (_activeAttribute == attribute)
            return;

        _activeAttribute = attribute;
        _configuration.EditorConfiguration.EditorMode = attribute;
        _configuration.Save();
        Notify();
    }

    public void SetUseWorldSpace(bool useWorldSpace)
    {
        if (_useWorldSpace == useWorldSpace)
            return;

        _useWorldSpace = useWorldSpace;
        Notify();
    }

    public void SetBoneEditorSliderActive(bool isActive)
    {
        if (_boneEditorSliderActive == isActive)
            return;

        _boneEditorSliderActive = isActive;
    }

    public void SetMirrorMode(bool mirrorMode)
    {
        if (_mirrorMode == mirrorMode)
            return;

        _mirrorMode = mirrorMode;
        _configuration.EditorConfiguration.BoneMirroringEnabled = mirrorMode;
        _configuration.Save();
        Notify();
    }

    public void SetGizmoEnabled(bool enabled)
    {
        if (_gizmoEnabled == enabled)
            return;

        _gizmoEnabled = enabled;
        _configuration.EditorConfiguration.GizmoEnabled = enabled;
        _configuration.Save();
        Notify();
    }

    public void ExecuteCommand(EditorCommand command)
    {
        switch (command)
        {
            case EditorCommand.Undo:
                Undo();
                return;
            case EditorCommand.Redo:
                Redo();
                return;
            case EditorCommand.ToggleFavorite:
                ToggleFavoriteForSelected();
                return;
            case EditorCommand.TogglePropagation:
                TogglePropagationForSelectedBone();
                return;
            case EditorCommand.ResetAttribute:
                ResetBoneAttribute(SelectedBone, null, _activeAttribute);
                return;
            case EditorCommand.RevertAttribute:
                RevertBoneAttribute(SelectedBone, null, _activeAttribute);
                return;
        }
    }

    internal void NotifyEditStateChange(EditState state)
    {
        switch (state)
        {
            case EditState.Start:
                BeginPendingEdit();
                break;
            case EditState.Commit:
                CommitPendingEditIfChanged();
                break;
            case EditState.Cancel:
                CancelPendingEdit();
                break;
        }
    }

    public void ClearHistory()
    {
        _pendingUndoSnapshot = null;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public void BeginPendingEdit()
    {
        if (_pendingUndoSnapshot == null)
            _pendingUndoSnapshot = CaptureCurrentState();
    }

    public void CommitPendingEditIfChanged()
    {
        if (_pendingUndoSnapshot == null)
            return;

        CommitSnapshotIfChanged(_pendingUndoSnapshot);
        _pendingUndoSnapshot = null;
    }

    public void CancelPendingEdit()
        => _pendingUndoSnapshot = null;

    public void SaveCurrentStateForUndo()
        => SaveStateForUndo(CaptureCurrentState());

    public void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        CancelPendingEdit();
        var state = _undoStack.Pop();
        _redoStack.Push(CaptureCurrentState());
        RestoreState(state);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        CancelPendingEdit();
        var state = _redoStack.Pop();
        _undoStack.Push(CaptureCurrentState());
        RestoreState(state);
    }

    public bool IsFavoriteBone(string? boneName)
        => !string.IsNullOrEmpty(boneName) && _favoriteBones.Contains(boneName);

    public void ToggleFavorite(string? boneName)
    {
        if (string.IsNullOrEmpty(boneName))
            return;

        if (_favoriteBones.Contains(boneName))
            _favoriteBones.Remove(boneName);
        else
            _favoriteBones.Add(boneName);

        _configuration.EditorConfiguration.FavoriteBones = _favoriteBones.ToHashSet(StringComparer.Ordinal);
        _configuration.Save();
        Notify();
    }

    public void ToggleFavoriteForSelected()
        => ToggleFavorite(_selectedBone);

    public void TogglePropagationForSelectedBone(BoneAttribute? overrideAttribute = null)
    {
        if (string.IsNullOrEmpty(_selectedBone))
            return;

        var attribute = overrideAttribute ?? _activeAttribute;
        var snapshot = CaptureCurrentState();

        if (!TryGetSelectedBoneTransform(out var boneName, out var transform, createIfMissing: false))
            return;

        var newPropagation = !transform.IsPropagationEnabledForAttribute(attribute);

        ApplyPropagationToggle(boneName, attribute, newPropagation);

        if (_mirrorMode)
        {
            var mirrorBone = BoneData.GetBoneMirror(boneName);
            if (!string.IsNullOrEmpty(mirrorBone) && !string.Equals(mirrorBone, boneName, StringComparison.Ordinal))
                ApplyPropagationToggle(mirrorBone, attribute, newPropagation);
        }

        CommitSnapshotIfChanged(snapshot);
    }

    public void ResetBoneAttribute(string? boneName, string? mirroredBoneName, BoneAttribute? overrideAttribute = null)
    {
        if (string.IsNullOrEmpty(boneName))
            return;

        var attribute = overrideAttribute ?? _activeAttribute;
        var snapshot = CaptureCurrentState();
        ExecuteBoneAttributeChange(boneName, mirroredBoneName, attribute, _editorManager.ResetBoneAttributeChanges);
        CommitSnapshotIfChanged(snapshot);
    }

    public void RevertBoneAttribute(string? boneName, string? mirroredBoneName, BoneAttribute? overrideAttribute = null)
    {
        if (string.IsNullOrEmpty(boneName))
            return;

        var attribute = overrideAttribute ?? _activeAttribute;
        var snapshot = CaptureCurrentState();
        ExecuteBoneAttributeChange(boneName, mirroredBoneName, attribute, _editorManager.RevertBoneAttributeChanges);
        CommitSnapshotIfChanged(snapshot);
    }

    private void OnTemplateChanged(TemplateChanged.Type type, Template? template, object? data)
    {
        switch (type)
        {
            case TemplateChanged.Type.EditorEnabled:
                _currentTemplate = template;
                _currentActor = ExtractActorIdentifier(data);
                _selectedBone = null;
                ClearHistory();
                Notify();
                break;
            case TemplateChanged.Type.EditorDisabled:
                _currentActor = ActorIdentifier.Invalid;
                _currentTemplate = null;
                _selectedBone = null;
                ClearHistory();
                Notify();
                break;
            case TemplateChanged.Type.EditorCharacterChanged:
                var updatedActor = ExtractActorIdentifier(data);
                if (updatedActor.IsValid)
                {
                    _currentActor = updatedActor;
                    Notify();
                }
                break;
        }
    }

    private static ActorIdentifier ExtractActorIdentifier(object? data)
    {
        if (data is ActorIdentifier actorId)
            return actorId;

        if (data is ValueTuple<ActorIdentifier, bool> tupleWithFlag)
            return tupleWithFlag.Item1;

        if (data is ValueTuple<ActorIdentifier, Profile> tupleWithProfile)
            return tupleWithProfile.Item1;

        return ActorIdentifier.Invalid;
    }

    private bool TryGetSelectedBoneTransform(out string boneName, out BoneTransform transform, bool createIfMissing)
    {
        boneName = _selectedBone ?? string.Empty;
        transform = null!;
        if (string.IsNullOrEmpty(boneName))
            return false;

        return TryGetBoneTransform(boneName, out transform, createIfMissing);
    }

    private bool TryGetBoneTransform(string boneName, out BoneTransform transform, bool createIfMissing)
    {
        transform = null!;
        if (string.IsNullOrEmpty(boneName))
            return false;

        var template = _editorManager.CurrentlyEditedTemplate;
        if (template == null)
            return false;

        if (!template.Bones.TryGetValue(boneName, out var existingTransform) || existingTransform == null)
        {
            transform = new BoneTransform();
            if (createIfMissing)
                template.Bones[boneName] = transform;
            return true;
        }

        transform = new BoneTransform(existingTransform);
        return true;
    }

    private void ApplyPropagationToggle(string boneName, BoneAttribute attribute, bool newPropagation)
    {
        if (!TryGetBoneTransform(boneName, out var transform, createIfMissing: false))
            return;

        transform.UpdateAttribute(attribute, transform.GetValueForAttribute(attribute), newPropagation);
        _editorManager.ModifyBoneTransform(boneName, transform);
    }

    private void ExecuteBoneAttributeChange(string boneName, string? mirroredBoneName, BoneAttribute attribute, Func<string, BoneAttribute, bool> applyAction)
    {
        _ = applyAction(boneName, attribute);
        if (_mirrorMode)
        {
            var twin = mirroredBoneName ?? BoneData.GetBoneMirror(boneName);
            if (!string.IsNullOrEmpty(twin))
                _ = applyAction(twin, attribute);
        }

        SetSelectedBone(boneName);
    }

    private Dictionary<string, BoneTransform> CaptureCurrentState()
    {
        return _editorManager.EditorProfile?.Armatures.Count > 0
            ? _editorManager.EditorProfile.Armatures[0]
                .GetAllBones()
                .DistinctBy(b => b.BoneName)
                .ToDictionary(
                    b => b.BoneName,
                    b => new BoneTransform(b.CustomizedTransform ?? new BoneTransform()))
            : new Dictionary<string, BoneTransform>();
    }

    private void SaveStateForUndo(Dictionary<string, BoneTransform> snapshot)
    {
        if (_undoStack.Count == 0 || !AreSnapshotsEqual(_undoStack.Peek(), snapshot))
        {
            _undoStack.Push(snapshot);
            _redoStack.Clear();
        }
    }

    private void RestoreState(Dictionary<string, BoneTransform> state)
    {
        foreach (var kvp in state.DistinctBy(x => x.Key))
            _editorManager.ModifyBoneTransform(kvp.Key, kvp.Value);
    }

    private void CommitSnapshotIfChanged(Dictionary<string, BoneTransform> snapshot)
    {
        if (!AreSnapshotsEqual(snapshot, CaptureCurrentState()))
            SaveStateForUndo(snapshot);
    }

    private static bool AreSnapshotsEqual(IReadOnlyDictionary<string, BoneTransform> left, IReadOnlyDictionary<string, BoneTransform> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (boneName, leftTransform) in left)
        {
            if (!right.TryGetValue(boneName, out var rightTransform) || !AreTransformsEqual(leftTransform, rightTransform))
                return false;
        }

        return true;
    }

    private static bool AreTransformsEqual(BoneTransform left, BoneTransform right)
        => left.Translation == right.Translation
            && left.Rotation == right.Rotation
            && left.Scaling == right.Scaling
            && left.ChildScaling == right.ChildScaling
            && left.PropagateTranslation == right.PropagateTranslation
            && left.PropagateRotation == right.PropagateRotation
            && left.PropagateScale == right.PropagateScale
            && left.ChildScalingIndependent == right.ChildScalingIndependent;

    private void Notify()
        => ContextChanged?.Invoke();
}
