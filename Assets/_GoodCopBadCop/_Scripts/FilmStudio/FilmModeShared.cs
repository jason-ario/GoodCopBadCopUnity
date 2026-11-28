using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Names and component classification shared by the Film Studio editor tools and the Film Mode play guard.
    /// "Visual" components are the ones a film needs from the game world (meshes, lights, volumes, FX, world UI);
    /// everything else is gameplay and is stripped (actors) or switched off (Main.unity in Film Mode play).
    /// </summary>
    public static class FilmModeShared
    {
        public const string SetsRootName = "--SETS";
        public const string FilmsRootName = "--FILMS";
        public const string CameraName = "Main Camera";
        public const string LabRootName = "--GENERIC_LAB";
        public const string OccludersName = "__CameraOccluders";

        /// <summary>SessionState keys written by the editor just before Play Mode starts.</summary>
        public const string KeyFilmModePlay = "FilmStudio.FilmModePlay";
        public const string KeyOverlayPath = "FilmStudio.OverlayPath";

        /// <summary>Overlay-scene roots owned by Film Studio. Anything else in the overlay is foreign and gets suppressed.</summary>
        public static readonly HashSet<string> OverlayRootNames = new()
        {
            SetsRootName, FilmsRootName, CameraName, LabRootName, "Directional Light", "Global Volume",
        };

        private static readonly HashSet<string> VisualTypeNames = new()
        {
            "UnityEngine.Transform",
            "UnityEngine.RectTransform",
            "UnityEngine.MeshFilter",
            "UnityEngine.MeshRenderer",
            "UnityEngine.SkinnedMeshRenderer",
            "UnityEngine.SpriteRenderer",
            "UnityEngine.LineRenderer",
            "UnityEngine.TrailRenderer",
            "UnityEngine.LODGroup",
            "UnityEngine.Light",
            "UnityEngine.ReflectionProbe",
            "UnityEngine.LightProbeGroup",
            "UnityEngine.ParticleSystem",
            "UnityEngine.ParticleSystemRenderer",
            "UnityEngine.Animator",
            "UnityEngine.Animation",
            "UnityEngine.Canvas",
            "UnityEngine.CanvasRenderer",
            "UnityEngine.Rendering.Volume",
            "UnityEngine.Rendering.Universal.UniversalAdditionalLightData",
            "UnityEngine.Rendering.Universal.DecalProjector",
            "TMPro.TextMeshPro",
            "TMPro.TextMeshProUGUI",
            "UnityEngine.UI.Image",
            "UnityEngine.UI.RawImage",
            "UnityEngine.UI.Text",
            "UnityEngine.UI.CanvasScaler",
            "GoodCopBadCop.FilmStudio.StudioActor",
            "GoodCopBadCop.FilmStudio.StudioSet",
            "GoodCopBadCop.FilmStudio.StudioMark",
            "GoodCopBadCop.FilmStudio.StudioPoint",
            "GoodCopBadCop.FilmStudio.StudioCameraSpot",
            "GoodCopBadCop.FilmStudio.StudioSeeThrough",
        };

        /// <summary>True for components a film keeps (walks the base-type chain, so subclasses count).</summary>
        public static bool IsVisual(Component c, bool keepColliders)
        {
            if (c == null) return false;
            if (keepColliders && c is Collider) return true;
            for (Type type = c.GetType(); type != null && type != typeof(Component); type = type.BaseType)
                if (VisualTypeNames.Contains(type.FullName)) return true;
            return false;
        }
    }
}
