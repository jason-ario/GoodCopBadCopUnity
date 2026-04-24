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

    private void Update()
    {
        if (_playerRadiation == null)
        {
            _playerRadiation = PlayerInstance.Instance.PlayerRadiation;
            _playerRadiation.OnRadiationChanged.AddListener(UpdateBar);
            UpdateBar(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
        }
    }

    private void OnDisable()
    {
        PlayerInstance.Instance.PlayerRadiation.OnRadiationChanged.RemoveListener(UpdateBar);
    }

    private void UpdateBar(float current, float max)
    {
        fillImage.fillAmount = current / max;
    }
}