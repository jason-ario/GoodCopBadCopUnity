using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Visual-only film actor spawned by the Film Studio (no networking, no gameplay scripts).
    /// Holds the base/mutated art containers and the anchors cameras frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class StudioActor : MonoBehaviour
    {
        // ── Settings ──────────────────────────────────────────────────────────────
        public string actorId;
        public string displayName;

        [Tooltip("The 'Base Version' container (or the whole art if the prefab has no variants).")]
        public GameObject baseArt;

        [Tooltip("The 'Mutated Version' container. Optional.")]
        public GameObject mutatedArt;

        public Transform headAnchor;
        public Transform chestAnchor;
        public Transform mutatedHeadAnchor;
        public Transform mutatedChestAnchor;

        [Tooltip("Centre of the face (between the head bone and the top of the head), parented to the head bone. " +
                 "Head bones sit at the skull base, so big-headed rigs framed on them show only chins.")]
        public Transform faceAnchor;
        public Transform mutatedFaceAnchor;

        [Tooltip("Standing height above the actor root (top of the head, world metres incl. scale), per variant.")]
        public float height;
        public float mutatedHeight;

        [Tooltip("Renderers that were enabled on spawn, per variant. Variant swaps toggle these instead of " +
                 "deactivating GameObjects so both Animators stay bound for Timeline preview and recording.")]
        public List<Renderer> baseRenderers = new();
        public List<Renderer> mutatedRenderers = new();

        [Tooltip("Generic animation mode: Animators have no avatar and play per-rig Generic clips.")]
        public bool genericRig;

        // ── Runtime state ─────────────────────────────────────────────────────────
        private bool _isMutated;

        public bool HasMutatedArt => mutatedArt != null;
        public bool IsMutated => _isMutated;

        /// <summary>The Animator of the currently visible variant.</summary>
        public Animator ActiveAnimator
        {
            get
            {
                GameObject art = _isMutated && mutatedArt != null ? mutatedArt : baseArt;
                return art != null ? art.GetComponentInChildren<Animator>(true) : GetComponentInChildren<Animator>(true);
            }
        }

        public Animator BaseAnimator => baseArt != null ? baseArt.GetComponentInChildren<Animator>(true) : null;
        public Animator MutatedAnimator => mutatedArt != null ? mutatedArt.GetComponentInChildren<Animator>(true) : null;

        public void SetVariant(bool mutated)
        {
            _isMutated = mutated && mutatedArt != null;
            if (baseRenderers.Count == 0 && mutatedRenderers.Count == 0)
            {
                // Legacy actors without cached renderers.
                if (baseArt != null) baseArt.SetActive(!_isMutated);
                if (mutatedArt != null) mutatedArt.SetActive(_isMutated);
                return;
            }
            foreach (Renderer r in baseRenderers) if (r != null) r.enabled = !_isMutated;
            foreach (Renderer r in mutatedRenderers) if (r != null) r.enabled = _isMutated;
        }

        /// <summary>Anchor on the requested variant's skeleton (falls back to the base anchor).</summary>
        public Transform GetAnchor(string anchorName, bool mutated = false)
        {
            Transform head = mutated && mutatedHeadAnchor != null ? mutatedHeadAnchor : headAnchor;
            Transform chest = mutated && mutatedChestAnchor != null ? mutatedChestAnchor : chestAnchor;
            Transform face = mutated && mutatedFaceAnchor != null ? mutatedFaceAnchor : faceAnchor;
            switch (anchorName)
            {
                case "face": return face != null ? face : head != null ? head : transform;
                case "head": return head != null ? head : transform;
                case "chest": return chest != null ? chest : transform;
                default: return transform;
            }
        }

        /// <summary>Standing height of a variant (falls back to the base height).</summary>
        public float GetHeight(bool mutated) => mutated && mutatedHeight > 0.1f ? mutatedHeight : height;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (headAnchor != null) Gizmos.DrawWireSphere(headAnchor.position, 0.12f);
            if (chestAnchor != null) Gizmos.DrawWireSphere(chestAnchor.position, 0.18f);
        }
    }
}
