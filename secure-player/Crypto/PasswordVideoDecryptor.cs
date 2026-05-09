using secure_player.MediaPackage;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace secure_player.Crypto
{
    internal static class PasswordVideoDecryptor
    {
        public static byte[] Decrypt(
            byte[] encryptedVideoBytes,
            byte[] salt,
            byte[] nonce,
            byte[] tag,
            string password)
        {
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                PackageConstants.Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                PackageConstants.AesKeySize);

            byte[] plaintext = new byte[encryptedVideoBytes.Length];

            try
            {
                using AesGcm aes = new(key, PackageConstants.TagSize);
                aes.Decrypt(nonce, encryptedVideoBytes, tag, plaintext);
                return plaintext;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
