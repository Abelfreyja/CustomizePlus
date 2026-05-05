using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;

namespace CustomizePlus.Armatures.Data;

internal static unsafe class BoneTransformPropagator
{
    public static void Apply(CharacterBase* cBase, ModelBone bone)
    {
        var transform = bone.CustomizedTransform;
        if (transform != null)
            Apply(cBase, bone, transform);
    }

    public static void Apply(CharacterBase* cBase, ModelBone bone, BoneTransform transform)
    {
        if (cBase == null || !transform.IsEdited())
            return;

        var gameTransformAccess = BonePoseAccessor.Access(cBase, bone, LivePoseSpace.Model);
        if (gameTransformAccess == null)
            return;

        var initialTransform = *gameTransformAccess;
        var modifiedTransform = transform.ModifyExistingTransform(initialTransform);
        if (modifiedTransform.Equals(Constants.NullTransform))
            return;

        if (!BonePoseAccessor.Set(cBase, bone, modifiedTransform, LivePoseSpace.Model))
            return;

        var propagation = CreatePropagation(transform, initialTransform, modifiedTransform);
        if (!propagation.HasAny)
            return;

        ApplyToDescendants(cBase, bone, propagation);
    }

    private static Propagation CreatePropagation(BoneTransform transform, hkQsTransformf initialTransform, hkQsTransformf modifiedTransform)
    {
        var propagateTranslation = transform.PropagateTranslation && !transform.Translation.Equals(Vector3.Zero);
        var propagateRotation = transform.PropagateRotation && !transform.Rotation.Equals(Vector3.Zero);
        var propagateScale = transform.PropagateScale
            && (!transform.Scaling.Equals(Vector3.One)
                || (transform.ChildScalingIndependent && !transform.ChildScaling.Equals(Vector3.One)));

        var childScale = modifiedTransform.Scale.ToVector3();
        if (transform.ChildScalingIndependent)
        {
            var initialScale = initialTransform.Scale.ToVector3();
            childScale = new Vector3(
                initialScale.X * transform.ChildScaling.X,
                initialScale.Y * transform.ChildScaling.Y,
                initialScale.Z * transform.ChildScaling.Z);
        }

        return new Propagation(
            initialTransform.Translation.ToVector3(),
            propagateTranslation ? modifiedTransform.Translation.ToVector3() : initialTransform.Translation.ToVector3(),
            propagateRotation ? modifiedTransform.Rotation.ToQuaternion() / initialTransform.Rotation.ToQuaternion() : Quaternion.Identity,
            propagateScale ? DivideScale(childScale, initialTransform.Scale.ToVector3()) : Vector3.One,
            propagateTranslation,
            propagateRotation,
            propagateScale);
    }

    private static void ApplyToDescendants(CharacterBase* cBase, ModelBone bone, Propagation propagation)
    {
        foreach (var child in bone.DescendantBones)
        {
            // keep this manual so child scale can stay separate from the source bone scale
            var access = BonePoseAccessor.Access(cBase, child, LivePoseSpace.Model);
            if (access == null)
                continue;

            var offset = access->Translation.ToVector3() - propagation.SourcePosition;

            var matrix = InteropAlloc.GetMatrix(access);
            if (propagation.Scale)
            {
                var scaleMatrix = Matrix4x4.CreateScale(propagation.ScaleDelta, Vector3.Zero);
                matrix *= scaleMatrix;
                offset = Vector3.Transform(offset, scaleMatrix);
            }

            if (propagation.Rotation)
            {
                matrix *= Matrix4x4.CreateFromQuaternion(propagation.RotationDelta);
                offset = Vector3.Transform(offset, propagation.RotationDelta);
            }

            matrix.Translation = propagation.TargetPosition + offset;
            InteropAlloc.SetMatrix(access, matrix);
        }
    }

    private static Vector3 DivideScale(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            DivideScale(value.X, divisor.X),
            DivideScale(value.Y, divisor.Y),
            DivideScale(value.Z, divisor.Z));
    }

    private static float DivideScale(float value, float divisor)
    {
        return Math.Abs(divisor) > 0.00001f
            ? value / divisor
            : 1f;
    }

    private readonly struct Propagation
    {
        public Vector3 SourcePosition { get; }
        public Vector3 TargetPosition { get; }
        public Quaternion RotationDelta { get; }
        public Vector3 ScaleDelta { get; }
        public bool Translation { get; }
        public bool Rotation { get; }
        public bool Scale { get; }

        public Propagation(
            Vector3 sourcePosition,
            Vector3 targetPosition,
            Quaternion rotationDelta,
            Vector3 scaleDelta,
            bool translation,
            bool rotation,
            bool scale)
        {
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            RotationDelta = rotationDelta;
            ScaleDelta = scaleDelta;
            Translation = translation;
            Rotation = rotation;
            Scale = scale;
        }

        public bool HasAny => Translation || Rotation || Scale;
    }
}
