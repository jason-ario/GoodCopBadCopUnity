using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor
{
    public sealed class PlayerDamagePreviewWindow : EditorWindow
    {
        private readonly struct DamagePreviewOption
        {
            public readonly string Label;
            public readonly string Description;
            public readonly float DamageAmount;

            public DamagePreviewOption(string label, string description, float damageAmount)
            {
                Label = label;
                Description = description;
                DamageAmount = damageAmount;
            }
        }

        private static readonly DamagePreviewOption[] DamageOptions =
        {
            new DamagePreviewOption("Mutant Melee", "Standard mutant attack hit feedback.", 10f),
            new DamagePreviewOption("Bear Trap", "Heavy trap snap damage feedback.", 50f),
            new DamagePreviewOption("Friendly Fire / Melee", "Player melee weapon friendly-fire feedback.", 25f),
            new DamagePreviewOption("Radiation Tick", "Small radiation health tick feedback.", 5f),
            new DamagePreviewOption("Scripted Rifle", "Repeated rifle chip damage feedback.", 1f),
            new DamagePreviewOption("Debug Hit", "Matches the existing H-key local hit test.", 10f),
        };

        private PlayerInstance player;
        private PlayerHealth playerHealth;
        private HurtVFXController hurtVFX;
        private ScreenDamage screenDamage;
        private string status = "Not connected.";
        private Vector2 scrollPosition;

        [MenuItem(EditorConstants.PlayerDamagePreviewMenuPath, false, EditorConstants.RootMenuPriority + 1)]
        private static void Open()
        {
            PlayerDamagePreviewWindow window = GetWindow<PlayerDamagePreviewWindow>();
            window.titleContent = new GUIContent("Player Damage Preview");
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

            if (!CanPreviewDamage() && !CanKillPlayer())
                RefreshRuntimeReferences();

            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawStatus();

            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!CanPreviewDamage()))
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                foreach (DamagePreviewOption option in DamageOptions)
                    DrawDamageOption(option);
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(8f);
            DrawDeathControls();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Player Damage Preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Preview local hurt feedback without changing HP. Dead applies real lethal damage.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Refresh", GUILayout.Height(24f)))
                RefreshRuntimeReferences();
        }

        private void DrawStatus()
        {
            MessageType messageType = status == "Ready." ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(status, messageType);

            if (playerHealth == null)
                return;

            EditorGUILayout.LabelField("Health", $"{playerHealth.Health:0.#} / {playerHealth.MaxHealth:0.#}");
            EditorGUILayout.LabelField("Dead", playerHealth.IsDead ? "Yes" : "No");
        }

        private void DrawDamageOption(DamagePreviewOption option)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(option.Label, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(option.Description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Preview damage", option.DamageAmount.ToString("0.#"));

            if (GUILayout.Button($"Preview {option.Label}", GUILayout.Height(28f)))
                PreviewDamage(option);

            EditorGUILayout.EndVertical();
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

        private void PreviewDamage(DamagePreviewOption option)
        {
            RefreshRuntimeReferences();

            if (!CanPreviewDamage())
            {
                Debug.LogWarning($"[PlayerDamagePreviewWindow] Cannot preview '{option.Label}': {status}");
                return;
            }

            hurtVFX.PreviewDamageFeedback(option.DamageAmount, option.Label);
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

            playerHealth.TakeDamage(999f);
            Debug.Log("[PlayerDamagePreviewWindow] Applied lethal damage to the local player.");
            Repaint();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += RefreshRuntimeReferences;
            else if (stateChange == PlayModeStateChange.ExitingPlayMode || stateChange == PlayModeStateChange.EnteredEditMode)
                ClearRuntimeReferences("Enter Play Mode to preview player damage feedback.");

            Repaint();
        }

        private void RefreshRuntimeReferences()
        {
            player = null;
            playerHealth = null;
            hurtVFX = null;
            screenDamage = null;

            if (!EditorApplication.isPlaying)
            {
                status = "Enter Play Mode to preview player damage feedback.";
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

            hurtVFX = player.GetComponent<HurtVFXController>();
            if (hurtVFX == null)
            {
                status = "Local player does not have a HurtVFXController component.";
                return;
            }

            screenDamage = UIController.Instance != null ? UIController.Instance.ScreenDamage : null;
            if (screenDamage == null)
            {
                status = "UIController.ScreenDamage was not found. Hurt audio and camera impulse may still work, but screen overlay cannot be previewed.";
                return;
            }

            status = "Ready.";
        }

        private void ClearRuntimeReferences(string newStatus)
        {
            player = null;
            playerHealth = null;
            hurtVFX = null;
            screenDamage = null;
            status = newStatus;
        }

        private bool CanPreviewDamage()
        {
            return EditorApplication.isPlaying
                   && player != null
                   && playerHealth != null
                   && hurtVFX != null
                   && screenDamage != null
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