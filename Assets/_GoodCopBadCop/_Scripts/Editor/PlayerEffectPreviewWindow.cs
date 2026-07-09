using System;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using GoodCopBadCop.Infrastructure;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace GoodCopBadCop.Editor
{
    public sealed class PlayerEffectPreviewWindow : EditorWindow
    {
        private const string EffectCatalogAssetPath = "Assets/_GoodCopBadCop/_Data/Effects/EffectCatalog.asset";

        private readonly List<EffectPreset> previewPresets = new List<EffectPreset>();
        private readonly FullscreenEffectService fallbackFullscreenEffectService = new FullscreenEffectService();
        private PlayerInstance player;
        private PlayerHealth playerHealth;
        private IEffectService effectService;
        private IEffectCatalog effectCatalog;
        private string status = "Not connected.";
        private Vector2 scrollPosition;

        [MenuItem(EditorConstants.PlayerEffectPreviewMenuPath, false, EditorConstants.RootMenuPriority + 1)]
        private static void Open()
        {
            PlayerEffectPreviewWindow window = GetWindow<PlayerEffectPreviewWindow>();
            window.titleContent = new GUIContent("Player Effects Preview");
            window.minSize = new Vector2(360f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            RefreshRuntimeReferences();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        }

        private void OnInspectorUpdate()
        {
            RefreshRuntimeReferences();
            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawStatus();

            EditorGUILayout.Space(8f);

            using (EditorGUILayout.ScrollViewScope scrollView = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                scrollPosition = scrollView.scrollPosition;
                foreach (EffectPreset preset in previewPresets.ToArray())
                    DrawEffectOption(preset);
            }

            EditorGUILayout.Space(8f);
            DrawDeathControls();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Player Effects Preview", EditorStyles.boldLabel);

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Refresh", GUILayout.Height(24f)))
                RefreshRuntimeReferences();
        }

        private void DrawStatus()
        {
            if (status == "Ready.")
                return;

            EditorGUILayout.HelpBox(status, MessageType.Warning);
        }

        private void DrawEffectOption(EffectPreset preset)
        {
            if (preset == null || preset.Key == EffectKeys.PlayerDeath)
                return;

            using (new EditorGUI.DisabledScope(!CanPreviewEffect(preset)))
            {
                if (GUILayout.Button($"Preview {preset.DisplayName}", GUILayout.Height(30f)))
                    PreviewEffect(preset);
            }
        }

        private void DrawDeathControls()
        {
            using (new EditorGUI.DisabledScope(!CanKillPlayer()))
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f, 1f);
                if (GUILayout.Button("Dead", GUILayout.Height(34f)))
                    KillPlayer();
                GUI.backgroundColor = Color.white;
            }
        }

        private void PreviewEffect(EffectPreset preset)
        {
            RefreshRuntimeReferences();

            if (!CanPreviewEffect(preset))
            {
                Debug.LogWarning($"[PlayerEffectPreviewWindow] Cannot preview '{preset.DisplayName}': {status}");
                return;
            }

            EffectContext context = player != null
                ? new EffectContext(player.gameObject, player.transform.position)
                : EffectContext.Default;

            if (effectService != null)
            {
                effectService.Play(preset, context);
            }
            else
            {
                fallbackFullscreenEffectService.Play(preset.Fullscreen, context);
            }

            Repaint();
        }

        private void KillPlayer()
        {
            RefreshRuntimeReferences();

            if (!CanKillPlayer())
            {
                Debug.LogWarning($"[PlayerEffectPreviewWindow] Cannot kill player: {status}");
                return;
            }

            EffectContext context = new EffectContext(player.gameObject, player.transform.position);
            if (effectService != null)
            {
                effectService.PlayByKey(EffectKeys.PlayerDeath, context);
            }
            else if (effectCatalog != null && effectCatalog.TryGet(EffectKeys.PlayerDeath, out EffectPreset deathPreset))
            {
                fallbackFullscreenEffectService.Play(deathPreset.Fullscreen, context);
            }

            playerHealth.TakeDamage(999f, EffectKeys.PlayerDeath);
            Debug.Log("[PlayerEffectPreviewWindow] Applied lethal damage to the local player.");
            Repaint();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.EnteredPlayMode || stateChange == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += RefreshRuntimeReferences;

            Repaint();
        }

        private void RefreshRuntimeReferences()
        {
            player = null;
            playerHealth = null;
            effectService = null;
            effectCatalog = LoadEffectCatalogAsset();
            previewPresets.Clear();

            if (EditorApplication.isPlaying)
            {
                TryResolveEffects();
                TryResolvePlayer();
            }

            PopulatePreviewPresets();
            UpdateStatus();
        }

        private EffectCatalog LoadEffectCatalogAsset()
        {
            return AssetDatabase.LoadAssetAtPath<EffectCatalog>(EffectCatalogAssetPath);
        }

        private void PopulatePreviewPresets()
        {
            if (effectCatalog == null)
                return;

            foreach (EffectPreset preset in effectCatalog.Presets)
            {
                if (preset != null && preset.Key != EffectKeys.PlayerDeath)
                    previewPresets.Add(preset);
            }
        }

        private void TryResolvePlayer()
        {
            player = PlayerInstance.Instance;
            if (player == null)
            {
                PlayerInstance[] players = UnityEngine.Object.FindObjectsByType<PlayerInstance>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                foreach (PlayerInstance candidate in players)
                {
                    if (candidate != null && candidate.IsOwner)
                    {
                        player = candidate;
                        break;
                    }
                }

                if (player == null && players.Length > 0)
                    player = players[0];
            }

            if (player != null)
                playerHealth = player.PlayerHealth != null ? player.PlayerHealth : player.GetComponent<PlayerHealth>();
        }

        private void TryResolveEffects()
        {
            MainSceneLifetimeScope scope = UnityEngine.Object.FindAnyObjectByType<MainSceneLifetimeScope>();
            if (scope == null || scope.Container == null)
                return;

            try
            {
                effectService = scope.Container.Resolve<IEffectService>();
                effectCatalog = scope.Container.Resolve<IEffectCatalog>() ?? effectCatalog;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayerEffectPreviewWindow] Effects services were not resolved: {exception.Message}");
            }
        }

        private void UpdateStatus()
        {
            if (effectCatalog == null)
            {
                status = $"Effect catalog was not found at '{EffectCatalogAssetPath}'.";
                return;
            }

            if (previewPresets.Count == 0)
            {
                status = "Effect catalog has no preview presets.";
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                status = "Enter Play Mode to preview player effects.";
                return;
            }

            if (effectService == null)
            {
                status = playerHealth == null
                    ? "Effects service and local player were not found. Preview uses fullscreen fallback only; Dead is disabled."
                    : "Effects service was not found. Preview uses fullscreen fallback only.";
                return;
            }

            if (playerHealth == null)
            {
                status = "Local player was not found. Preview works; Dead is disabled.";
                return;
            }

            status = "Ready.";
        }

        private bool CanPreviewEffect(EffectPreset preset)
        {
            return EditorApplication.isPlaying
                   && effectCatalog != null
                   && preset != null
                   && (effectService != null || (preset.Fullscreen != null && preset.Fullscreen.Enabled));
        }

        private bool CanKillPlayer()
        {
            return EditorApplication.isPlaying
                   && player != null
                   && playerHealth != null
                   && !playerHealth.IsDead;
        }
    }
}