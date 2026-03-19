namespace CustomizePlus.Core.Data;

public static class BoneAttributeExtensions
{
    public static string GetShortLabel(this BoneAttribute attribute)
        => attribute switch
        {
            BoneAttribute.Position => "Pos",
            BoneAttribute.Rotation => "Rot",
            BoneAttribute.Scale => "Scale",
            _ => string.Empty,
        };

    public static string GetFriendlyName(this BoneAttribute attribute)
        => attribute switch
        {
            BoneAttribute.Position => "position",
            BoneAttribute.Rotation => "rotation",
            BoneAttribute.Scale => "scale",
            _ => "attribute",
        };
}
