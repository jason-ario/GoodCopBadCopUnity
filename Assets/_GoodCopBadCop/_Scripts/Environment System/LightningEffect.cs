using System.Collections;
using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Cosmetic, client-local lightning flashes. Lives under the RainEffect object so it only runs while
    /// rain is enabled (RainEffectController toggles the GameObject). Each strike briefly enables a
    /// directional light, brightens the current skybox (via a runtime material clone, never the asset)
    /// and boosts trilight ambient colors, then restores everything.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LightningEffect : MonoBehaviour
    {
        [Header("Light")]
        [SerializeField] private Light flashLight;
        [SerializeField] private float peakIntensity = 4f;
        [Tooltip("Random pitch (X angle) range for the flash direction, in degrees.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(25f, 60f);

        [Header("Timing")]
        [Tooltip("Seconds between strikes (min, max).")]
        [SerializeField] private Vector2 strikeInterval = new Vector2(8f, 22f);
        [Tooltip("Delay before the very first strike after rain starts (min, max).")]
        [SerializeField] private Vector2 initialDelay = new Vector2(3f, 8f);
        [Tooltip("Number of flickers per strike (min, max inclusive).")]
        [SerializeField] private Vector2Int flickersPerStrike = new Vector2Int(1, 3);
        [Tooltip("Duration of a single flicker's decay (min, max).")]
        [SerializeField] private Vector2 flickerDuration = new Vector2(0.06f, 0.18f);
        [Tooltip("Dark gap between flickers within one strike (min, max).")]
        [SerializeField] private Vector2 flickerGap = new Vector2(0.04f, 0.12f);

        [Header("Skybox")]
        [SerializeField] private bool flashSkybox = true;
        [Tooltip("Multiplier applied to skybox _Exposure (or _Tint/_SkyTint/_Color) at the flash peak.")]
        [SerializeField] private float skyboxPeakMultiplier = 4f;

        [Header("Ambient")]
        [SerializeField] private bool flashAmbient = true;
        [SerializeField] private Color ambientFlashColor = new Color(0.55f, 0.6f, 0.75f, 1f);

        [Header("Thunder (optional)")]
        [SerializeField] private AudioSource thunderSource;
        [SerializeField] private AudioClip[] thunderClips;
        [Tooltip("Seconds after the first flash before thunder plays (min, max). Mimics light arriving before sound.")]
        [SerializeField] private Vector2 thunderDelay = new Vector2(0.7f, 1.6f);
        [SerializeField] private Vector2 thunderPitch = new Vector2(0.85f, 1.1f);
        [SerializeField] private Vector2 thunderVolume = new Vector2(0.7f, 1f);

        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int[] TintIds =
        {
            Shader.PropertyToID("_Tint"),
            Shader.PropertyToID("_SkyTint"),
            Shader.PropertyToID("_Color"),
        };

        private Coroutine loop;
        private Material sourceSkybox;
        private Material flashSkyboxMaterial;
        private int skyboxTintId = -1;
        private float baseExposure;
        private Color baseTint;

        private bool ambientCaptured;
        private Color baseAmbientSky, baseAmbientEquator, baseAmbientGround;
        private Color appliedAmbientSky;

        private void Reset()
        {
            flashLight = GetComponent<Light>();
        }

        private void OnEnable()
        {
            SetLight(0f);
            loop = StartCoroutine(StrikeLoop());
        }

        private void OnDisable()
        {
            if (loop != null)
            {
                StopCoroutine(loop);
                loop = null;
            }

            EndFlash();
        }

        private void OnDestroy()
        {
            if (flashSkyboxMaterial != null)
            {
                Destroy(flashSkyboxMaterial);
            }
        }

        /// <summary>Triggers a strike immediately (Play Mode only; useful for tuning).</summary>
        [ContextMenu("Strike Now")]
        public void StrikeNow()
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                StartCoroutine(Strike());
            }
        }

        private IEnumerator StrikeLoop()
        {
            yield return new WaitForSeconds(Random.Range(initialDelay.x, initialDelay.y));

            while (true)
            {
                yield return Strike();
                yield return new WaitForSeconds(Random.Range(strikeInterval.x, strikeInterval.y));
            }
        }

        private IEnumerator Strike()
        {
            if (flashLight != null)
            {
                flashLight.transform.rotation = Quaternion.Euler(
                    Random.Range(pitchRange.x, pitchRange.y), Random.Range(0f, 360f), 0f);
            }

            BeginFlash();
            PlayThunder();

            int flickers = Random.Range(flickersPerStrike.x, flickersPerStrike.y + 1);
            for (int i = 0; i < flickers; i++)
            {
                // First flicker is the strongest; follow-ups are a bit weaker.
                float strength = i == 0 ? 1f : Random.Range(0.4f, 0.85f);
                float duration = Random.Range(flickerDuration.x, flickerDuration.y);

                for (float t = 0f; t < duration; t += Time.deltaTime)
                {
                    float k = 1f - t / duration;
                    ApplyFlash(strength * k * k);
                    yield return null;
                }

                ApplyFlash(0f);

                if (i < flickers - 1)
                {
                    yield return new WaitForSeconds(Random.Range(flickerGap.x, flickerGap.y));
                }
            }

            EndFlash();
        }

        private void BeginFlash()
        {
            if (flashSkybox)
            {
                PrepareSkybox();
            }

            if (flashAmbient && RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Trilight)
            {
                baseAmbientSky = RenderSettings.ambientSkyColor;
                baseAmbientEquator = RenderSettings.ambientEquatorColor;
                baseAmbientGround = RenderSettings.ambientGroundColor;
                ambientCaptured = true;
            }

            if (flashLight != null)
            {
                flashLight.enabled = true;
            }
        }

        private void ApplyFlash(float amount)
        {
            SetLight(amount * peakIntensity);

            if (flashSkyboxMaterial != null && RenderSettings.skybox == flashSkyboxMaterial)
            {
                float mul = Mathf.Lerp(1f, skyboxPeakMultiplier, amount);
                if (skyboxTintId == ExposureId)
                {
                    flashSkyboxMaterial.SetFloat(ExposureId, baseExposure * mul);
                }
                else if (skyboxTintId != -1)
                {
                    Color c = baseTint * mul;
                    c.a = baseTint.a;
                    flashSkyboxMaterial.SetColor(skyboxTintId, c);
                }
            }

            if (ambientCaptured)
            {
                Color add = ambientFlashColor * amount;
                appliedAmbientSky = baseAmbientSky + add;
                RenderSettings.ambientSkyColor = appliedAmbientSky;
                RenderSettings.ambientEquatorColor = baseAmbientEquator + add * 0.6f;
                RenderSettings.ambientGroundColor = baseAmbientGround + add * 0.2f;
            }
        }

        private void EndFlash()
        {
            SetLight(0f);
            if (flashLight != null)
            {
                flashLight.enabled = false;
            }

            // Restore the original skybox unless something else (e.g. a preset change) replaced it mid-flash.
            if (flashSkyboxMaterial != null && RenderSettings.skybox == flashSkyboxMaterial)
            {
                RenderSettings.skybox = sourceSkybox;
            }

            // Only restore ambient if no one else changed it during the flash.
            if (ambientCaptured && RenderSettings.ambientSkyColor == appliedAmbientSky)
            {
                RenderSettings.ambientSkyColor = baseAmbientSky;
                RenderSettings.ambientEquatorColor = baseAmbientEquator;
                RenderSettings.ambientGroundColor = baseAmbientGround;
            }

            ambientCaptured = false;
        }

        private void PrepareSkybox()
        {
            Material current = RenderSettings.skybox;
            if (current == null || current == flashSkyboxMaterial)
            {
                return;
            }

            if (current != sourceSkybox || flashSkyboxMaterial == null)
            {
                if (flashSkyboxMaterial != null)
                {
                    Destroy(flashSkyboxMaterial);
                }

                sourceSkybox = current;
                flashSkyboxMaterial = new Material(current) { name = current.name + " (Lightning)" };
            }
            else
            {
                flashSkyboxMaterial.CopyPropertiesFromMaterial(current);
            }

            skyboxTintId = -1;
            if (flashSkyboxMaterial.HasFloat(ExposureId))
            {
                skyboxTintId = ExposureId;
                baseExposure = flashSkyboxMaterial.GetFloat(ExposureId);
            }
            else
            {
                foreach (int id in TintIds)
                {
                    if (flashSkyboxMaterial.HasColor(id))
                    {
                        skyboxTintId = id;
                        baseTint = flashSkyboxMaterial.GetColor(id);
                        break;
                    }
                }
            }

            RenderSettings.skybox = flashSkyboxMaterial;
        }

        private void SetLight(float intensity)
        {
            if (flashLight != null)
            {
                flashLight.intensity = intensity;
            }
        }

        private void PlayThunder()
        {
            if (thunderSource == null || thunderClips == null || thunderClips.Length == 0)
            {
                return;
            }

            StartCoroutine(PlayThunderDelayed(thunderClips[Random.Range(0, thunderClips.Length)]));
        }

        private IEnumerator PlayThunderDelayed(AudioClip clip)
        {
            yield return new WaitForSeconds(Random.Range(thunderDelay.x, thunderDelay.y));
            if (clip != null && thunderSource != null && thunderSource.isActiveAndEnabled)
            {
                thunderSource.pitch = Random.Range(thunderPitch.x, thunderPitch.y);
                thunderSource.PlayOneShot(clip, Random.Range(thunderVolume.x, thunderVolume.y));
            }
        }
    }
}
