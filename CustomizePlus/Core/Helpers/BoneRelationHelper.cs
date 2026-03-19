using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using System;
using System.Collections.Generic;

namespace CustomizePlus.Core.Helpers;

public static class BoneRelationHelper
{
    public static List<string> EnumerateAncestors(ModelBone? bone, string boneName)
    {
        if (bone != null)
        {
            var ancestors = EnumerateAncestorsFromModelBone(bone);
            if (ancestors.Count > 0)
                return ancestors;
        }

        return EnumerateAncestorsFromBoneData(boneName);
    }

    public static List<string> EnumerateDescendants(ModelBone? bone, string boneName)
    {
        if (bone != null)
        {
            var descendants = EnumerateDescendantsFromModelBone(bone);
            if (descendants.Count > 0)
                return descendants;
        }

        return EnumerateDescendantsFromBoneData(boneName);
    }

    public static List<string> EnumerateAncestorsFromBoneData(string boneName)
    {
        var ancestors = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var parent = BoneData.GetParent(boneName);

        while (!string.IsNullOrEmpty(parent) && visited.Add(parent))
        {
            ancestors.Add(parent);
            parent = BoneData.GetParent(parent);
        }

        return ancestors;
    }

    public static List<string> EnumerateDescendantsFromBoneData(string boneName)
    {
        var descendants = new List<string>();
        if (string.IsNullOrEmpty(boneName))
            return descendants;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(BoneData.GetChildren(boneName));

        while (queue.Count > 0)
        {
            var child = queue.Dequeue();
            if (string.IsNullOrEmpty(child) || !visited.Add(child))
                continue;

            descendants.Add(child);

            foreach (var grandChild in BoneData.GetChildren(child))
                queue.Enqueue(grandChild);
        }

        return descendants;
    }

    private static List<string> EnumerateAncestorsFromModelBone(ModelBone bone)
    {
        var ancestors = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var parent = bone.ParentBone;

        while (parent != null && visited.Add(parent.BoneName))
        {
            ancestors.Add(parent.BoneName);
            parent = parent.ParentBone;
        }

        return ancestors;
    }

    private static List<string> EnumerateDescendantsFromModelBone(ModelBone bone)
    {
        var descendants = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<ModelBone>(bone.ChildBones);

        while (queue.Count > 0)
        {
            var child = queue.Dequeue();
            if (!visited.Add(child.BoneName))
                continue;

            descendants.Add(child.BoneName);

            foreach (var grandChild in child.ChildBones)
                queue.Enqueue(grandChild);
        }

        return descendants;
    }
}
