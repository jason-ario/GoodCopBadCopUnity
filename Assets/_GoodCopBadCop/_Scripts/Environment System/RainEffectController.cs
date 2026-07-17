using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    public sealed class RainEffectController : MonoBehaviour
    {
        public void SetEnabled(bool enabled)
        {
            gameObject.SetActive(enabled);
        }
    }
}
