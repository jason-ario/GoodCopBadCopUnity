using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Everything the director (Claude) may reference. Built by StudioCatalogBuilder
    /// (Tools/Film Studio/Rebuild Catalog). IDs are stable slugs.
    /// </summary>
    [CreateAssetMenu(menuName = "Film Studio/Studio Catalog", fileName = "StudioCatalog")]
    public class StudioCatalog : ScriptableObject
    {
        public List<ActorEntry> actors = new();
        public List<SetEntry> sets = new();
        public List<SfxEntry> sfx = new();
        public List<PropEntry> props = new();

        public ActorEntry FindActor(string id) => actors.Find(a => a.actorId == id);
        public SetEntry FindSet(string id) => sets.Find(s => s.setId == id);
        public SfxEntry FindSfx(string id) => sfx.Find(s => s.sfxId == id);

        [Serializable]
        public class ActorEntry
        {
            public string actorId;
            public string displayName;
            [TextArea(1, 4)] public string bio;
            public GameObject prefab;
            public bool hasMutatedVariant;
            public bool isHumanoid;
            [Tooltip("Film-only size multiplier applied to the spawned actor (the game prefab is untouched). " +
                     "Kept across catalog rebuilds. Camera framing adapts to the scaled height.")]
            [Range(0.3f, 1.6f)] public float filmScale = 1f;
            public List<ClipEntry> clips = new();

            public ClipEntry FindClip(string clipId) => clips.Find(c => c.clipId == clipId);

            public ClipEntry FindFirstWithTag(string tag) => clips.Find(c => c.tags != null && c.tags.Contains(tag));
        }

        [Serializable]
        public class ClipEntry
        {
            public string clipId;
            public AnimationClip clip;
            public List<string> tags = new();
            public float length;
            public bool loop;
            /// <summary>"base", "mutated" or "both" — which variant's rig this clip is valid on.</summary>
            public string variant = "base";
        }

        [Serializable]
        public class SetEntry
        {
            public string setId;
            [TextArea(1, 4)] public string description;
            public List<MarkEntry> marks = new();
            public List<PointEntry> points = new();
            public List<CameraSpotEntry> cameraSpots = new();

            public bool HasMark(string id) => marks != null && marks.Exists(m => m != null && m.markId == id);
            public bool HasPoint(string id) => points != null && points.Exists(p => p != null && p.pointId == id);
            public bool HasSpot(string id) => cameraSpots != null && cameraSpots.Exists(c => c != null && c.spotId == id);
        }

        [Serializable]
        public class MarkEntry
        {
            public string markId;
            public string description;
            [TextArea(1, 4)] public string context;
            public Vector3 position;
        }

        [Serializable]
        public class PointEntry
        {
            public string pointId;
            public string kind;
            public string description;
            public Vector3 position;
            public float radius;
            public bool seeThrough;
        }

        [Serializable]
        public class CameraSpotEntry
        {
            public string spotId;
            public string description;
            public string markId;
            public bool throughGlass;
            public Vector3 position;
        }

        [Serializable]
        public class SfxEntry
        {
            public string sfxId;
            public AudioClip clip;
            public List<string> tags = new();
            public float length;
        }

        [Serializable]
        public class PropEntry
        {
            public string propId;
            public GameObject prefab;
            public string description;
        }
    }
}
