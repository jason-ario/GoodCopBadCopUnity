using TMPro;
using UnityEngine;

/// <summary>
/// Writes <see cref="GameSettings.GetVersionDisplayString"/> (prefix + Application.version + channel)
/// into a TMP text. The version number is bumped automatically on each build by BuildVersionPreprocessor.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class VersionLabel : MonoBehaviour
{
    private void OnEnable()
    {
        GetComponent<TMP_Text>().text = GameSettings.Instance.GetVersionDisplayString();
    }
}
