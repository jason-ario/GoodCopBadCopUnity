using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One named expression and the material that represents it.
/// </summary>
[Serializable]
public struct DialogueExpressionEntry
{
    [Tooltip("Key authored in ScriptedDialogueNode / ScriptedDialogueChoice 'expression' (case-insensitive), e.g. Concerned, Angry.")]
    public string key;

    [Tooltip("Material applied to the target renderer slot while this expression is active.")]
    public Material material;
}

/// <summary>
/// Swaps one material slot on a speaker's renderer (e.g. Tina's mask) per scripted dialogue line.
///
/// <see cref="ScriptedDialogueRunner"/> calls <see cref="SetExpression"/> on every client at the
/// start of each NPC line using the node/choice <c>expression</c> key. An empty or unknown key
/// restores the default material. The expression also reverts automatically once this character
/// is no longer <see cref="ScriptedDialogueRunner.ActiveDialogueSpeakerNetId"/>, so an abandoned,
/// force-exited or completed sequence never leaves the mask stuck on a non-default face.
///
/// Place on the suspect root (or any child under its NetworkObject).
/// </summary>
[DisallowMultipleComponent]
public class DialogueExpressionMaterial : MonoBehaviour
{
    [Tooltip("Renderer whose material slot is swapped (e.g. the mask mesh). Leave empty to auto-find " +
             "the child renderer whose slot already uses the default or an expression material.")]
    [SerializeField] private Renderer _targetRenderer;

    [Tooltip("Material slot index on the target renderer.")]
    [SerializeField, Min(0)] private int _materialIndex;

    [Tooltip("Material used when no expression (or an unknown one) is set. " +
             "Leave empty to capture the renderer's current material on Awake.")]
    [SerializeField] private Material _defaultMaterial;

    [SerializeField] private DialogueExpressionEntry[] _expressions = Array.Empty<DialogueExpressionEntry>();

    private NetworkObject _networkObject;
    private string _currentKey = string.Empty;

    /// <summary>Currently applied expression key; empty when the default material is shown.</summary>
    public string CurrentExpression => _currentKey;

    private void Awake()
    {
        _networkObject = GetComponentInParent<NetworkObject>();

        if (_targetRenderer == null)
            _targetRenderer = FindRendererUsingKnownMaterial();

        if (_targetRenderer == null)
        {
            Debug.LogWarning($"[DialogueExpressionMaterial] '{name}': no target renderer assigned or found.", this);
            return;
        }

        if (_defaultMaterial == null)
        {
            Material[] mats = _targetRenderer.sharedMaterials;
            if (_materialIndex < mats.Length)
                _defaultMaterial = mats[_materialIndex];
        }

        ResetExpression();
    }

    /// <summary>
    /// Fallback when <see cref="_targetRenderer"/> is unassigned: the first child renderer whose
    /// <see cref="_materialIndex"/> slot already holds the default or any expression material.
    /// </summary>
    private Renderer FindRendererUsingKnownMaterial()
    {
        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = candidate.sharedMaterials;
            if (_materialIndex >= mats.Length || mats[_materialIndex] == null) continue;

            Material current = mats[_materialIndex];
            if (current == _defaultMaterial) return candidate;
            foreach (DialogueExpressionEntry entry in _expressions)
                if (entry.material == current) return candidate;
        }
        return null;
    }

    private void Update()
    {
        if (_currentKey.Length == 0) return;

        // Revert once this character's scripted sequence has ended (any exit path).
        if (_networkObject == null || !_networkObject.IsSpawned ||
            ScriptedDialogueRunner.ActiveDialogueSpeakerNetId != _networkObject.NetworkObjectId)
        {
            ResetExpression();
        }
    }

    private void OnDisable()
    {
        ResetExpression();
    }

    /// <summary>
    /// Applies the material registered for <paramref name="key"/>. Empty or unknown keys
    /// restore the default material.
    /// </summary>
    public void SetExpression(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ResetExpression();
            return;
        }

        key = key.Trim();
        foreach (DialogueExpressionEntry entry in _expressions)
        {
            if (entry.material == null || !string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            ApplyMaterial(entry.material);
            _currentKey = entry.key;
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning($"[DialogueExpressionMaterial] '{name}': unknown expression '{key}', using default.", this);
#endif
        ResetExpression();
    }

    /// <summary>Restores the default material.</summary>
    public void ResetExpression()
    {
        _currentKey = string.Empty;
        if (_defaultMaterial != null)
            ApplyMaterial(_defaultMaterial);
    }

    private void ApplyMaterial(Material material)
    {
        if (_targetRenderer == null) return;

        Material[] mats = _targetRenderer.sharedMaterials;
        if (_materialIndex >= mats.Length || mats[_materialIndex] == material) return;

        mats[_materialIndex] = material;
        _targetRenderer.sharedMaterials = mats;
    }
}
