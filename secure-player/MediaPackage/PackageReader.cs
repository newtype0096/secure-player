using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace secure_player.MediaPackage
{
    internal static class PackageReader
    {
        public static bool TryReadEmbeddedVideo(string exePath, out byte[] videoBytes)
        {
            videoBytes = [];

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

            long videoOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(16, 8));
            long videoLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(24, 8));

            if (videoOffset < 0 || videoLength <= 0)
                return false;

            if (videoOffset + videoLength > stream.Length - PackageConstants.FooterSize)
                return false;

            if (videoLength > int.MaxValue)
                throw new InvalidOperationException("Video is too large for current byte[] loader.");

            videoBytes = new byte[videoLength];

            stream.Position = videoOffset;
            int totalRead = 0;

            while (totalRead < videoBytes.Length)
            {
                int chunk = stream.Read(videoBytes, totalRead, videoBytes.Length - totalRead);
                if (chunk == 0)
                    throw new EndOfStreamException();

                totalRead += chunk;
            }

            return true;
        }
    }
}
