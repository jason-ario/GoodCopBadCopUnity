using System;
using System.Collections.Generic;
using R3;

public sealed class PersistentReactiveProperty<T> : ReactiveProperty<T>
{
    private readonly string _key;
    private readonly IPersistentPropertyStorage _storage;

    public string Key => _key;

    public PersistentReactiveProperty(string key)
        : this(key, default, PlayerPrefsPersistentPropertyStorage.Shared)
    {
    }

    public PersistentReactiveProperty(string key, T defaultValue)
        : this(key, defaultValue, PlayerPrefsPersistentPropertyStorage.Shared)
    {
    }

    public PersistentReactiveProperty(string key, T defaultValue, IPersistentPropertyStorage storage)
        : base(LoadInitialValue(key, defaultValue, storage))
    {
        _key = ValidateKey(key);
        _storage = storage;
    }

    public PersistentReactiveProperty(string key, T defaultValue, IEqualityComparer<T> equalityComparer)
        : this(key, defaultValue, PlayerPrefsPersistentPropertyStorage.Shared, equalityComparer)
    {
    }

    public PersistentReactiveProperty(
        string key,
        T defaultValue,
        IPersistentPropertyStorage storage,
        IEqualityComparer<T> equalityComparer)
        : base(LoadInitialValue(key, defaultValue, storage), equalityComparer)
    {
        _key = ValidateKey(key);
        _storage = storage;
    }

    public override T Value
    {
        get => base.Value;
        set
        {
            base.Value = value;
            SaveCurrentValue();
        }
    }

    public override void OnNext(T value)
    {
        base.OnNext(value);
        SaveCurrentValue();
    }

    public void DeleteStoredValue()
    {
        _storage.Delete(_key);
    }

    public void Flush()
    {
        _storage.Flush();
    }

    private void SaveCurrentValue()
    {
        _storage.Save(_key, base.Value);
    }

    private static T LoadInitialValue(string key, T defaultValue, IPersistentPropertyStorage storage)
    {
        if (storage == null)
            throw new ArgumentNullException(nameof(storage));

        return storage.Load(ValidateKey(key), defaultValue);
    }

    private static string ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));

        return key;
    }
}
