using System;
using System.Collections.Generic;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Claude-authored short film description. Mirrors the <c>submit_screenplay</c> tool schema exactly
    /// (see ClaudeDirectorClient). All times inside a <see cref="Shot"/> are relative to the shot start.
    /// </summary>
    [Serializable]
    public class Screenplay
    {
        public string title = "Untitled";
        public string logline = "";
        public string hook = "";
        public string setId = "";
        public int fps = 30;
        public float durationSec;
        public List<CastEntry> cast = new();
        public List<Shot> shots = new();
        /// <summary>Animations authored for this short (AnimPlan JSON), baked per actor rig when the film is built.
        /// Referenced from anim actions by <see cref="CustomAnim.id"/> like a catalog clipId.</summary>
        public List<CustomAnim> customAnims = new();

        /// <summary>Recomputes every shot start from the shot order and returns the total duration.</summary>
        public float RecomputeTiming()
        {
            float t = 0f;
            foreach (Shot shot in shots)
            {
                shot.start = t;
                t += shot.duration;
            }
            durationSec = t;
            return t;
        }

        public Shot FindShot(string id) => shots.Find(s => s.id == id);
    }

    [Serializable]
    public class CustomAnim
    {
        /// <summary>Unique id, used as clipId in anim actions (must not clash with the actor's catalog clipIds).</summary>
        public string id = "";
        public string actorId = "";
        /// <summary>"base", "mutated" or "both" — which variant rig(s) to bake for.</summary>
        public string variant = "base";
        public string description = "";
        /// <summary>AnimPlan object (full-body AnimGen plan + Generic bones/blendShapes). Kept as raw JSON
        /// (Newtonsoft turns it into a JObject) so the runtime assembly doesn't depend on the editor-only AnimGen types.</summary>
        public object plan;
    }

    [Serializable]
    public class CastEntry
    {
        public string actorId = "";
        public string role = "";
        public string startMarkId = "";
        /// <summary>"base" or "mutated".</summary>
        public string variant = "base";
    }

    [Serializable]
    public class Shot
    {
        public string id = "";
        public float start;
        public float duration = 2f;
        public ShotCamera camera = new();
        public List<ActorAction> actions = new();
        public List<SfxCue> sfx = new();
        public string caption = "";
        public float speed = 1f;
        public string notes = "";
        public bool locked;
    }

    [Serializable]
    public class ShotCamera
    {
        /// <summary>extreme_wide | wide | medium | close_up | extreme_close_up | over_shoulder | pov | insert</summary>
        public string framing = "medium";
        public string subjectActorId = "";
        public string secondaryActorId = "";
        /// <summary>Vertical field of view in degrees. 0 = framing preset default.</summary>
        public float fov;
        /// <summary>static | dolly_in | dolly_out | orbit_left | orbit_right | crane_up | crane_down | truck_left | truck_right | whip_pan | crash_zoom</summary>
        public string move = "static";
        /// <summary>0..1 move strength.</summary>
        public float moveAmount = 0.5f;
        public float dutch;
        /// <summary>0..1 handheld noise gain.</summary>
        public float shake;
        /// <summary>cut | ease</summary>
        public string blendIn = "cut";
        /// <summary>eye_level | low | high | overhead | ground. Empty = eye_level.</summary>
        public string angle = "";
        /// <summary>Optional set camera spot id: the camera stands there and the lens is fitted to the framing.</summary>
        public string spotId = "";
        /// <summary>Optional point/mark id to frame instead of (or without) an actor, e.g. an insert of the table bell.</summary>
        public string targetId = "";
        /// <summary>Optional point id placed between camera and subject (shoot through the window glass, past the bell).</summary>
        public string foregroundId = "";
    }

    [Serializable]
    public class ActorAction
    {
        public string actorId = "";
        public float t;
        /// <summary>anim | move | face | variant_swap | hide | show</summary>
        public string type = "anim";
        public string clipId = "";
        public string toMarkId = "";
        /// <summary>For face: an actorId, markId or pointId.</summary>
        public string faceTargetId = "";
        /// <summary>Metres per second for move actions. 0 = default walk speed.</summary>
        public float moveSpeed;
    }

    [Serializable]
    public class SfxCue
    {
        public float t;
        public string sfxId = "";
        public float volume = 1f;
    }

    public static class ScreenplayVocabulary
    {
        public static readonly string[] Framings =
            { "extreme_wide", "wide", "medium", "close_up", "extreme_close_up", "over_shoulder", "pov", "insert" };

        public static readonly string[] Moves =
            { "static", "dolly_in", "dolly_out", "orbit_left", "orbit_right", "crane_up", "crane_down",
              "truck_left", "truck_right", "whip_pan", "crash_zoom" };

        public static readonly string[] ActionTypes = { "anim", "move", "face", "variant_swap", "hide", "show" };

        public static readonly string[] Blends = { "cut", "ease" };

        public static readonly string[] Angles = { "eye_level", "low", "high", "overhead", "ground" };

        public static readonly string[] Variants = { "base", "mutated" };

        public static readonly string[] AnimVariants = { "base", "mutated", "both" };
    }
}
