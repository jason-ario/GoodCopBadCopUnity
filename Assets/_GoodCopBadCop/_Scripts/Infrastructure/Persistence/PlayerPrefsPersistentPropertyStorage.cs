using System;
using System.Globalization;
using UnityEngine;

namespace GoodCopBadCop.Infrastructure.Persistence
{
    public sealed class PlayerPrefsPersistentPropertyStorage : IPersistentPropertyStorage
    {
        public static PlayerPrefsPersistentPropertyStorage Shared { get; } = new();

        public bool HasKey(string key)
        {
            ValidateKey(key);
            return PlayerPrefs.HasKey(key);
        }

        public T Load<T>(string key, T defaultValue = default)
        {
            ValidateKey(key);

            Type type = typeof(T);

            if (type == typeof(int))
                return (T)(object)PlayerPrefs.GetInt(key, (int)(object)defaultValue);

            if (type == typeof(long))
                return (T)(object)LoadLong(key, (long)(object)defaultValue);

            if (type == typeof(float))
                return (T)(object)PlayerPrefs.GetFloat(key, (float)(object)defaultValue);

            if (type == typeof(string))
                return (T)(object)PlayerPrefs.GetString(key, defaultValue as string ?? string.Empty);

            if (type == typeof(bool))
                return (T)(object)(PlayerPrefs.GetInt(key, (bool)(object)defaultValue ? 1 : 0) != 0);

            if (type.IsEnum)
                return LoadEnum(key, defaultValue);

            throw CreateUnsupportedTypeException(type);
        }

        public void Save<T>(string key, T value)
        {
            ValidateKey(key);

            Type type = typeof(T);

            if (type == typeof(int))
                PlayerPrefs.SetInt(key, (int)(object)value);
            else if (type == typeof(long))
                SaveLong(key, (long)(object)value);
            else if (type == typeof(float))
                PlayerPrefs.SetFloat(key, (float)(object)value);
            else if (type == typeof(string))
                PlayerPrefs.SetString(key, value as string ?? string.Empty);
            else if (type == typeof(bool))
                PlayerPrefs.SetInt(key, (bool)(object)value ? 1 : 0);
            else if (type.IsEnum)
                PlayerPrefs.SetString(key, value.ToString());
            else
                throw CreateUnsupportedTypeException(type);
        }

        public void Delete(string key)
        {
            ValidateKey(key);
            PlayerPrefs.DeleteKey(key);
        }

        public void Flush()
        {
            PlayerPrefs.Save();
        }

        private static long LoadLong(string key, long defaultValue)
        {
            string raw = PlayerPrefs.GetString(key, defaultValue.ToString(CultureInfo.InvariantCulture));

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return value;

            throw new NotSupportedException($"Failed to convert stored value '{raw}' for key '{key}' to long.");
        }

        private static void SaveLong(string key, long value)
        {
            PlayerPrefs.SetString(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static T LoadEnum<T>(string key, T defaultValue)
        {
            string raw = PlayerPrefs.GetString(key, defaultValue.ToString());

            try
            {
                object value = Enum.Parse(typeof(T), raw, true);

                if (Enum.IsDefined(typeof(T), value))
                    return (T)value;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            {
                throw CreateLoadEnumException<T>(key, raw, exception);
            }

            throw CreateLoadEnumException<T>(key, raw);
        }

        private static NotSupportedException CreateLoadEnumException<T>(string key, string raw, Exception innerException = null)
        {
            return new NotSupportedException(
                $"Failed to convert stored value '{raw}' for key '{key}' to {typeof(T).Name}.",
                innerException);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }

        private static NotSupportedException CreateUnsupportedTypeException(Type type)
        {
            return new NotSupportedException(
                $"Type {type} is not supported. Only int, long, float, string, bool, and enum are allowed.");
        }
    }
}