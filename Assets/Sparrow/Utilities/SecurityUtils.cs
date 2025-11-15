// 
// Copyright (c) 2024 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
//

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Sparrow.Utilities
{
    public class SecurityUtils
    {
        private const string CryptoKey = "YgievgP7rpg6wbWd5qQrAwBdGzgHeMcY";

        public static string EncryptString(string plainText, string key)
        {
            byte[] salt = GenerateSalt();
            key = ApplySaltToKey(key, salt);
            byte[] plainData = Encoding.UTF8.GetBytes(plainText);
            byte[] keyData = GenerateKey(key, plainData.Length);
            byte[] encryptedData = Encrypt(plainData, keyData);
            byte[] result = new byte[salt.Length + encryptedData.Length];
            Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
            Buffer.BlockCopy(encryptedData, 0, result, salt.Length, encryptedData.Length);
            return Convert.ToBase64String(result);
        }

        public static string DecryptString(string cipherText, string key)
        {
            byte[] encryptedWithSalt = Convert.FromBase64String(cipherText);
            byte[] salt = new byte[16];
            byte[] encryptedData = new byte[encryptedWithSalt.Length - salt.Length];
            Buffer.BlockCopy(encryptedWithSalt, 0, salt, 0, salt.Length);
            Buffer.BlockCopy(encryptedWithSalt, salt.Length, encryptedData, 0, encryptedData.Length);
            key = ApplySaltToKey(key, salt);
            byte[] keyData = GenerateKey(key, encryptedData.Length);
            byte[] decryptedData = Decrypt(encryptedData, keyData);
            return Encoding.UTF8.GetString(decryptedData);
        }

        private static byte[] Encrypt(byte[] plainData, byte[] key)
        {
            byte[] encryptedData = new byte[plainData.Length];
            for (int i = 0; i < plainData.Length; i++)
            {
                encryptedData[i] = (byte)(plainData[i] ^ key[i]);
            }
            return encryptedData;
        }

        private static byte[] Decrypt(byte[] encryptedData, byte[] key)
        {
            return Encrypt(encryptedData, key); // XOR ist symmetrisch
        }

        private static byte[] GenerateKey(string key, int length)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] keyData = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                byte[] extendedKey = new byte[length];
                for (int i = 0; i < length; i++)
                {
                    extendedKey[i] = keyData[i % keyData.Length];
                }
                return extendedKey;
            }
        }

        private static byte[] GenerateSalt()
        {
            byte[] salt = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }
            return salt;
        }

        private static string ApplySaltToKey(string key, byte[] salt)
        {
            return Convert.ToBase64String(salt) + key;
        }

        public static string GenerateRandomKey(int length = 32)
        {
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!§$%&/()=?";
            StringBuilder sb = new StringBuilder();
            System.Random rand = new System.Random();
            while (0 < length--)
            {
                sb.Append(validChars[rand.Next(validChars.Length)]);
            }
            return sb.ToString();
        }

        public static string GenerateKey(string externalKey)
        {
            return CombineKeys(externalKey, CryptoKey);
        }

        private static string CombineKeys(string key1, string key2)
        {
            string ret = "";
            for (int i = 0; i < Math.Max(key1.Length, key2.Length); i++)
                ret += ((i < key1.Length ? key1[i] : 'w') + (i < key2.Length ? key2[i] : 'o'));
            return ret;
        }
    }
}