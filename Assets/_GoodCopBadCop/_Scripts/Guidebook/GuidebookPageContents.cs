using UnityEngine;

/// <summary>
/// Base class for in-scene guidebook page content objects.
/// Each page that renders to a RenderTexture has one of these.
/// </summary>
public abstract class GuidebookPageContents : MonoBehaviour
{
    /// <summary>
    /// Called by GuidebookTabController when this page becomes active.
    /// Override to update dynamic content before it is rendered.
    /// </summary>
    public abstract void Refresh();
}
