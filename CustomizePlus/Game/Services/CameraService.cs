using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Numerics;

namespace CustomizePlus.Game.Services;

/// <summary>
/// Provides camera information to help gizmos respond to the player's view direction.
/// </summary>
public sealed class CameraService
{
    public unsafe bool TryGetCameraInfo(out CameraInfo info)
    {
        var manager = CameraManager.Instance();
        if (manager == null)
        {
            info = default;
            return false;
        }

        var camera = manager->GetActiveCamera();
        if (camera == null)
        {
            info = default;
            return false;
        }

        var viewMatrix = camera->CameraBase.SceneCamera.ViewMatrix;
        viewMatrix.M44 = 1f;

        if (!Matrix4x4.Invert(viewMatrix, out var inverseView))
        {
            info = default;
            return false;
        }

        var position = inverseView.Translation;
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, inverseView);
        if (forward.LengthSquared() <= float.Epsilon)
            forward = Vector3.UnitZ;
        else
            forward = Vector3.Normalize(forward);

        Matrix4x4.Decompose(viewMatrix, out _, out var cameraRotation, out _);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(cameraRotation);

        info = new CameraInfo(viewMatrix, rotationMatrix, position, forward);
        return true;
    }

    public readonly struct CameraInfo
    {
        public CameraInfo(Matrix4x4 viewMatrix, Matrix4x4 viewRotationMatrix, Vector3 position, Vector3 forward)
        {
            ViewMatrix = viewMatrix;
            ViewRotationMatrix = viewRotationMatrix;
            Position = position;
            Forward = forward;
        }

        public Matrix4x4 ViewMatrix { get; }

        public Matrix4x4 ViewRotationMatrix { get; }

        public Vector3 Position { get; }

        public Vector3 Forward { get; }
    }
}
