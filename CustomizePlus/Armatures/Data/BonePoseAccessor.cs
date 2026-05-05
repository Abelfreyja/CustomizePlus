using CustomizePlus.Core.Data;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using static FFXIVClientStructs.Havok.Animation.Rig.hkaPose;

namespace CustomizePlus.Armatures.Data;

internal enum LivePoseSpace
{
    Local,
    Model
}

internal static unsafe class BonePoseAccessor
{
    public static hkQsTransformf* Access(CharacterBase* cBase, ModelBone bone, LivePoseSpace space)
    {
        var targetPose = GetPose(cBase, bone);
        if (targetPose == null)
            return null;

        return space switch
        {
            LivePoseSpace.Local => targetPose->AccessBoneLocalSpace(bone.BoneIndex),
            LivePoseSpace.Model => targetPose->AccessBoneModelSpace(bone.BoneIndex, PropagateOrNot.DontPropagate),
            _ => throw new ArgumentOutOfRangeException(nameof(space), space, null)
        };
    }

    public static bool Set(CharacterBase* cBase, ModelBone bone, hkQsTransformf transform, LivePoseSpace space)
    {
        var targetPose = GetPose(cBase, bone);
        if (targetPose == null)
            return false;

        switch (space)
        {
            case LivePoseSpace.Local:
                if (targetPose->LocalInSync == 0)
                    return false;

                targetPose->LocalPose.Data[bone.BoneIndex] = transform;
                return true;

            case LivePoseSpace.Model:
                if (targetPose->ModelInSync == 0)
                    return false;

                targetPose->ModelPose.Data[bone.BoneIndex] = transform;
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(space), space, null);
        }
    }

    private static hkaPose* GetPose(CharacterBase* cBase, ModelBone bone)
    {
        if (cBase == null || cBase->Skeleton == null)
            return null;

        var skeleton = cBase->Skeleton;
        if (bone.PartialSkeletonIndex < 0 || bone.PartialSkeletonIndex >= skeleton->PartialSkeletonCount)
            return null;

        var partial = skeleton->PartialSkeletons[bone.PartialSkeletonIndex];
        var targetPose = partial.GetHavokPose(Constants.TruePoseIndex);
        if (targetPose == null || targetPose->Skeleton == null)
            return null;

        // skeleton swaps can leave cached bone indices stale for a frame
        if (bone.BoneIndex < 0 || bone.BoneIndex >= targetPose->Skeleton->Bones.Length)
            return null;

        if (targetPose->Skeleton->Bones[bone.BoneIndex].Name.String != bone.BoneName)
            return null;

        return targetPose;
    }
}
