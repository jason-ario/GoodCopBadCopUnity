using System;
using System.Collections.Generic;
using GoodCopBadCop.Infrastructure.Persistence;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace GoodCopBadCop.Tests.Editor.Infrastructure.Persistence
{
    public sealed class PersistentReactivePropertyTests
    {
        private enum ETestSetting
        {
            First,
            Second
        }

        [Test]
        public void Constructor_LoadsStoredValue()
        {
            var storage = new FakePersistentPropertyStorage();
            storage.Save("test.value", 7);

            using var property = new PersistentReactiveProperty<int>("test.value", 1, storage);

            Assert.That(property.Value, Is.EqualTo(7));
            Assert.That(property.Key, Is.EqualTo("test.value"));
        }

        [Test]
        public void Value_Set_SavesAndNotifies()
        {
            var storage = new FakePersistentPropertyStorage();
            using var property = new PersistentReactiveProperty<int>("test.value", 1, storage);
            var observed = new List<int>();

            using IDisposable subscription = property.Subscribe(value => observed.Add(value));
            property.Value = 2;

            Assert.That(storage.Load("test.value", 0), Is.EqualTo(2));
            Assert.That(storage.SaveCount("test.value"), Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void OnNext_SavesAndNotifies()
        {
            var storage = new FakePersistentPropertyStorage();
            using var property = new PersistentReactiveProperty<string>("test.value", "initial", storage);
            var observed = new List<string>();

            using IDisposable subscription = property.Subscribe(value => observed.Add(value));
            property.OnNext("changed");

            Assert.That(storage.Load("test.value", string.Empty), Is.EqualTo("changed"));
            Assert.That(storage.SaveCount("test.value"), Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(new[] { "initial", "changed" }));
        }

        [Test]
        public void CanBeUsedAsReadOnlyReactiveProperty()
        {
            var storage = new FakePersistentPropertyStorage();
            using var property = new PersistentReactiveProperty<float>("test.value", 1.25f, storage);

            ReadOnlyReactiveProperty<float> readOnly = property;

            Assert.That(readOnly.CurrentValue, Is.EqualTo(1.25f));
        }

        [Test]
        public void DeleteStoredValue_RemovesStorageEntry()
        {
            var storage = new FakePersistentPropertyStorage();
            using var property = new PersistentReactiveProperty<bool>("test.value", false, storage);

            property.Value = true;
            property.DeleteStoredValue();

            Assert.That(storage.HasKey("test.value"), Is.False);
        }

        [Test]
        public void Flush_ForwardsToStorage()
        {
            var storage = new FakePersistentPropertyStorage();
            using var property = new PersistentReactiveProperty<int>("test.value", 1, storage);

            property.Flush();

            Assert.That(storage.FlushCount, Is.EqualTo(1));
        }

        [Test]
        public void EmptyKey_ThrowsArgumentException()
        {
            var storage = new FakePersistentPropertyStorage();

            Assert.Throws<ArgumentException>(() => new PersistentReactiveProperty<int>("", 1, storage));
        }

        [Test]
        public void PlayerPrefsStorage_RoundTripsPersistentPropertyValue()
        {
            string key = $"gcbc.tests.persistentReactiveProperty.{Guid.NewGuid():N}";

            try
            {
                using (var first = new PersistentReactiveProperty<ETestSetting>(key, ETestSetting.First))
                {
                    first.Value = ETestSetting.Second;
                    first.Flush();
                }

                using var second = new PersistentReactiveProperty<ETestSetting>(key, ETestSetting.First);

                Assert.That(second.Value, Is.EqualTo(ETestSetting.Second));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        private sealed class FakePersistentPropertyStorage : IPersistentPropertyStorage
        {
            private readonly Dictionary<string, object> _values = new();
            private readonly Dictionary<string, int> _saveCounts = new();

            public int FlushCount { get; private set; }

            public bool HasKey(string key)
            {
                return _values.ContainsKey(key);
            }

            public T Load<T>(string key, T defaultValue = default)
            {
                if (_values.TryGetValue(key, out object value))
                    return (T)value;

                return defaultValue;
            }

            public void Save<T>(string key, T value)
            {
                _values[key] = value;
                _saveCounts[key] = SaveCount(key) + 1;
            }

            public void Delete(string key)
            {
                _values.Remove(key);
            }

            public void Flush()
            {
                FlushCount++;
            }

            public int SaveCount(string key)
            {
                return _saveCounts.TryGetValue(key, out int count) ? count : 0;
            }
        }
    }
}