using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    public static class VolumeUtilities
    {
        private static readonly Vector3[] s_PointsOS = new Vector3[8]
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, +0.5f, -0.5f),
            new Vector3(+0.5f, +0.5f, -0.5f),
            new Vector3(+0.5f, -0.5f, -0.5f),

            new Vector3(-0.5f, -0.5f, +0.5f),
            new Vector3(-0.5f, +0.5f, +0.5f),
            new Vector3(+0.5f, +0.5f, +0.5f),
            new Vector3(+0.5f, -0.5f, +0.5f)
        };

        private static Vector3[] s_TempPointsWS = new Vector3[8];

        /// <summary>
        /// Get the 8 corners of a volume in world space
        /// </summary>
        /// <param name="pointsWS">Pre-allocated array storing 8 points</param>
        /// <param name="localToWorldMatrix">The volume's transfromation matrix</param>
        public static void GetWorldPointsNonAlloc(Vector3[] pointsWS, Matrix4x4 localToWorldMatrix)
        {
            if (pointsWS == null || pointsWS.Length != 8)
                throw new System.ArgumentException($"{nameof(pointsWS)} length must be 8");

            for (int i = 0; i < 8; ++i)
            {
                pointsWS[i] = localToWorldMatrix.MultiplyPoint(s_PointsOS[i]);
            }
        }

        /// <summary>
        /// Check if a volume is visible by a camera
        /// </summary>
        /// <param name="planes">The camera's frustum planes</param>
        /// <param name="localToWorldMatrix">The volume's transfromation matrix</param>
        /// <returns><see langword="true"/> if the volume is visible</returns>
        public static bool IsVisible(Plane[] planes, Matrix4x4 localToWorldMatrix)
        {
            GetWorldPointsNonAlloc(s_TempPointsWS, localToWorldMatrix);
            return IsVisible(planes, s_TempPointsWS);
        }

        /// <summary>
        /// Check if a volume is visible by a camera
        /// </summary>
        /// <param name="planes">The camera's frustum planes</param>
        /// <param name="pointsWS">The 8 corners of the volume</param>
        /// <returns></returns>
        public static bool IsVisible(Plane[] planes, Vector3[] pointsWS)
        {
            //If all points are on the back side of a plane, it's not visible
            if (IsAllBehindPlane(planes[0], pointsWS)) return false;
            if (IsAllBehindPlane(planes[1], pointsWS)) return false;
            if (IsAllBehindPlane(planes[2], pointsWS)) return false;
            if (IsAllBehindPlane(planes[3], pointsWS)) return false;
            if (IsAllBehindPlane(planes[4], pointsWS)) return false;
            if (IsAllBehindPlane(planes[5], pointsWS)) return false;

            //Otherwise fully/partially visible
            return true;
        }

        /// <summary>
        /// Check if all points in an array is behind a plane
        /// </summary>
        /// <param name="plane">The plane</param>
        /// <param name="pointsWS">The points array</param>
        /// <returns>True if all points are behind the plane</returns>
        private static bool IsAllBehindPlane(Plane plane, Vector3[] pointsWS)
        {
            for (int i = 0; i < pointsWS.Length; ++i)
            {
                if (plane.GetSide(pointsWS[i]) == true)
                    return false;
            }

            return true;
        }
    }
}
