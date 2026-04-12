using UnityEngine;

public class TabOptions : MonoBehaviour
{
    public SelectableTab[] selectableTabs;
    

    public void SelectTab(int index)
    {
        foreach (SelectableTab tab in selectableTabs)
        {
            tab.SetSelected(false);
        }
        
        selectableTabs[index].SetSelected(true);
    }
}
