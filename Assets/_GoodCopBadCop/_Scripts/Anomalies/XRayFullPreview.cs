using UnityEngine;

namespace GoodCopBadCop.XRay
{
    /// <summary>
    /// Play Mode-only full-body X-ray mode for Anomaly Preview. Unlike the cursor scanner,
    /// this keeps anatomy visible in the regular Game camera for the entire suspect.
    /// </summary>
    public sealed class XRayFullPreview : MonoBehaviour
    {
        private XRayAnatomyView _anatomyView;

        public void Configure(XRayAnatomyView anatomyView)
        {
            _anatomyView = anatomyView;
            SetVisible(true);
        }

        private void OnEnable()
        {
            SetVisible(true);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_anatomyView != null)
                _anatomyView.SetXRayVisible(visible);
        }
    }
}
