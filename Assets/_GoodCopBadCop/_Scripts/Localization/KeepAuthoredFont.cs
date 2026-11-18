using TMPro;
using UnityEngine;

/// <summary>
/// Opts a decorative TMP label (separator lines, ornaments, digit-only counters) out of the per-language font swap
/// done by <see cref="LocalizationTextRuntime"/>. Glyphs such as '_' differ in height and width between language
/// fonts, so swapping them breaks layouts that were tuned against the authored font.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class KeepAuthoredFont : MonoBehaviour
{
    private void Awake()
    {
        LocalizationTextRuntime.KeepOwnFont(GetComponent<TMP_Text>());
    }
}
