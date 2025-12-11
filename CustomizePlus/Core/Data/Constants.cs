using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Havok.Common.Base.Math.Quaternion;
using FFXIVClientStructs.Havok.Common.Base.Math.Vector;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CustomizePlus.Core.Data;

internal static class Constants
{
    /// <summary>
    /// Version of the configuration file, when increased a converter should be implemented if necessary.
    /// </summary>
    public const int ConfigurationVersion = 5;

    /// <summary>
    /// The name of the root bone.
    /// </summary>
    public const string RootBoneName = "n_root";

    /// <summary>
    /// Minimum allowed value for any of the vector values.
    /// </summary>
    public const int MinVectorValueLimit = -512;

    /// <summary>
    /// Maximum allowed value for any of the vector values.
    /// </summary>
    public const int MaxVectorValueLimit = 512;

    /// <summary>
    /// Predicate function for determining if the given object table index represents an
    /// NPC in a busy area (i.e. there are ~245 other objects already).
    /// </summary>
    public static bool IsInObjectTableBusyNPCRange(int index) => index > 245;

    /// <summary>
    /// A "null" havok vector. Since the type isn't inherently nullable, and the default value (0, 0, 0, 0)
    /// is valid input in a lot of cases, we can use this instead.
    /// </summary>
    public static readonly hkVector4f NullVector = new()
    {
        X = float.NaN,
        Y = float.NaN,
        Z = float.NaN,
        W = float.NaN
    };

    /// <summary>
    /// A "null" havok quaternion. Since the type isn't inherently nullable, and the default value (0, 0, 0, 0)
    /// is valid input in a lot of cases, we can use this instead.
    /// </summary>
    public static readonly hkQuaternionf NullQuaternion = new()
    {
        X = float.NaN,
        Y = float.NaN,
        Z = float.NaN,
        W = float.NaN
    };

    /// <summary>
    /// A "null" havok transform. Since the type isn't inherently nullable, and the default values
    /// aren't immediately obviously wrong, we can use this instead.
    /// </summary>
    public static readonly hkQsTransformf NullTransform = new()
    {
        Translation = NullVector,
        Rotation = NullQuaternion,
        Scale = NullVector
    };

    /// <summary>
    /// The pose at index 0 is the only one we apparently need to care about.
    /// </summary>
    public const int TruePoseIndex = 0;

    /// <summary>
    /// Main render hook address
    /// </summary>
    public const string RenderHookAddress = "E8 ?? ?? ?? ?? 48 81 C3 ?? ?? ?? ?? BF ?? ?? ?? ?? 33 ED";

    /// <summary>
    /// Movement hook address, used for position offset and other changes which cannot be done in main hook
    /// Client::Game::Object::GameObject_UpdateVisualPosition
    /// </summary>
    public const string MovementHookAddress = "E8 ?? ?? ?? ?? 84 DB 74 3A";

    internal static class Colors
    {
        public static Vector4 Normal = new Vector4(1, 1, 1, 1);
        public static Vector4 Info = new Vector4(0.3f, 0.5f, 1f, 1);
        public static Vector4 Warning = new Vector4(1, 0.5f, 0, 1);
        public static Vector4 Error = new Vector4(1, 0, 0, 1);
        public static Vector4 Active = new Vector4(0, 1, 0, 1);
        public static Vector4 Favorite = new Vector4(0.9f, 0.8f, 0.4f, 1);
    }

    internal static class PropagationColors
    {
        private static readonly IReadOnlyDictionary<BoneData.BoneFamily, Vector4> Palette = new Dictionary<BoneData.BoneFamily, Vector4>
        {
            { BoneData.BoneFamily.Root,    new Vector4(0.18f, 0.18f, 0.18f, 0.38f) },
            { BoneData.BoneFamily.Spine,   new Vector4(0.18f, 0.28f, 0.42f, 0.38f) },
            { BoneData.BoneFamily.Chest,   new Vector4(0.24f, 0.28f, 0.44f, 0.38f) },
            { BoneData.BoneFamily.Arms,    new Vector4(0.30f, 0.24f, 0.40f, 0.38f) },
            { BoneData.BoneFamily.Hands,   new Vector4(0.34f, 0.28f, 0.30f, 0.38f) },
            { BoneData.BoneFamily.Legs,    new Vector4(0.20f, 0.32f, 0.26f, 0.38f) },
            { BoneData.BoneFamily.Feet,    new Vector4(0.22f, 0.34f, 0.28f, 0.38f) },
            { BoneData.BoneFamily.Tail,    new Vector4(0.22f, 0.32f, 0.44f, 0.38f) },
            { BoneData.BoneFamily.Face,    new Vector4(0.40f, 0.26f, 0.28f, 0.40f) },
            { BoneData.BoneFamily.Hair,    new Vector4(0.34f, 0.32f, 0.20f, 0.38f) },
            { BoneData.BoneFamily.Eyes,    new Vector4(0.26f, 0.38f, 0.50f, 0.38f) },
            { BoneData.BoneFamily.Ears,    new Vector4(0.32f, 0.30f, 0.24f, 0.38f) },
            { BoneData.BoneFamily.Groin,   new Vector4(0.34f, 0.24f, 0.36f, 0.38f) },
            { BoneData.BoneFamily.Unknown, new Vector4(0.18f, 0.18f, 0.18f, 0.38f) },
        };

        private static readonly Vector4 DefaultChild = new Vector4(0.22f, 0.32f, 0.44f, 0.38f);
        private const float ParentChannelBoost = 0f;
        private const float ParentAlphaBoost = 0.35f;
        private const float TooltipChildBoost = 0.07f;
        private const float TooltipChildAlphaBoost = 0.75f;
        private const float TooltipParentBoost = 0.07f;
        private const float TooltipParentAlphaBoost = 0.95f;

        public static Vector4 GetChildColor(BoneData.BoneFamily family)
            => Palette.TryGetValue(family, out var color) ? color : DefaultChild;

        public static Vector4 GetParentColor(BoneData.BoneFamily family)
            => BoostColor(GetChildColor(family), ParentChannelBoost, ParentAlphaBoost);

        public static Vector4 GetTooltipColor(BoneData.BoneFamily family, bool isSource)
        {
            var baseColor = isSource ? GetParentColor(family) : GetChildColor(family);
            var channelBoost = isSource ? TooltipParentBoost : TooltipChildBoost;
            var alphaBoost = isSource ? TooltipParentAlphaBoost : TooltipChildAlphaBoost;

            return BoostColor(baseColor, channelBoost, alphaBoost);
        }

        private static Vector4 BoostColor(Vector4 color, float channelBoost, float alphaBoost)
        {
            return new Vector4(
                MathF.Min(color.X + channelBoost, 1f),
                MathF.Min(color.Y + channelBoost, 1f),
                MathF.Min(color.Z + channelBoost, 1f),
                MathF.Min(color.W + alphaBoost, 1f));
        }
    }
}