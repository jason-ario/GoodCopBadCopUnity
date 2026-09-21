using UnityEngine;
using UnityEngine.Rendering;

namespace GoodCopBadCop.Settings
{
    /// <summary>
    /// Marks the scene's persistent, always-on global post-processing Volume as the target for
    /// user-facing graphics preferences (Brightness, Film Grain, Chromatic Aberration) applied by
    /// <see cref="GraphicsPreferencesApplier"/>. Attach to the base "Post Processing" Volume
    /// GameObject — deliberately distinct from any proximity/event-driven volumes (e.g. the glitch
    /// or alert volumes), which ramp their weight/values for narrative effect and are not a
    /// reliable target for a persistent user preference.
    /// </summary>
    public sealed class GraphicsPreferencesVolumeAnchor : MonoBehaviour
    {
        [SerializeField] private Volume volume;

        public Volume Volume => volume != null ? volume : (volume = GetComponent<Volume>());
    }
}
