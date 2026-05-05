using System;
using UnityEngine;
using UnityEngine.UI;

public class RadiationBarUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    private PlayerRadiation _playerRadiation;
    
    void Start()
    {
       
    }

    private void OnEnable()
    {
        if (PlayerInstance.Instance == null) return;

        if (_playerRadiation == null)
            _playerRadiation = PlayerInstance.Instance.PlayerRadiation;

        _playerRadiation.OnRadiationChanged.AddListener(UpdateBar);
        UpdateBar(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void Update()
    {
        if (_playerRadiation != null) return;
        if (PlayerInstance.Instance == null) return;

        _playerRadiation = PlayerInstance.Instance.PlayerRadiation;
        _playerRadiation.OnRadiationChanged.AddListener(UpdateBar);
        UpdateBar(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void OnDisable()
    {
        if (_playerRadiation == null) return;
        _playerRadiation.OnRadiationChanged.RemoveListener(UpdateBar);
    }

    private void UpdateBar(float current, float max)
    {
        fillImage.fillAmount = current / max;
    }
}