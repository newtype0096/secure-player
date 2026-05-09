using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace secure_player.MediaPackage
{
    internal static class PackageReader
    {
        public static bool TryReadEmbeddedPackage(string exePath, out PackagePayload? payload)
        {
            payload = null;

            using FileStream stream = File.OpenRead(exePath);

            if (stream.Length < PackageConstants.FooterSize)
                return false;

            stream.Position = stream.Length - PackageConstants.FooterSize;

            Span<byte> footerBuffer = stackalloc byte[PackageConstants.FooterSize];
            int read = stream.Read(footerBuffer);

            if (read != PackageConstants.FooterSize)
                return false;

            string magic = Encoding.ASCII.GetString(footerBuffer[..8]);
            if (magic != PackageConstants.FooterMagic)
                return false;

            int version = BinaryPrimitives.ReadInt32LittleEndian(footerBuffer.Slice(8, 4));
            if (version != PackageConstants.Version)
                return false;

            long metadataOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(16, 8));
            long metadataLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(24, 8));

            long encryptedVideoOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(32, 8));
            long encryptedVideoLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(40, 8));

            long saltOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(48, 8));
            long saltLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(56, 8));

            long nonceOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(64, 8));
            long nonceLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(72, 8));

            long tagOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(80, 8));
            long tagLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(88, 8));

            long hashOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(96, 8));
            long hashLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(104, 8));

            if (saltLength != PackageConstants.SaltSize)
                return false;

            if (nonceLength != PackageConstants.NonceSize)
                return false;

            if (tagLength != PackageConstants.TagSize)
                return false;

            if (hashLength != PackageConstants.Sha256HashSize)
                return false;

            if (!IsValidRange(metadataOffset, metadataLength, stream.Length))
                return false;

            if (!IsValidRange(encryptedVideoOffset, encryptedVideoLength, stream.Length))
                return false;

            if (!IsValidRange(saltOffset, saltLength, stream.Length))
                return false;

            if (!IsValidRange(nonceOffset, nonceLength, stream.Length))
                return false;

            if (!IsValidRange(tagOffset, tagLength, stream.Length))
                return false;

            if (!IsValidRange(hashOffset, hashLength, stream.Length))
                return false;

            long packageEnd = stream.Length - PackageConstants.FooterSize;

            if (metadataOffset + metadataLength > packageEnd)
                return false;

            if (encryptedVideoOffset + encryptedVideoLength > packageEnd)
                return false;

            if (saltOffset + saltLength > packageEnd)
                return false;

            if (nonceOffset + nonceLength > packageEnd)
                return false;

            if (tagOffset + tagLength > packageEnd)
                return false;

            if (hashOffset + hashLength > packageEnd)
                return false;

            if (metadataLength > int.MaxValue || encryptedVideoLength > int.MaxValue)
                throw new InvalidOperationException("Package section is too large for current loader.");

            byte[] metadataBytes = ReadRange(stream, metadataOffset, (int)metadataLength);
            byte[] encryptedVideoBytes = ReadRange(stream, encryptedVideoOffset, (int)encryptedVideoLength);
            byte[] salt = ReadRange(stream, saltOffset, (int)saltLength);
            byte[] nonce = ReadRange(stream, nonceOffset, (int)nonceLength);
            byte[] tag = ReadRange(stream, tagOffset, (int)tagLength);
            byte[] expectedHash = ReadRange(stream, hashOffset, (int)hashLength);

            if (!VerifyPackageHash(metadataBytes, encryptedVideoBytes, salt, nonce, tag, expectedHash))
                throw new InvalidOperationException("Package integrity check failed.");

            PackageMetadata? metadata = JsonSerializer.Deserialize<PackageMetadata>(metadataBytes);
            if (metadata == null)
                return false;

            payload = new PackagePayload
            {
                Metadata = metadata,
                EncryptedVideoBytes = encryptedVideoBytes,
                Salt = salt,
                Nonce = nonce,
                Tag = tag
            };

            return true;
        }

        private static bool IsValidRange(long offset, long length, long fileLength)
        {
            if (offset < 0 || length <= 0)
                return false;

            if (offset > fileLength)
                return false;

            if (length > fileLength - offset)
                return false;

            return true;
        }

        private static byte[] ReadRange(FileStream stream, long offset, int length)
        {
            byte[] buffer = new byte[length];

            stream.Position = offset;

            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                    throw new EndOfStreamException();

                totalRead += read;
            }

            return buffer;
        }

        private static bool VerifyPackageHash(
            byte[] metadataBytes,
            byte[] encryptedVideoBytes,
            byte[] salt,
            byte[] nonce,
            byte[] tag,
            byte[] expectedHash)
        {
            using SHA256 sha256 = SHA256.Create();

            int length =
                metadataBytes.Length +
                encryptedVideoBytes.Length +
                salt.Length +
                nonce.Length +
                tag.Length;

            byte[] combined = new byte[length];

            int offset = 0;

            Buffer.BlockCopy(metadataBytes, 0, combined, offset, metadataBytes.Length);
            offset += metadataBytes.Length;

            Buffer.BlockCopy(encryptedVideoBytes, 0, combined, offset, encryptedVideoBytes.Length);
            offset += encryptedVideoBytes.Length;

            Buffer.BlockCopy(salt, 0, combined, offset, salt.Length);
            offset += salt.Length;

            Buffer.BlockCopy(nonce, 0, combined, offset, nonce.Length);
            offset += nonce.Length;

            Buffer.BlockCopy(tag, 0, combined, offset, tag.Length);

            byte[] actualHash = sha256.ComputeHash(combined);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
