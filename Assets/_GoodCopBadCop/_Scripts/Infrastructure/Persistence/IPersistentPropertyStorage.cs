namespace GoodCopBadCop.Infrastructure.Persistence
{
    public interface IPersistentPropertyStorage
    {
        bool HasKey(string key);
        T Load<T>(string key, T defaultValue = default);
        void Save<T>(string key, T value);
        void Delete(string key);
        void Flush();
    }
}