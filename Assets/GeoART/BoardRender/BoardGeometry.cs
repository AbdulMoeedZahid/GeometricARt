using GeoART.Contracts;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Board-space &lt;-&gt; board-local &lt;-&gt; screen math shared by ARBoardSpace and FlatBoardSpace (P2 4.3).
    /// World units are metres, Y is up. The board lies in its transform's local XZ plane:
    /// board (u,v) maps to local (s*(u-0.5), 0, s*(v-0.5)), so +u = local +X (user's right) and
    /// +v = local +Z (away from the user at placement).
    /// </summary>
    public static class BoardGeometry
    {
        /// <summary>D-04: 40 cm square board.</summary>
        public const float DefaultSideMeters = 0.40f;

        /// <summary>Reject rays within about 1.1 degrees of grazing the board; their hit points are unstable.</summary>
        public const float MinRayPlaneCos = 0.02f;

        public static Vector3 BoardToLocal(Vec2 uv, float side, float lift = 0f) =>
            new Vector3((float)((uv.X - 0.5) * side), lift, (float)((uv.Y - 0.5) * side));

        public static Vec2 LocalToBoard(Vector3 local, float side) =>
            new Vec2(local.x / side + 0.5, local.z / side + 0.5);

        public static bool IsInside(Vec2 uv) => uv.X >= 0.0 && uv.X <= 1.0 && uv.Y >= 0.0 && uv.Y <= 1.0;

        /// <summary>Ray against the infinite board plane. False for near-parallel rays and hits behind the ray origin.</summary>
        public static bool TryRayToBoardPlane(Ray worldRay, Transform board, float side, out Vec2 uv)
        {
            uv = default;
            if (board == null || side <= 0f) return false;

            Vector3 normal = board.up;
            Vector3 dir = worldRay.direction.normalized;
            float denom = Vector3.Dot(normal, dir);
            if (Mathf.Abs(denom) < MinRayPlaneCos) return false;

            float t = Vector3.Dot(board.position - worldRay.origin, normal) / denom;
            if (t <= 0f) return false;

            Vector3 hit = worldRay.origin + dir * t;
            uv = LocalToBoard(board.InverseTransformPoint(hit), side);
            return uv.IsFinite;
        }

        /// <summary>Like <see cref="TryRayToBoardPlane"/>, but also false when the hit is outside the board.</summary>
        public static bool TryRayToBoard(Ray worldRay, Transform board, float side, out Vec2 uv) =>
            TryRayToBoardPlane(worldRay, board, side, out uv) && IsInside(uv);

        public static bool TryScreenToBoard(Camera cam, Transform board, float side, Vec2 screenPx, out Vec2 uv)
        {
            uv = default;
            if (cam == null || !screenPx.IsFinite) return false;
            Ray ray = cam.ScreenPointToRay(new Vector3((float)screenPx.X, (float)screenPx.Y, 0f));
            return TryRayToBoard(ray, board, side, out uv);
        }

        public static bool TryBoardToScreen(Camera cam, Transform board, float side, Vec2 uv, out Vec2 screenPx)
        {
            screenPx = default;
            if (cam == null || board == null || !uv.IsFinite) return false;
            Vector3 world = board.TransformPoint(BoardToLocal(uv, side));
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= cam.nearClipPlane) return false;
            screenPx = new Vec2(sp.x, sp.y);
            return true;
        }

        /// <summary>
        /// Board rotation for a surface with normal <paramref name="planeUp"/>: local +Z (board +v) points away from
        /// the camera along the surface, so the bottom edge faces the user and +u is to their right.
        /// </summary>
        public static Quaternion FacingRotation(Vector3 planeUp, Vector3 cameraForward)
        {
            Vector3 up = planeUp.sqrMagnitude > 1e-8f ? planeUp.normalized : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, up);   // looking straight down: any stable yaw
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            return Quaternion.LookRotation(forward.normalized, up);
        }
    }
}
