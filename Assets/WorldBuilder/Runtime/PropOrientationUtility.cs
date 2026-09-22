using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Resolves a <see cref="PropOrientationMode"/> into an actual local rotation, using the exact same
    /// formulas this project's existing by-hand tool
    /// (<c>Assets/Scripts/Editor/ScreenAlignmentTools.cs</c>) already applies via its "Face Main Camera" /
    /// "Lay Flat on XZ" / "Level World Axes" commands, so Phase 3 placement is indistinguishable from
    /// that established, already-verified-in-Editor workflow -- just automatic instead of a manual menu
    /// click per object.
    /// </summary>
    public static class PropOrientationUtility
    {
        public static Quaternion ResolveRotation(PropDefinition definition, Camera referenceCamera, Vector3 worldPosition)
        {
            if (definition == null) return Quaternion.identity;

            switch (definition.defaultOrientation)
            {
                case PropOrientationMode.Vertical:
                    // ScreenAlignmentTools "Level World Axes".
                    return Quaternion.identity;

                case PropOrientationMode.Horizontal:
                    // ScreenAlignmentTools "Lay Flat on XZ".
                    return Quaternion.Euler(-90f, 0f, 0f);

                case PropOrientationMode.Billboard:
                    return ResolveBillboardRotation(referenceCamera, worldPosition);

                case PropOrientationMode.Custom:
                    return Quaternion.Euler(definition.customEulerAngles);

                default:
                    return Quaternion.identity;
            }
        }

        private static Quaternion ResolveBillboardRotation(Camera referenceCamera, Vector3 worldPosition)
        {
            if (referenceCamera == null) return Quaternion.identity;

            // ScreenAlignmentTools "Face Main Camera": look from the prop toward the camera, upright.
            Vector3 forward = worldPosition - referenceCamera.transform.position;
            if (forward.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
