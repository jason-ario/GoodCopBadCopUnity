using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace GoodCopBadCop.Infrastructure
{
    public interface ILegacyGameObjectInjector
    {
        void Inject(GameObject root);
    }

    public sealed class LegacyGameObjectInjector : ILegacyGameObjectInjector
    {
        private readonly IObjectResolver resolver;

        public LegacyGameObjectInjector(IObjectResolver resolver)
        {
            this.resolver = resolver;
        }

        public void Inject(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            resolver.InjectGameObject(root);
        }
    }
}