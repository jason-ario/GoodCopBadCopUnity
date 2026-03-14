using Unity.Netcode;
using UnityEngine;

public class GlassController : NetworkBehaviour
{
    private Animator _animator;
    private NetworkVariable<bool> _isUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        _isUp.OnValueChanged += OnGlassStateChanged;
        _animator.SetBool("IsUp", _isUp.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isUp.OnValueChanged -= OnGlassStateChanged;
    }

    /// <summary>
    /// Toggles the glass window up or down.
    /// </summary>
    public void Toggle() => SetIsUp(!_isUp.Value);

    /// <summary>
    /// Moves the glass window up.
    /// </summary>
    public void SetUp() => SetIsUp(true);

    /// <summary>
    /// Moves the glass window down.
    /// </summary>
    public void SetDown() => SetIsUp(false);

    private void SetIsUp(bool value)
    {
        if (IsServer)
        {
            _isUp.Value = value;
        }
        else
        {
            SetIsUpServerRpc(value);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetIsUpServerRpc(bool value)
    {
        _isUp.Value = value;
    }

    private void OnGlassStateChanged(bool previous, bool current)
    {
        _animator.SetBool("IsUp", current);
    }
}
