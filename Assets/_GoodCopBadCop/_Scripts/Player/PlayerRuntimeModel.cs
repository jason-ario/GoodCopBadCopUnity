using R3;

namespace GoodCopBadCop.Player
{
    public interface IPlayerRuntimeModel
    {
        ReadOnlyReactiveProperty<global::PlayerInstance> LocalPlayer { get; }
    }

    public sealed class PlayerRuntimeModel : IPlayerRuntimeModel
    {
        public readonly ReactiveProperty<global::PlayerInstance> LocalPlayerMutable = new(null);

        public ReadOnlyReactiveProperty<global::PlayerInstance> LocalPlayer => LocalPlayerMutable;
    }
}
