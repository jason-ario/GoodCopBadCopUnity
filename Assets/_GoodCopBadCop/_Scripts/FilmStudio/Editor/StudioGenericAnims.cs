using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GoodCopBadCop.AnimGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Generic animation support for Film Studio: retargets catalog clips onto each actor rig variant as Generic clips
    /// (cached under <see cref="FilmStudioSettings.genericCacheFolder"/>), picks standing idles for the base layer, and
    /// bakes screenplay <see cref="CustomAnim"/>s with the AnimGen Generic baker.
    /// </summary>
    public static class StudioGenericAnims
    {
        private static readonly Regex IdPattern = new Regex("^[A-Za-z0-9_\\-]+$");

        public static bool IsValidId(string id) => !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id);

        public static string RigPath(StudioCatalog.ActorEntry entry) => entry?.prefab != null ? AssetDatabase.GetAssetPath(entry.prefab) : null;

        public static string VariantName(bool mutated) => mutated ? "mutated" : "base";

        /// <summary>True if the prefab has a Humanoid Animator for this variant (required for retargeting and custom bakes).</summary>
        public static bool HasHumanoidRig(StudioCatalog.ActorEntry entry, string variant)
        {
            if (entry?.prefab == null) return false;
            bool wantMutated = GenericClipBaker.IsMutated(variant);
            foreach (Animator a in entry.prefab.GetComponentsInChildren<Animator>(true))
            {
                if (a.avatar == null || !a.avatar.isHuman) continue;
                bool underMutated = false;
                for (Transform p = a.transform; p != null; p = p.parent)
                    if (string.Equals(p.name.Trim(), HumanoidRig.MutatedContainerName, StringComparison.OrdinalIgnoreCase)) { underMutated = true; break; }
                if (underMutated == wantMutated) return true;
            }
            return false;
        }

        // ── Catalog clips → Generic ───────────────────────────────────────────────

        /// <summary>
        /// The Generic version of a catalog clip for one rig variant. Non-humanoid clips (or rigs without a Humanoid
        /// avatar for that variant) are returned unchanged. Converted clips are cached and rebuilt when the source clip
        /// or the prefab changes.
        /// </summary>
        public static AnimationClip GetGeneric(StudioCatalog.ActorEntry entry, StudioCatalog.ClipEntry ce, string variant, List<string> warnings, bool force = false)
        {
            if (ce?.clip == null) return null;
            if (!ce.clip.humanMotion || !HasHumanoidRig(entry, variant)) return ce.clip;

            string path = $"{FilmStudioSettings.instance.genericCacheFolder.TrimEnd('/')}/{entry.actorId}/{variant}/{ce.clipId}.anim";
            if (force || IsStale(path, AssetDatabase.GetAssetPath(ce.clip), RigPath(entry)))
            {
                try
                {
                    GenericClipBaker.ConvertClip(ce.clip, path, RigPath(entry), variant);
                }
                catch (Exception e)
                {
                    warnings?.Add($"Generic retarget of '{ce.clipId}' for '{entry.actorId}' ({variant}) failed: {e.Message}");
                    return null;
                }
            }
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static bool IsStale(string cachePath, params string[] sources)
        {
            if (!File.Exists(cachePath)) return true;
            DateTime cached = File.GetLastWriteTimeUtc(cachePath);
            foreach (string s in sources)
                if (!string.IsNullOrEmpty(s) && File.Exists(s) && File.GetLastWriteTimeUtc(s) > cached) return true;
            return false;
        }

        /// <summary>A standing idle for the base layer: idle-tagged, not sitting, preferring the variant's own clips.</summary>
        public static StudioCatalog.ClipEntry FindStandingIdle(StudioCatalog.ActorEntry entry, bool mutated)
        {
            if (entry?.clips == null) return null;
            string want = VariantName(mutated);
            var idles = entry.clips.Where(c => c?.clip != null && c.tags != null && c.tags.Contains("idle")).ToList();
            int Score(StudioCatalog.ClipEntry c) =>
                (c.tags.Contains("sit") || c.clipId.Contains("sit") ? 0 : 4) +
                (c.variant == want ? 2 : c.variant == "both" ? 1 : 0) +
                (c.loop ? 1 : 0);
            return idles.OrderByDescending(Score).FirstOrDefault();
        }

        // ── Custom anims ──────────────────────────────────────────────────────────

        public static AnimPlan ParsePlan(CustomAnim ca)
        {
            if (ca?.plan == null) throw new ArgumentException("plan is missing.");
            JToken token = ca.plan as JToken ?? JToken.FromObject(ca.plan);
            if (token.Type == JTokenType.String) token = JToken.Parse(token.ToString());
            AnimPlan plan = HumanoidClipWriter.Parse(token.ToString(Formatting.None));
            if (string.IsNullOrEmpty(plan.name)) plan.name = ca.id;
            return plan;
        }

        /// <summary>Resolves basePose.clip given as a catalog clipId of the actor. Returns an error string or null.</summary>
        public static string CheckBasePose(AnimPlan plan, StudioCatalog.ActorEntry entry)
        {
            string bc = plan.basePose?.clip;
            if (string.IsNullOrEmpty(bc) || bc.Contains("Assets/")) return null;
            return entry.FindClip(bc) != null ? null : $"basePose.clip '{bc}' is not a clipId of '{entry.actorId}'.";
        }

        /// <summary>
        /// Bakes one custom anim for one variant into <paramref name="filmFolder"/>/Anims. Generic mode uses the Generic
        /// baker (bones/blendShapes supported); Humanoid mode falls back to the Humanoid body baker.
        /// </summary>
        public static AnimationClip BakeCustom(CustomAnim ca, StudioCatalog.ActorEntry entry, string variant, string filmFolder,
            bool generic, List<string> warnings, out AnimPlan plan)
        {
            plan = null;
            try
            {
                plan = ParsePlan(ca);
                AnimationClip baseClip = null;
                string bc = plan.basePose?.clip;
                if (string.IsNullOrEmpty(bc))
                {
                    baseClip = FindStandingIdle(entry, GenericClipBaker.IsMutated(variant))?.clip;
                    plan.basePose ??= new BasePose { follow = true };
                }
                else if (!bc.Contains("Assets/"))
                {
                    baseClip = entry.FindClip(bc)?.clip ?? throw new ArgumentException(CheckBasePose(plan, entry));
                }

                string path = $"{filmFolder.TrimEnd('/')}/Anims/{ca.id}_{(generic ? variant : "humanoid")}.anim";
                string report;
                if (generic)
                {
                    report = GenericClipBaker.Bake(plan, path, RigPath(entry), variant, baseClip);
                }
                else
                {
                    if (baseClip != null)
                    {
                        string p = AssetDatabase.GetAssetPath(baseClip);
                        plan.basePose.clip = p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ? p : $"{p} :: {baseClip.name}";
                    }
                    report = BodyClipBaker.Bake(plan, path, RigPath(entry));
                }
                foreach (string line in report.Split('\n'))
                    if (line.StartsWith("WARNING", StringComparison.Ordinal) && !IsStraightLegNoise(report))
                        warnings?.Add($"customAnim '{ca.id}' ({variant}): {line.Trim()}");
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            catch (Exception e)
            {
                warnings?.Add($"CUSTOM ANIM FAILED '{ca.id}' ({variant}) for '{entry?.actorId}': {e.Message}");
                return null;
            }
        }

        /// <summary>Planted feet on a straight-legged idle report "clamped" every frame although nothing is wrong.</summary>
        private static bool IsStraightLegNoise(string report)
        {
            foreach (string line in report.Split('\n'))
                if (line.Contains("hand character-space") && !line.EndsWith("reach-clamped frames: 0")) return false;
            return true;
        }
    }
}
