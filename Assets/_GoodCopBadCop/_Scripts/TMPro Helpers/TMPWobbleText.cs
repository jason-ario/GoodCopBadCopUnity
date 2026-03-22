using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPWobbleText : MonoBehaviour
{
    [SerializeField] private TMPWobbleProfile profile;
    [SerializeField] private bool playOnEnable = true;

    private TextMeshProUGUI tmp;
    private bool isPlaying;

    private string lastText = string.Empty;
    private float[] randomPhaseX;
    private float[] randomPhaseY;

    private void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        if (playOnEnable)
            StartWobble();
    }

    private void OnDisable()
    {
        StopWobble(false);
    }

    private void LateUpdate()
    {
        if (!isPlaying || tmp == null || profile == null || !tmp.enabled)
            return;

        string currentText = tmp.text ?? string.Empty;

        if (currentText != lastText || randomPhaseX == null || randomPhaseY == null ||
            randomPhaseX.Length != currentText.Length || randomPhaseY.Length != currentText.Length)
        {
            RegenerateRandomOffsets();
        }

        ApplyWobble();
    }

    public void SetProfile(TMPWobbleProfile newProfile, bool restartSeeds = true)
    {
        profile = newProfile;

        if (restartSeeds)
            RegenerateRandomOffsets();
    }

    public void StartWobble()
    {
        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        isPlaying = true;
        RegenerateRandomOffsets();
    }

    public void StopWobble(bool resetVisuals = true)
    {
        isPlaying = false;

        if (tmp == null)
            return;

        if (resetVisuals)
        {
            tmp.ForceMeshUpdate();
            tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    public void RefreshSeeds()
    {
        RegenerateRandomOffsets();
    }

    private void RegenerateRandomOffsets()
    {
        if (tmp == null || profile == null)
            return;

        lastText = tmp.text ?? string.Empty;

        int length = lastText.Length;
        randomPhaseX = new float[length];
        randomPhaseY = new float[length];

        for (int i = 0; i < length; i++)
        {
            randomPhaseX[i] = Random.Range(profile.randomPhaseMin, profile.randomPhaseMax);
            randomPhaseY[i] = Random.Range(profile.randomPhaseMin, profile.randomPhaseMax);
        }
    }

    private void ApplyWobble()
    {
        tmp.ForceMeshUpdate();

        TMP_TextInfo textInfo = tmp.textInfo;
        float time = Time.time * profile.speed;

        for (int meshIndex = 0; meshIndex < textInfo.meshInfo.Length; meshIndex++)
        {
            Vector3[] vertices = textInfo.meshInfo[meshIndex].vertices;

            for (int charIndex = 0; charIndex < textInfo.characterCount; charIndex++)
            {
                TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];

                if (!charInfo.isVisible || charInfo.materialReferenceIndex != meshIndex)
                    continue;

                int vertexIndex = charInfo.vertexIndex;

                float phaseX = (charIndex < randomPhaseX.Length) ? randomPhaseX[charIndex] : 0f;
                float phaseY = (charIndex < randomPhaseY.Length) ? randomPhaseY[charIndex] : 0f;

                float x = Mathf.Sin(time * profile.xFrequencyMultiplier + phaseX) * profile.amountX;
                float y = Mathf.Cos(time * profile.yFrequencyMultiplier + phaseY) * profile.amountY;

                float noiseX = (Mathf.PerlinNoise(charIndex * 0.173f, Time.time * 0.31f * profile.speed) - 0.5f) * 2f * profile.noiseAmount;
                float noiseY = (Mathf.PerlinNoise(charIndex * 0.271f, Time.time * 0.47f * profile.speed) - 0.5f) * 2f * profile.noiseAmount;

                Vector3 offset = new Vector3(x + noiseX, y + noiseY, 0f);

                vertices[vertexIndex + 0] += offset;
                vertices[vertexIndex + 1] += offset;
                vertices[vertexIndex + 2] += offset;
                vertices[vertexIndex + 3] += offset;
            }

            tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }
}