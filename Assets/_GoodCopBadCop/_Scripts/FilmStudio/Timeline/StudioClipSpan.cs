using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Lightweight snapshot of a TimelineClip (start, end, asset) handed to Film Studio track mixers
    /// so they can evaluate deterministically from the track's initial state.
    /// </summary>
    public struct StudioClipSpan
    {
        public double start;
        public double end;
        public PlayableAsset asset;

        public StudioClipSpan(double start, double end, PlayableAsset asset)
        {
            this.start = start;
            this.end = end;
            this.asset = asset;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        /// <summary>Collects the track's clips sorted by start time.</summary>
        public static List<StudioClipSpan> FromTrack(TrackAsset track)
        {
            var spans = new List<StudioClipSpan>();
            if (track == null) return spans;

            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip == null) continue;
                spans.Add(new StudioClipSpan(clip.start, clip.end, clip.asset as PlayableAsset));
            }

            spans.Sort((a, b) => a.start.CompareTo(b.start));
            return spans;
        }

        /// <summary>Normalized [0,1] progress of time inside this span.</summary>
        public float Normalized(double time)
        {
            double duration = end - start;
            if (duration <= 0.0) return 1f;
            return Mathf.Clamp01((float)((time - start) / duration));
        }
    }
}
