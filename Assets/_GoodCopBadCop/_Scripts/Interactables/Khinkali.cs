using GoodCopBadCop.Effects;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Khinkali : PickableObject
{
    // в”Ђв”Ђ Eat в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    private const float HealAmount = 100f;

    [SerializeField] private float eatDuration = 1f;

    // в”Ђв”Ђ Spoilage в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// How close (in world units) the Khinkali must be to a MiniFridge centre for it
    /// to count as being stored inside it when the shift ends.
    /// </summary>
    [SerializeField] private float fridgeDetectionRadius = 1.5f;

    [SerializeField] private GameObject _flies;
    [SerializeField] private Renderer _meshRenderer;
    [SerializeField] private Material _spoiledMaterial;

    private Material _freshMaterial;

    private readonly NetworkVariable<bool> _isSpoiled = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsSpoiled => _isSpoiled.Value;

    // в”Ђв”Ђ Lifecycle в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    protected override void Awake()
    {
        base.Awake();

        if (_meshRenderer != null)
            _freshMaterial = _meshRenderer.sharedMaterial;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isSpoiled.OnValueChanged += OnSpoiledChanged;

        // Snap late-joining clients to the correct visual state.
        ApplySpoiledVisuals(_isSpoiled.Value);

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftEnd += OnShiftEnded;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _isSpoiled.OnValueChanged -= OnSpoiledChanged;

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftEnd -= OnShiftEnded;
    }

    // в”Ђв”Ђ Eat в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// Begins the eat sequence if not already in use. Sets the "UsingTool" anim bool,
    /// waits <see cref="eatDuration"/> seconds, heals the player by <see cref="HealAmount"/>,
    /// then despawns the item.
    /// </summary>
    public override void OnStartUse()
    {
        if (isUsing) return;

        base.OnStartUse();
        StartCoroutine(EatCoroutine());
    }

    private IEnumerator EatCoroutine()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);

        yield return new WaitForSeconds(eatDuration);

        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        PlayerInstance.Instance.Heal(HealAmount, EffectKeys.FoodHeal);

        playerPickupController.DestroyEquippedItem();
    }

    // в”Ђв”Ђ Spoilage вЂ” server only в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// Called on the server when the shift ends. Spoils the Khinkali if it is not
    /// sitting inside a powered MiniFridge.
    /// </summary>
    private void OnShiftEnded()
    {
        if (_isSpoiled.Value) return;
        if (IsHeld) return;

        if (!IsInsideActiveFridge())
            Spoil();
    }

    /// <summary>
    /// Returns true when the Khinkali is within <see cref="fridgeDetectionRadius"/> of a
    /// MiniFridge that currently has power. Server-only.
    /// </summary>
    private bool IsInsideActiveFridge()
    {
        foreach (MiniFridge fridge in FindObjectsByType<MiniFridge>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(transform.position, fridge.transform.position);
            if (dist <= fridgeDetectionRadius)
                return fridge.IsPowered;
        }

        return false;
    }

    /// <summary>Marks this Khinkali as spoiled on the server.</summary>
    private void Spoil()
    {
        _isSpoiled.Value = true;
    }

    // в”Ђв”Ђ Visuals вЂ” all clients в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    private void OnSpoiledChanged(bool previous, bool current)
    {
        ApplySpoiledVisuals(current);
    }

    private void ApplySpoiledVisuals(bool spoiled)
    {
        if (_flies != null)
            _flies.SetActive(spoiled);

        if (_meshRenderer != null)
            _meshRenderer.material = spoiled ? _spoiledMaterial : _freshMaterial;
    }
}
