using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class ToolShopController : MonoBehaviour
{
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private ItemPreviewSpawner _itemPreviewSpawner;
    [SerializeField] private ShopItem[] shopItems;
    [SerializeField] ShopItemView shopItemViewPrefab;
    [SerializeField] private Transform shopItemViewContainer;
    [SerializeField] TextMeshProUGUI itemPreviewText;
    private List<ShopItemView> shopItemViews = new List<ShopItemView>();
    [SerializeField] private TextMeshProUGUI _buyText;
    
    private void Start()
    {
        FadeIn();
        
        for (var i = 0; i < shopItems.Length; i++)
        {
            var shopItem = shopItems[i];
            if (shopItem == null) continue;

            var shopItemView = Instantiate(shopItemViewPrefab, shopItemViewContainer);
            shopItemView.Initialize(shopItem, this);
            shopItemViews.Add(shopItemView);
        }

        foreach (var shopItemView in shopItemViews)
        {
            shopItemView.Deselect();
        }

        shopItemViews[0].SelectShopItem();
    }

    private void OnDisable()
    {
        _canvasGroup.DOKill();
    }

    private void FadeIn()
    {
        _canvasGroup.alpha = 0;
        _canvasGroup.DOFade(1, .5f);
    }


    public void Select(ShopItem shopItem)
    {
        foreach (var shopItemView in shopItemViews)
        {
            shopItemView.Deselect();
        }
        
        if (shopItem == null)
        {
            Debug.LogWarning("ToolShopController: Attempted to select a null or destroyed ShopItem.");
            return;
        }

        _itemPreviewSpawner.SpawnAndFrame(shopItem);
        itemPreviewText.text = shopItem.Name;
        _buyText.text = "Buy " +  "<sprite=0>"  + shopItem.Price;
    }
}
