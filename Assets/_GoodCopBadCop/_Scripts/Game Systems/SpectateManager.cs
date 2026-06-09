using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpectateManager : MonoBehaviour
{
    public static SpectateManager Instance;

    private List<PlayerInstance> _teammates = new List<PlayerInstance>();
    private int _currentIndex = 0;
    private bool _isSpectating = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (!_isSpectating) return;

        if (Input.GetMouseButtonDown(0)) // Left click to next teammate
        {
            SpectateNext();
        }
    }

    public void StartSpectating()
    {
        _isSpectating = true;
        UpdateTeammateList();
        SpectateNext();
    }

    public void SpectateNext()
    {
        if (_teammates.Count == 0)
        {
            UpdateTeammateList();
            if (_teammates.Count == 0) return;
        }

        _currentIndex = (_currentIndex + 1) % _teammates.Count;
        var target = _teammates[_currentIndex];

        if (target != null && target != PlayerInstance.Instance)
        {
            PlayerInstance.Instance.SetSpectateTarget(target.CameraTransform);
        }
        else if (_teammates.Count > 1)
        {
            // If we hit ourselves, skip to next
            SpectateNext();
        }
    }

    private void UpdateTeammateList()
    {
        _teammates.Clear();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<PlayerInstance>();
                if (player != null && player != PlayerInstance.Instance && !player.PlayerHealth.IsDead)
                {
                    _teammates.Add(player);
                }
            }
        }
    }
}
