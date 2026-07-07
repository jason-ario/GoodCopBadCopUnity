namespace GoodCopBadCop.Player
{
    public interface IPlayerRuntimeService
    {
        void SetLocalPlayer(global::PlayerInstance playerInstance);
        void ClearLocalPlayer(global::PlayerInstance playerInstance);
    }

    public sealed class PlayerRuntimeService : IPlayerRuntimeService
    {
        private readonly PlayerRuntimeModel model;

        public PlayerRuntimeService(PlayerRuntimeModel model)
        {
            this.model = model;
        }

        public void SetLocalPlayer(global::PlayerInstance playerInstance)
        {
            if (model.LocalPlayerMutable.Value == playerInstance)
            {
                return;
            }

            model.LocalPlayerMutable.Value = playerInstance;
        }

        public void ClearLocalPlayer(global::PlayerInstance playerInstance)
        {
            if (model.LocalPlayerMutable.Value != playerInstance)
            {
                return;
            }

            model.LocalPlayerMutable.Value = null;
        }
    }
}
