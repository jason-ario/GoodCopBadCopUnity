using System;
using UnityEngine;

public class ShopItem : MonoBehaviour
{
    [SerializeField] private string name;
    public string Name => name; 
    public PickableItemData pickableItemData;

    [SerializeField] private int price;
    public int Price => price;
    [SerializeField] private Vector3 rotationOffset; 
    public Vector3 RotationOffset => rotationOffset;
    
    private void Awake()
    {
        SetLayer();
    }

    void SetLayer()
    {
        int layer = LayerMask.NameToLayer("ShopItems");
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }
}
