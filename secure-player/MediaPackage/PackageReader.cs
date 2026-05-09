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
            long videoOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(32, 8));
            long videoLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(40, 8));
            long hashOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(48, 8));
            long hashLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(56, 8));

            if (hashLength != PackageConstants.Sha256HashSize)
                return false;

            if (!IsValidRange(metadataOffset, metadataLength, stream.Length))
                return false;

            if (!IsValidRange(videoOffset, videoLength, stream.Length))
                return false;

            if (!IsValidRange(hashOffset, hashLength, stream.Length))
                return false;

            long packageEnd = stream.Length - PackageConstants.FooterSize;

            if (metadataOffset + metadataLength > packageEnd)
                return false;

            if (videoOffset + videoLength > packageEnd)
                return false;

            if (hashOffset + hashLength > packageEnd)
                return false;

            if (metadataLength > int.MaxValue || videoLength > int.MaxValue)
                throw new InvalidOperationException("Package section is too large for current loader.");

            byte[] metadataBytes = ReadRange(stream, metadataOffset, (int)metadataLength);
            byte[] videoBytes = ReadRange(stream, videoOffset, (int)videoLength);
            byte[] expectedHash = ReadRange(stream, hashOffset, (int)hashLength);

            if (!VerifyPackageHash(metadataBytes, videoBytes, expectedHash))
                throw new InvalidOperationException("Package integrity check failed.");

            PackageMetadata? metadata = JsonSerializer.Deserialize<PackageMetadata>(metadataBytes);
            if (metadata == null)
                return false;

            payload = new PackagePayload
            {
                Metadata = metadata,
                VideoBytes = videoBytes
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

        private static bool VerifyPackageHash(byte[] metadataBytes, byte[] videoBytes, byte[] expectedHash)
        {
            using SHA256 sha256 = SHA256.Create();

            byte[] combined = new byte[metadataBytes.Length + videoBytes.Length];

            Buffer.BlockCopy(metadataBytes, 0, combined, 0, metadataBytes.Length);
            Buffer.BlockCopy(videoBytes, 0, combined, metadataBytes.Length, videoBytes.Length);

            byte[] actualHash = sha256.ComputeHash(combined);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
