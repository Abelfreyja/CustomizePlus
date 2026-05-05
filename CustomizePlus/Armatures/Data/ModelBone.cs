using CustomizePlus.Core.Data;
using CustomizePlus.Templates.Data;

namespace CustomizePlus.Armatures.Data;

/// <summary>
///     Represents a single bone of an ingame character's skeleton.
/// </summary>
public class ModelBone
{
    public Armature MasterArmature { get; }

    public int PartialSkeletonIndex { get; }
    public int BoneIndex { get; }

    /// <summary>
    /// Gets the model bone corresponding to this model bone's parent, if it exists.
    /// (It should in all cases but the root of the skeleton)
    /// </summary>
    public ModelBone? ParentBone { get; private set; }

    private readonly List<ModelBone> childBones = new();

    /// <summary>
    /// Gets each model bone for which this model bone corresponds to a direct parent thereof.
    /// A model bone may have zero children.
    /// </summary>
    public IReadOnlyList<ModelBone> ChildBones => childBones;

    private ModelBone[] descendantBones = Array.Empty<ModelBone>();
    private bool isGraphCached;

    internal ReadOnlySpan<ModelBone> DescendantBones => descendantBones;

    /// <summary>
    /// Gets the model bone that forms a mirror image of this model bone, if one exists.
    /// </summary>
    public ModelBone? TwinBone { get; private set; }

    /// <summary>
    /// The name of the bone within the in-game skeleton. Referred to in some places as its "code name".
    /// </summary>
    public string BoneName { get; }

    /// <summary>
    /// The transform that this model bone will impart upon its in-game sibling when the master armature
    /// is applied to the in-game skeleton. Reference to transform contained in top most template in profile applied to character.
    /// </summary>
    public BoneTransform? CustomizedTransform { get; private set; }

    /// <summary>
    /// True if bone is linked to any template
    /// </summary>
    public bool IsActive => CustomizedTransform != null;

    internal ModelBone(Armature arm, string codeName, int partialIdx, int boneIdx)
    {
        MasterArmature = arm;
        PartialSkeletonIndex = partialIdx;
        BoneIndex = boneIdx;

        BoneName = codeName;
    }

    /// <summary>
    /// Link bone to specific template, unlinks if null is passed
    /// </summary>
    /// <param name="template"></param>
    /// <returns></returns>
    public bool LinkToTemplate(Template? template)
    {
        if (template == null)
        {
            if (CustomizedTransform == null)
                return false;

            CustomizedTransform = null;

            CustomizePlus.Logger.Verbose($"Unlinked {BoneName} from all templates");

            return true;
        }

        if (!template.Bones.ContainsKey(BoneName))
            return false;

        CustomizePlus.Logger.Verbose($"Linking {BoneName} to {template.Name}");
        CustomizedTransform = template.Bones[BoneName];

        return true;
    }

    /// <summary>
    /// Indicate a bone to act as this model bone's "parent".
    /// </summary>
    internal void AddParent(ModelBone parent)
    {
        ThrowIfGraphCached();

        if (ParentBone != null)
        {
            throw new Exception($"Tried to add redundant parent to model bone -- {this}");
        }

        ParentBone = parent;
    }

    /// <summary>
    /// Indicate that a bone is one of this model bone's "children".
    /// </summary>
    internal void AddChild(ModelBone child)
    {
        ThrowIfGraphCached();

        if (!childBones.Contains(child))
            childBones.Add(child);
    }

    /// <summary>
    /// Indicate a bone that acts as this model bone's mirror image, or "twin".
    /// </summary>
    internal void AddTwin(ModelBone twin)
    {
        ThrowIfGraphCached();

        TwinBone = twin;
    }

    internal void CacheDescendants()
    {
        var descendants = new List<ModelBone>(childBones);
        var seen = new HashSet<ModelBone>(childBones);
        for (var i = 0; i < descendants.Count; i++)
        {
            foreach (var child in descendants[i].childBones)
            {
                if (!ReferenceEquals(child, this) && seen.Add(child))
                    descendants.Add(child);
            }
        }

        descendantBones = descendants.ToArray();
        isGraphCached = true;
    }

    private void ThrowIfGraphCached()
    {
        if (isGraphCached)
            throw new InvalidOperationException($"Tried to modify cached model bone graph -- {this}");
    }

    public override string ToString()
    {
        //string numCopies = _copyIndices.Count > 0 ? $" ({_copyIndices.Count} copies)" : string.Empty;
        return $"{BoneName} ({BoneData.GetBoneDisplayName(BoneName)}) @ <{PartialSkeletonIndex}, {BoneIndex}>";
    }

    /// <summary>
    /// Get the lineage of this model bone, going back to the skeleton's root bone.
    /// </summary>
    public IEnumerable<ModelBone> GetAncestors(bool includeSelf = true)
    {
        var bone = includeSelf ? this : ParentBone;
        while (bone != null)
        {
            yield return bone;
            bone = bone.ParentBone;
        }
    }

    /// <summary>
    /// Gets all model bones with a lineage that contains this one.
    /// </summary>
    public IEnumerable<ModelBone> GetDescendants(bool includeSelf = false)
    {
        if (includeSelf)
            yield return this;

        foreach (var descendant in descendantBones)
        {
            yield return descendant;
        }
    }

    /// <summary>
    /// Checks for a non-zero and non-identity (root) scale.
    /// </summary>
    /// <returns>If the scale should be applied.</returns>
    public bool IsModifiedScale()
    {
        var customizedTransform = CustomizedTransform;
        if (customizedTransform == null)
            return false;

        return (customizedTransform.Scaling.X != 0 && customizedTransform.Scaling.X != 1)
               || (customizedTransform.Scaling.Y != 0 && customizedTransform.Scaling.Y != 1)
               || (customizedTransform.Scaling.Z != 0 && customizedTransform.Scaling.Z != 1);
    }
}