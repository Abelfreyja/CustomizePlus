using CustomizePlus.Core.Data;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.GameData.Actors;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// Represents a "copy" of the ingame skeleton upon which the linked character profile is meant to operate.
/// Acts as an interface by which the in-game skeleton can be manipulated on a bone-by-bone basis.
/// </summary>
public unsafe class Armature
{
    /// <summary>
    /// Gets the Customize+ profile for which this mockup applies transformations.
    /// </summary>
    public Profile Profile { get; set; }

    /// <summary>
    /// Static identifier of the actor associated with this armature
    /// </summary>
    public ActorIdentifier ActorIdentifier { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether or not this armature has any renderable objects on which it should act.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// Represents date and time when actor associated with this armature was last seen.
    /// Implemented mostly as a armature cleanup protection hack for mare and penumbra.
    /// </summary>
    public DateTime LastSeen { get; private set; }

    /// <summary>
    /// Gets a value indicating whether or not this armature has successfully built itself with bone information.
    /// </summary>
    public bool IsBuilt => _partialSkeletons.Any();

    /// <summary>
    /// Internal flag telling ArmatureManager that it should attempt to rebind profile to (another) profile whenever possible.
    /// </summary>
    public bool IsPendingProfileRebind { get; set; }

    /// <summary>
    /// For debugging purposes, each armature is assigned a globally-unique ID number upon creation.
    /// </summary>
    private static uint _nextGlobalId;
    private readonly uint _localId;

    /// <summary>
    /// Binding telling which bones are bound to each template for this armature. Built from template list in profile.
    /// </summary>
    public Dictionary<string, Template> BoneTemplateBinding { get; init; }

    /// <summary>
    /// Each skeleton is made up of several smaller "partial" skeletons.
    /// Each partial skeleton has its own list of bones, with a root bone at index zero.
    /// The root bone of a partial skeleton may also be a regular bone in a different partial skeleton.
    /// </summary>
    private ModelBone[][] _partialSkeletons;

    #region Bone Accessors -------------------------------------------------------------------------------

    /// <summary>
    /// Gets the number of partial skeletons contained in this armature.
    /// </summary>
    public int PartialSkeletonCount => _partialSkeletons.Length;

    /// <summary>
    /// Get the list of bones belonging to the partial skeleton at the given index.
    /// </summary>
    public ModelBone[] this[int i]
    {
        get => _partialSkeletons[i];
    }

    /// <summary>
    /// Returns the number of bones contained within the partial skeleton with the given index.
    /// </summary>
    public int GetBoneCountOfPartial(int partialIndex) => _partialSkeletons[partialIndex].Length;

    /// <summary>
    /// Get the bone at index 'j' within the partial skeleton at index 'i'.
    /// </summary>
    public ModelBone this[int i, int j]
    {
        get => _partialSkeletons[i][j];
    }

    /// <summary>
    /// Return the bone at the given indices, if it exists
    /// </summary>
    public ModelBone? GetBoneAt(int partialIndex, int boneIndex)
    {
        if (partialIndex >= 0
            && boneIndex >= 0
            && _partialSkeletons.Length > partialIndex
            && _partialSkeletons[partialIndex].Length > boneIndex)
        {
            return this[partialIndex, boneIndex];
        }

        return null;
    }

    /// <summary>
    /// Returns the root bone of the partial skeleton with the given index.
    /// </summary>
    public ModelBone GetRootBoneOfPartial(int partialIndex) => this[partialIndex, 0];

    public ModelBone MainRootBone => GetRootBoneOfPartial(0);

    /// <summary>
    /// Get the total number of bones in each partial skeleton combined.
    /// </summary>
    // In exactly one partial skeleton will the root bone be an independent bone. In all others, it's a reference to a separate, real bone.
    // For that reason we must subtract the number of duplicate bones
    public int TotalBoneCount => _partialSkeletons.Sum(x => x.Length);

    public IEnumerable<ModelBone> GetAllBones()
    {
        for (var i = 0; i < _partialSkeletons.Length; ++i)
        {
            for (var j = 0; j < _partialSkeletons[i].Length; ++j)
            {
                yield return this[i, j];
            }
        }
    }

    //----------------------------------------------------------------------------------------------------
    #endregion

    public Armature(ActorIdentifier actorIdentifier, Profile profile)
    {
        _localId = _nextGlobalId++;

        _partialSkeletons = Array.Empty<ModelBone[]>();

        BoneTemplateBinding = new Dictionary<string, Template>();

        ActorIdentifier = actorIdentifier;
        Profile = profile;
        IsVisible = false;

        UpdateLastSeen();

        Profile.Armatures.Add(this);

        CustomizePlus.Logger.Debug($"Instantiated {this}, attached to {Profile}");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return IsBuilt
            ? $"Armature (#{_localId}) on {ActorIdentifier.IncognitoDebug()} ({Profile}) with {TotalBoneCount} bone/s"
            : $"Armature (#{_localId}) on {ActorIdentifier.IncognitoDebug()} ({Profile}) with no skeleton reference";
    }

    public bool IsSkeletonUpdated(CharacterBase* cBase)
    {
        if (cBase == null || cBase->Skeleton == null)
            return false;

        var skeleton = cBase->Skeleton;
        if (skeleton->PartialSkeletonCount != _partialSkeletons.Length)
            return true;

        for (var partialIndex = 0; partialIndex < skeleton->PartialSkeletonCount; ++partialIndex)
        {
            var partial = skeleton->PartialSkeletons[partialIndex];
            var pose = partial.GetHavokPose(Constants.TruePoseIndex);
            if (pose == null || pose->Skeleton == null)
                continue;

            if (pose->Skeleton->Bones.Length != _partialSkeletons[partialIndex].Length)
                return true;

            var rootCount = 0;
            var rootBoneIndex = -1;
            for (var boneIndex = 0; boneIndex < pose->Skeleton->ParentIndices.Length; boneIndex++)
            {
                if (pose->Skeleton->ParentIndices[boneIndex] < 0)
                {
                    rootCount++;
                    rootBoneIndex = boneIndex;
                }
            }

            if (partialIndex > 0
                && rootCount == 1
                && IsSingleRootPartialConnectionUpdated(partialIndex, rootBoneIndex, partial.ConnectedParentBoneIndex, partial.ConnectedBoneIndex))
            {
                return true;
            }

            for (var boneIndex = 0; boneIndex < pose->Skeleton->Bones.Length; boneIndex++)
            {
                if (IsBoneDefinitionUpdated(partialIndex, boneIndex, pose->Skeleton->Bones[boneIndex].Name.String, pose->Skeleton->ParentIndices[boneIndex]))
                    return true;
            }
        }

        return false;
    }

    private bool IsSingleRootPartialConnectionUpdated(int partialIndex, int rootBoneIndex, short connectedParentBoneIndex, short connectedBoneIndex)
    {
        var rootBone = GetBoneAt(partialIndex, rootBoneIndex);
        if (rootBone == null)
            return true;

        var expectedParent = connectedBoneIndex == rootBoneIndex
            ? GetBoneAt(0, connectedParentBoneIndex)
            : null;

        expectedParent ??= _partialSkeletons[0].FirstOrDefault(bone => bone.BoneName == rootBone.BoneName);

        return !ReferenceEquals(rootBone.ParentBone, expectedParent);
    }

    private bool IsBoneDefinitionUpdated(int partialIndex, int boneIndex, string? boneName, short parentIndex)
    {
        var bone = _partialSkeletons[partialIndex][boneIndex];
        if (bone.BoneName != boneName)
            return true;

        var parent = bone.ParentBone;
        if (parentIndex < 0)
            return parent != null && parent.PartialSkeletonIndex == partialIndex;

        return parent == null
               || parent.PartialSkeletonIndex != partialIndex
               || parent.BoneIndex != parentIndex;
    }

    /// <summary>
    /// Rebuild the armature using the provided character base as a reference.
    /// </summary>
    public void RebuildSkeleton(CharacterBase* cBase)
    {
        if (cBase == null || cBase->Skeleton == null)
            return;

        _partialSkeletons = SkeletonLinker.Build(this, cBase);

        RebuildBoneTemplateBinding(); //todo: intentionally not calling ArmatureChanged.Type.Updated because this is pending rewrite

        CustomizePlus.Logger.Debug($"Rebuilt {this}");
    }

    public BoneTransform? GetAppliedBoneTransform(string boneName)
    {
        if (BoneTemplateBinding.TryGetValue(boneName, out var template)
            && template != null)
        {
            if (template.Bones.TryGetValue(boneName, out var boneTransform))
                return boneTransform;
            else
                CustomizePlus.Logger.Error($"Bone {boneName} is null in template {template.UniqueId}");
        }

        return null;
    }

    /// <summary>
    /// Update last time actor for this armature was last seen in the game
    /// </summary>
    public void UpdateLastSeen(DateTime? dateTime = null)
    {
        if (dateTime == null)
            dateTime = DateTime.UtcNow;

        LastSeen = (DateTime)dateTime;
    }

    public void RebuildBoneTemplateBinding()
    {
        BoneTemplateBinding.Clear();

        foreach (var template in Profile.Templates)
        {
            if (Profile.DisabledTemplates.Contains(template.UniqueId))
                continue;

            foreach (var kvPair in template.Bones)
            {
                BoneTemplateBinding[kvPair.Key] = template;
            }
        }

        foreach (var bone in GetAllBones())
            bone.LinkToTemplate(BoneTemplateBinding.ContainsKey(bone.BoneName) ? BoneTemplateBinding[bone.BoneName] : null);

        CustomizePlus.Logger.Debug($"Rebuilt template binding for armature {_localId}");
    }

}