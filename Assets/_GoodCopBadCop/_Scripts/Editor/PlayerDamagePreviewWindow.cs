using System;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using GoodCopBadCop.Infrastructure;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace GoodCopBadCop.Editor
{
    public sealed class PlayerDamagePreviewWindow : EditorWindow
    {
        private readonly List<EffectPreset> previewPresets = new List<EffectPreset>();
        private PlayerInstance player;
        private PlayerHealth playerHealth;
        private IEffectService effectService;
        private IEffectCatalog effectCatalog;
        private string status = "Not connected.";
        private Vector2 scrollPosition;

        [MenuItem(EditorConstants.PlayerDamagePreviewMenuPath, false, EditorConstants.RootMenuPriority + 1)]
        private static void Open()
        {
            PlayerDamagePreviewWindow window = GetWindow<PlayerDamagePreviewWindow>();
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
            if (!EditorApplication.isPlaying)
                return;

            if (!CanPreviewEffect() && !CanKillPlayer())
                RefreshRuntimeReferences();

            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawStatus();

            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!CanPreviewEffect()))
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                foreach (EffectPreset preset in previewPresets)
                    DrawEffectOption(preset);
                EditorGUILayout.EndScrollView();
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

            if (GUILayout.Button($"Preview {preset.DisplayName}", GUILayout.Height(30f)))
                PreviewEffect(preset);
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

            if (!CanPreviewEffect())
            {
                Debug.LogWarning($"[PlayerDamagePreviewWindow] Cannot preview '{preset.DisplayName}': {status}");
                return;
            }

            effectService.Play(preset, new EffectContext(player.gameObject, player.transform.position));
            Repaint();
        }

        private void KillPlayer()
        {
            RefreshRuntimeReferences();

            if (!CanKillPlayer())
            {
                Debug.LogWarning($"[PlayerDamagePreviewWindow] Cannot kill player: {status}");
                return;
            }

            playerHealth.TakeDamage(999f, EffectKeys.PlayerDeath);
            Debug.Log("[PlayerDamagePreviewWindow] Applied lethal damage to the local player.");
            Repaint();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += RefreshRuntimeReferences;
            else if (stateChange == PlayModeStateChange.ExitingPlayMode || stateChange == PlayModeStateChange.EnteredEditMode)
                ClearRuntimeReferences("Enter Play Mode to preview player effects.");

            Repaint();
        }

        private void RefreshRuntimeReferences()
        {
            player = null;
            playerHealth = null;
            effectService = null;
            effectCatalog = null;
            previewPresets.Clear();

            if (!EditorApplication.isPlaying)
            {
                status = "Enter Play Mode to preview player effects.";
                return;
            }

            player = PlayerInstance.Instance;
            if (player == null)
            {
                status = "PlayerInstance.Instance was not found. Start or join a game and wait for the local player to spawn.";
                return;
            }

            playerHealth = player.PlayerHealth != null ? player.PlayerHealth : player.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                status = "Local player does not have a PlayerHealth component.";
                return;
            }

            if (!TryResolveEffects())
                return;

            foreach (EffectPreset preset in effectCatalog.Presets)
            {
                if (preset != null && preset.Key != EffectKeys.PlayerDeath)
                    previewPresets.Add(preset);
            }

            status = previewPresets.Count > 0
                ? "Ready."
                : "Effect catalog has no preview presets.";
        }

        private bool TryResolveEffects()
        {
            MainSceneLifetimeScope scope = UnityEngine.Object.FindFirstObjectByType<MainSceneLifetimeScope>();
            if (scope == null || scope.Container == null)
            {
                status = "MainSceneLifetimeScope container was not found.";
                return false;
            }

            try
            {
                effectService = scope.Container.Resolve<IEffectService>();
                effectCatalog = scope.Container.Resolve<IEffectCatalog>();
            }
            catch (Exception exception)
            {
                status = $"Effects services were not resolved: {exception.Message}";
                return false;
            }

            if (effectService == null || effectCatalog == null)
            {
                status = "Effects services are not registered.";
                return false;
            }

            return true;
        }

        private void ClearRuntimeReferences(string newStatus)
        {
            player = null;
            playerHealth = null;
            effectService = null;
            effectCatalog = null;
            previewPresets.Clear();
            status = newStatus;
        }

        private bool CanPreviewEffect()
        {
            return EditorApplication.isPlaying
                   && player != null
                   && playerHealth != null
                   && effectService != null
                   && !playerHealth.IsDead;
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