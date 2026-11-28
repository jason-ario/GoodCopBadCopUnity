using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Serializes the relevant slice of a <see cref="StudioCatalog"/> into compact JSON for the director prompt.</summary>
    public static class StudioCatalogPrompt
    {
        private const int MaxChars = 40000;
        private const int BioMaxChars = 200;
        private const int OverviewClipLimit = 12;

        public static string ToPromptJson(StudioCatalog catalog, string setId, IList<string> actorIds)
        {
            if (catalog == null) return "{}";

            bool allActors = actorIds == null || actorIds.Count == 0;
            int clipLimit = allActors ? OverviewClipLimit : int.MaxValue;
            int sfxLimit = int.MaxValue;
            int actorLimit = int.MaxValue;

            // Progressively tighten limits until the JSON fits.
            for (int pass = 0; pass < 8; pass++)
            {
                string json = Build(catalog, setId, actorIds, allActors, clipLimit, sfxLimit, actorLimit).ToString(Formatting.None);
                if (json.Length <= MaxChars) return json;

                switch (pass)
                {
                    case 0: clipLimit = Math.Min(clipLimit, 24); break;
                    case 1: sfxLimit = 60; break;
                    case 2: clipLimit = Math.Min(clipLimit, 12); break;
                    case 3: sfxLimit = 30; break;
                    case 4: clipLimit = 8; actorLimit = 40; break;
                    case 5: clipLimit = 5; actorLimit = 25; break;
                    case 6: clipLimit = 3; actorLimit = 15; sfxLimit = 15; break;
                    default: return json;
                }
            }
            return Build(catalog, setId, actorIds, allActors, clipLimit, sfxLimit, actorLimit).ToString(Formatting.None);
        }

        private static JObject Build(StudioCatalog catalog, string setId, IList<string> actorIds, bool allActors,
            int clipLimit, int sfxLimit, int actorLimit)
        {
            var root = new JObject();

            // Sets
            var sets = new JArray();
            if (catalog.sets != null)
            {
                foreach (var set in catalog.sets)
                {
                    if (set == null) continue;
                    if (!string.IsNullOrEmpty(setId) && set.setId != setId) continue;
                    var marks = new JArray();
                    if (set.marks != null)
                    {
                        foreach (var m in set.marks)
                        {
                            if (m == null) continue;
                            var mj = new JObject { ["id"] = m.markId ?? "", ["desc"] = m.description ?? "" };
                            if (!string.IsNullOrEmpty(m.context)) mj["context"] = m.context;
                            marks.Add(mj);
                        }
                    }
                    var points = new JArray();
                    if (set.points != null)
                    {
                        foreach (var p in set.points)
                        {
                            if (p == null) continue;
                            var pj = new JObject { ["id"] = p.pointId ?? "", ["kind"] = p.kind ?? "prop", ["desc"] = p.description ?? "" };
                            if (p.seeThrough) pj["seeThrough"] = true;
                            points.Add(pj);
                        }
                    }
                    var spots = new JArray();
                    if (set.cameraSpots != null)
                        foreach (var c in set.cameraSpots)
                            if (c != null) spots.Add(new JObject { ["id"] = c.spotId ?? "", ["desc"] = c.description ?? "" });

                    var sj = new JObject
                    {
                        ["id"] = set.setId ?? "",
                        ["description"] = set.description ?? "",
                        ["marks"] = marks
                    };
                    if (points.Count > 0) sj["points"] = points;
                    if (spots.Count > 0) sj["cameraSpots"] = spots;
                    sets.Add(sj);
                }
            }
            root["sets"] = sets;

            // Actors
            var actors = new JArray();
            if (catalog.actors != null)
            {
                int added = 0;
                foreach (var a in catalog.actors)
                {
                    if (a == null) continue;
                    if (!allActors && !actorIds.Contains(a.actorId)) continue;
                    if (added >= actorLimit) break;

                    var clips = new JArray();
                    if (a.clips != null)
                    {
                        int n = 0;
                        foreach (var c in a.clips)
                        {
                            if (c == null) continue;
                            if (n >= clipLimit) break;
                            clips.Add(ClipJson(c));
                            n++;
                        }
                    }

                    var aj = new JObject
                    {
                        ["id"] = a.actorId ?? "",
                        ["displayName"] = a.displayName ?? "",
                        ["bio"] = Truncate(a.bio, BioMaxChars),
                        ["hasMutatedVariant"] = a.hasMutatedVariant,
                        ["clips"] = clips
                    };
                    if (a.clips != null && a.clips.Count > clips.Count)
                        aj["clipsOmitted"] = a.clips.Count - clips.Count;
                    if (!allActors)
                    {
                        JObject heights = Heights(a);
                        if (heights != null) aj["heightM"] = heights;
                    }
                    if (!allActors && FilmStudioSettings.instance.genericAnimation)
                    {
                        JObject rig = RigInfo(a);
                        if (rig != null) aj["rig"] = rig;
                    }
                    actors.Add(aj);
                    added++;
                }
            }
            root["actors"] = actors;

            // SFX
            var sfx = new JArray();
            if (catalog.sfx != null)
            {
                int n = 0;
                foreach (var s in catalog.sfx)
                {
                    if (s == null) continue;
                    if (n >= sfxLimit) break;
                    sfx.Add(new JObject
                    {
                        ["id"] = s.sfxId ?? "",
                        ["tags"] = new JArray(s.tags ?? new List<string>()),
                        ["len"] = Round1(s.length)
                    });
                    n++;
                }
            }
            root["sfx"] = sfx;

            return root;
        }

        private static readonly Dictionary<string, (DateTime stamp, JObject info)> RigCache = new();

        /// <summary>
        /// Cast actors only (Generic mode): per-variant proportions in the standing idle, extra bone chains and blendshapes,
        /// so the director can write customAnims in the rig's own units. Cached per prefab write time.
        /// </summary>
        private static JObject RigInfo(StudioCatalog.ActorEntry a)
        {
            string rigPath = StudioGenericAnims.RigPath(a);
            if (string.IsNullOrEmpty(rigPath)) return null;
            DateTime stamp = System.IO.File.Exists(rigPath) ? System.IO.File.GetLastWriteTimeUtc(rigPath) : DateTime.MinValue;
            if (RigCache.TryGetValue(a.actorId, out var cached) && cached.stamp == stamp) return cached.info;

            var info = new JObject();
            foreach (bool mutated in a.hasMutatedVariant ? new[] { false, true } : new[] { false })
            {
                string variant = StudioGenericAnims.VariantName(mutated);
                if (!StudioGenericAnims.HasHumanoidRig(a, variant)) { info[variant] = "no Humanoid-mapped rig: customAnims not supported"; continue; }
                try
                {
                    AnimationClip idle = StudioGenericAnims.FindStandingIdle(a, mutated)?.clip;
                    info[variant] = JObject.Parse(AnimGen.GenericClipBaker.DescribeRig(rigPath, variant, idle));
                }
                catch (Exception e) { info[variant] = $"rig info failed: {e.Message}"; }
            }
            RigCache[a.actorId] = (stamp, info);
            return info;
        }

        private static readonly Dictionary<string, (DateTime stamp, float scale, JObject info)> HeightCache = new();

        /// <summary>
        /// Cast actors only: on-screen standing height per variant (top of the head above the feet, metres, including the
        /// film-only scale), measured once per prefab write time from the prefab's renderers.
        /// </summary>
        private static JObject Heights(StudioCatalog.ActorEntry a)
        {
            string path = StudioGenericAnims.RigPath(a);
            if (string.IsNullOrEmpty(path)) return null;
            float scale = a.filmScale > 0.01f ? a.filmScale : 1f;
            DateTime stamp = System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (HeightCache.TryGetValue(a.actorId, out var cached) && cached.stamp == stamp && Mathf.Approximately(cached.scale, scale)) return cached.info;

            JObject info = null;
            GameObject go = null;
            try
            {
                go = UnityEditor.PrefabUtility.LoadPrefabContents(path);
                info = new JObject();
                foreach (string v in new[] { "Base Version", "Mutated Version" })
                {
                    Transform t = null;
                    foreach (Transform c in go.GetComponentsInChildren<Transform>(true)) if (c.name.Trim() == v) { t = c; break; }
                    if (t == null && v == "Base Version") t = go.transform;
                    if (t == null) continue;
                    Bounds b = default;
                    bool any = false;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is ParticleSystemRenderer || !r.enabled) continue;
                        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    }
                    if (!any) continue;
                    float h = (b.max.y - go.transform.position.y) * scale;
                    if (h > 0.3f && h < 8f) info[v.StartsWith("Base") ? "base" : "mutated"] = Round1(h);
                }
                if (!info.HasValues) info = null;
            }
            catch (Exception) { info = null; }
            finally { if (go != null) UnityEditor.PrefabUtility.UnloadPrefabContents(go); }

            HeightCache[a.actorId] = (stamp, scale, info);
            return info;
        }

        private static JObject ClipJson(StudioCatalog.ClipEntry c) => new JObject
        {
            ["id"] = c.clipId ?? "",
            ["tags"] = new JArray(c.tags ?? new List<string>()),
            ["len"] = Round1(c.length),
            ["loop"] = c.loop,
            ["variant"] = string.IsNullOrEmpty(c.variant) ? "base" : c.variant
        };

        private static float Round1(float v) => (float)Math.Round(v, 1);

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
