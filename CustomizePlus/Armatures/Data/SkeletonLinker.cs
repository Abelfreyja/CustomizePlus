using CustomizePlus.Core.Data;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace CustomizePlus.Armatures.Data;

internal static unsafe class SkeletonLinker
{
    public static ModelBone[][] Build(Armature armature, CharacterBase* cBase)
    {
        List<List<ModelBone>> partials = new();
        if (cBase == null || cBase->Skeleton == null)
            return Array.Empty<ModelBone[]>();

        try
        {
            BuildPartialSkeletons(armature, cBase, partials);
            ConnectPartialSkeletonRoots(cBase, partials);
        }
        catch (Exception ex)
        {
            CustomizePlus.Logger.Error($"Error parsing armature skeleton from {cBase->ToString()}:\n\t{ex}");
        }

        CacheDescendants(partials);
        LogNewBones(partials);

        return partials.Select(partial => partial.ToArray()).ToArray();
    }

    private static void BuildPartialSkeletons(Armature armature, CharacterBase* cBase, List<List<ModelBone>> partials)
    {
        for (var partialIndex = 0; partialIndex < cBase->Skeleton->PartialSkeletonCount; ++partialIndex)
        {
            partials.Add(new List<ModelBone>());

            var currentPartial = cBase->Skeleton->PartialSkeletons[partialIndex];
            var currentPose = currentPartial.GetHavokPose(Constants.TruePoseIndex);
            if (currentPose == null || currentPose->Skeleton == null)
                continue;

            for (var boneIndex = 0; boneIndex < currentPose->Skeleton->Bones.Length; ++boneIndex)
            {
                var boneName = currentPose->Skeleton->Bones[boneIndex].Name.String;
                if (boneName == null)
                {
                    CustomizePlus.Logger.Error($"Failed to process bone @ <{partialIndex}, {boneIndex}> while parsing bones from {cBase->ToString()}");
                    continue;
                }

                var bone = new ModelBone(armature, boneName, partialIndex, boneIndex);
                CustomizePlus.Logger.Verbose($"Created new bone: {boneName} on {partialIndex}->{boneIndex} for {armature}");

                ConnectLocalParent(partials[partialIndex], currentPose->Skeleton->ParentIndices[boneIndex], bone);
                ConnectTwin(partials, bone);

                partials[partialIndex].Add(bone);
            }
        }
    }

    private static void ConnectLocalParent(List<ModelBone> partialBones, short parentIndex, ModelBone bone)
    {
        if (parentIndex < 0)
            return;

        if (parentIndex >= partialBones.Count)
        {
            CustomizePlus.Logger.Error($"Failed to link parent {parentIndex} while parsing {bone}");
            return;
        }

        var parentBone = partialBones[parentIndex];
        bone.AddParent(parentBone);
        parentBone.AddChild(bone);
    }

    private static void ConnectTwin(IEnumerable<List<ModelBone>> partials, ModelBone bone)
    {
        foreach (var existingBone in partials.SelectMany(partial => partial))
        {
            if (!AreTwinnedNames(bone.BoneName, existingBone.BoneName))
                continue;

            bone.AddTwin(existingBone);
            existingBone.AddTwin(bone);
            return;
        }
    }

    private static void ConnectPartialSkeletonRoots(CharacterBase* cBase, IReadOnlyList<List<ModelBone>> partials)
    {
        if (partials.Count <= 1 || partials[0].Count == 0)
            return;

        for (var partialIndex = 1; partialIndex < partials.Count; partialIndex++)
        {
            if (partials[partialIndex].Count == 0)
                continue;

            var roots = partials[partialIndex]
                .Where(bone => bone.ParentBone == null)
                .ToList();
            if (roots.Count == 0)
                continue;

            var partial = cBase->Skeleton->PartialSkeletons[partialIndex];
            // single root partials use the connector indices exposed by the game skeleton
            if (roots.Count == 1
                && TryGetParsedBone(partials, 0, partial.ConnectedParentBoneIndex, out var connectedParent)
                && TryGetParsedBone(partials, partialIndex, partial.ConnectedBoneIndex, out var connectedBone)
                && ConnectParentChild(connectedParent, connectedBone))
            {
                continue;
            }

            ConnectRootsByName(partials[0], roots);
        }
    }

    private static void ConnectRootsByName(IReadOnlyList<ModelBone> rootPartial, IEnumerable<ModelBone> roots)
    {
        foreach (var root in roots)
        {
            // multi root partials are usually mapped back to matching root partial names
            var parent = rootPartial.FirstOrDefault(bone => bone.BoneName == root.BoneName);
            if (parent != null)
                _ = ConnectParentChild(parent, root);
        }
    }

    private static bool TryGetParsedBone(IReadOnlyList<List<ModelBone>> partials, int partialIndex, short boneIndex, out ModelBone bone)
    {
        if (partialIndex >= 0
            && partialIndex < partials.Count
            && boneIndex >= 0
            && boneIndex < partials[partialIndex].Count)
        {
            bone = partials[partialIndex][boneIndex];
            return true;
        }

        bone = null!;
        return false;
    }

    private static bool ConnectParentChild(ModelBone parent, ModelBone child)
    {
        if (ReferenceEquals(parent, child) || child.ParentBone != null)
            return false;

        child.AddParent(parent);
        parent.AddChild(child);
        return true;
    }

    private static void CacheDescendants(IEnumerable<List<ModelBone>> partials)
    {
        foreach (var bone in partials.SelectMany(partial => partial))
        {
            bone.CacheDescendants();
        }
    }

    private static void LogNewBones(IEnumerable<List<ModelBone>> partials)
    {
        try
        {
            BoneData.LogNewBones(partials.SelectMany(partial => partial.Select(bone => bone.BoneName)).ToArray());
        }
        catch (Exception ex)
        {
            CustomizePlus.Logger.Error($"Error logging parsed armature bones:\n\t{ex}");
        }
    }

    private static bool AreTwinnedNames(string name1, string name2)
    {
        if (name1.Length < 2 || name2.Length < 2)
            return false;

        return ((name1[^1] == 'r' && name2[^1] == 'l') || (name1[^1] == 'l' && name2[^1] == 'r'))
            && name1[..^1] == name2[..^1];
    }
}
