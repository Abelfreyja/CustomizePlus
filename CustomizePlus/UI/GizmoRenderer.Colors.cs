using CustomizePlus.Core.Data;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace CustomizePlus.UI;

/// <summary> just color helpers for <see cref="GizmoRenderer"/>.</summary>
public sealed partial class GizmoRenderer
{
    private static class GizmoColors
    {
        private static readonly Vector4[] AxisPalette =
        {
            new(0.95f, 0.3f, 0.3f, 1f),
            new(0.4f, 0.85f, 0.45f, 1f),
            new(0.35f, 0.6f, 1f, 1f),
        };

        private static readonly Vector4[] AttributeWheelPalette =
        {
            new(0.95f, 0.55f, 0.35f, 1f),
            new(0.5f, 0.8f, 1f, 1f),
            new(0.5f, 0.9f, 0.6f, 1f),
        };

        public static Vector4 TranslationDragActive => new(1f, 0.65f, 0.2f, 1f);
        public static Vector4 TranslationDragSuppressed => new(0.55f, 0.55f, 0.58f, 0.25f);
        public static Vector4 TranslationDragPath => new(0.75f, 0.75f, 0.8f, 0.9f);
        public static Vector4 RotationDragHighlight => new(1f, 0.7f, 0.3f, 0.75f);
        public static Vector4 RotationBackground => new(0f, 0f, 0f, RotationBackgroundAlpha);
        public static Vector4 CenterGlow => new(0f, 0f, 0f, CenterGlowOpacity);
        public static Vector4 CenterHandleHighlight => new(1f, 1f, 1f, 0.45f);
        public static Vector4 CenterHandleFill => new(1f, 1f, 1f, 0.95f);
        public static Vector4 CenterHandleOuterRing => new(0.1f, 0.1f, 0.1f, 0.85f);
        public static Vector4 ScaleCenterFill => new(0.92f, 0.92f, 0.95f, 0.9f);
        public static Vector4 ScaleCenterBorder => new(0.15f, 0.15f, 0.2f, 0.9f);
        public static Vector4 ScaleCenterHighlight => new(1f, 1f, 1f, 0.4f);
        public static Vector4 ModifierIndicator => new(1f, 1f, 1f, 0.55f);
        public static Vector4 DragMetricsBackground => new(0f, 0f, 0f, 0.55f);
        public static Vector4 WheelBackground => new(0.11f, 0.11f, 0.11f, 1f);
        public static Vector4 WheelOutline => new(0.1f, 0.1f, 0.1f, 1f);
        public static Vector4 WheelMask => new(0f, 0f, 0f, 1f);
        public static Vector4 WheelCenterPrimary => new(0.2f, 0.2f, 0.3f, 1f);
        public static Vector4 WheelCenterSecondary => new(0.3f, 0.55f, 0.95f, 1f);
        public static Vector4 WheelIconActive => new(0f, 0f, 0f, 0.95f);
        public static Vector4 WheelIconInactive => new(1f, 1f, 1f, 0.95f);
        public static Vector4 PropagationDefault => new(0.55f, 0.85f, 0.65f, 1f);
        public static Vector4 RadialTooltipBackground => new(0.08f, 0.08f, 0.08f, 0.95f);
        public static Vector4 RadialTooltipBorder => new(0.35f, 0.35f, 0.35f, 1f);
        public static Vector4 CenterSelectorDeadZoneHovered => new(1f, 1f, 1f, 0.55f);
        public static Vector4 CenterSelectorDeadZoneIdle => new(1f, 1f, 1f, 0.22f);
        public static Vector4 CenterSelectorCenterFill => new(1f, 1f, 1f, 0.92f);
        public static Vector4 WheelLocalSpace => new(0.75f, 0.75f, 0.9f, 1f);
        public static Vector4 WheelWorldSpace => new(0.55f, 0.75f, 1f, 1f);
        public static Vector4 WheelMirrorMode => new(0.85f, 0.6f, 0.95f, 1f);
        public static Vector4 WheelUndo => new(0.35f, 0.75f, 0.95f, 1f);
        public static Vector4 WheelRedo => new(0.95f, 0.55f, 0.2f, 1f);
        public static Vector4 WheelFavorite => new(0.9f, 0.8f, 0.35f, 1f);
        public static Vector4 WheelReset => new(0.5f, 0.7f, 0.95f, 1f);
        public static Vector4 WheelRevert => new(0.65f, 0.55f, 0.95f, 1f);

        public static Vector4 AxisLine(GizmoAxis axis, bool isActive, bool isHovered)
        {
            var baseColor = TryGetAxisBase(axis, out var axisColor)
                ? axisColor
                : ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.Text));
            var intensity = isActive ? 1.35f : isHovered ? 1.15f : 0.95f;
            return WithAlpha(ScaleRgb(baseColor, intensity), 0.95f);
        }

        public static uint AxisGlowU32(GizmoAxis axis, bool isActive)
        {
            if (!TryGetAxisBase(axis, out var baseColor))
                return ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.2f));

            return ImGui.GetColorU32(WithAlpha(baseColor, isActive ? 0.45f : 0.25f));
        }

        public static uint AxisBackgroundU32(GizmoAxis axis, bool isActive)
        {
            if (!TryGetAxisBase(axis, out var baseColor))
                return ImGui.GetColorU32(ImGuiCol.TextDisabled);

            return ImGui.GetColorU32(WithAlpha(baseColor, isActive ? 0.45f : 0.2f));
        }

        public static Vector4 TranslationDragGlow(Vector4 axisColor)
            => WithAlpha(axisColor, 0.45f);

        public static Vector4 AxisArrowBorder(Vector4 axisColor)
            => WithAlpha(axisColor, MathF.Min(axisColor.W + 0.15f, 1f));

        public static Vector4 ScaleHandleFill(Vector4 axisColor, bool highlight)
            => WithAlpha(axisColor, highlight ? 0.95f : 0.65f);

        public static Vector4 ScaleHandleBorder(Vector4 axisColor)
            => WithAlpha(axisColor, 0.95f);

        public static Vector4 AxisLabelBackground(Vector4 axisColor, bool highlighted)
            => WithAlpha(axisColor, highlighted ? 0.95f : 0.7f);

        public static Vector4 AxisLabelOutline(Vector4 axisColor)
            => WithAlpha(axisColor, 0.95f);

        public static Vector4 AxisLabelText(bool highlighted)
            => highlighted ? new Vector4(0f, 0f, 0f, 0.95f) : new Vector4(0.05f, 0.05f, 0.05f, 0.9f);

        public static Vector4 WheelAttribute(BoneAttribute attribute)
            => attribute switch
            {
                BoneAttribute.Position => AttributeWheelPalette[0],
                BoneAttribute.Rotation => AttributeWheelPalette[1],
                BoneAttribute.Scale => AttributeWheelPalette[2],
                _ => AttributeWheelPalette[0],
            };

        public static Vector4 WheelCenterButton(bool secondaryPage)
            => secondaryPage ? WheelCenterSecondary : WheelCenterPrimary;

        public static Vector4 WheelCenterIcon(bool secondaryPage)
            => secondaryPage ? WheelIconActive : WheelIconInactive;

        public static Vector4 WheelSegmentIcon(bool isActive)
            => isActive ? WheelIconActive : WheelIconInactive;

        public static Vector4 WheelFill(Vector4 accentColor, bool active, bool hovered)
        {
            if (!active)
            {
                if (hovered)
                {
                    const float dim = 0.25f;
                    return new Vector4(
                        MathF.Min(accentColor.X * dim, 0.35f),
                        MathF.Min(accentColor.Y * dim, 0.35f),
                        MathF.Min(accentColor.Z * dim, 0.35f),
                        1f);
                }

                return WheelBackground;
            }

            return new Vector4(
                MathF.Min(accentColor.X * 1.05f, 1f),
                MathF.Min(accentColor.Y * 1.05f, 1f),
                MathF.Min(accentColor.Z * 1.05f, 1f),
                1f);
        }

        public static Vector4 CenterSelectorConnector(bool isParent, bool isHovered)
            => isParent
                ? new Vector4(0.52f, 0.72f, 0.96f, isHovered ? 0.9f : 0.42f)
                : new Vector4(0.48f, 0.86f, 0.62f, isHovered ? 0.9f : 0.42f);

        public static Vector4 CenterSelectorBackground(bool isParent, bool isHovered)
            => isParent
                ? new Vector4(0.12f, 0.17f, 0.24f, isHovered ? 0.98f : 0.92f)
                : new Vector4(0.11f, 0.2f, 0.16f, isHovered ? 0.98f : 0.92f);

        public static Vector4 CenterSelectorBorder(bool isParent, bool isHovered)
            => isParent
                ? new Vector4(0.36f, 0.56f, 0.78f, isHovered ? 1f : 0.86f)
                : new Vector4(0.34f, 0.66f, 0.5f, isHovered ? 1f : 0.86f);

        public static Vector4 CenterSelectorText(bool isParent)
            => isParent
                ? new Vector4(0.9f, 0.95f, 1f, 0.98f)
                : new Vector4(0.9f, 1f, 0.94f, 0.98f);

        private static bool TryGetAxisBase(GizmoAxis axis, out Vector4 color)
        {
            var index = AxisToIndex(axis);
            if (index < 0)
            {
                color = default;
                return false;
            }

            color = AxisPalette[index];
            return true;
        }

        private static Vector4 ScaleRgb(Vector4 color, float intensity)
            => new(
                MathF.Min(color.X * intensity, 1f),
                MathF.Min(color.Y * intensity, 1f),
                MathF.Min(color.Z * intensity, 1f),
                color.W);

        private static Vector4 WithAlpha(Vector4 color, float alpha)
            => new(color.X, color.Y, color.Z, alpha);
    }
}
