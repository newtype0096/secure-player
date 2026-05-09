using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static byte[] ComputePackageHash(byte[] metadataBytes, byte[] videoBytes)
{
    using SHA256 sha256 = SHA256.Create();

    byte[] combined = new byte[metadataBytes.Length + videoBytes.Length];

    Buffer.BlockCopy(metadataBytes, 0, combined, 0, metadataBytes.Length);
    Buffer.BlockCopy(videoBytes, 0, combined, metadataBytes.Length, videoBytes.Length);

    return sha256.ComputeHash(combined);
}

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: secure-player-export <player-exe> <video-file> <output-exe>");
    return;
}

string playerExePath = args[0];
string videoPath = args[1];
string outputExePath = args[2];

byte[] playerBytes = File.ReadAllBytes(playerExePath);
byte[] videoBytes = File.ReadAllBytes(videoPath);

var metadata = new
{
    Title = Path.GetFileNameWithoutExtension(videoPath),
    OriginalFileName = Path.GetFileName(videoPath),
    ContentType = "video/mp4",
    CreatedAtUtc = DateTime.UtcNow
};

byte[] metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, new JsonSerializerOptions
{
    WriteIndented = false
});

byte[] hashBytes = ComputePackageHash(metadataBytes, videoBytes);

long metadataOffset = playerBytes.Length;
long metadataLength = metadataBytes.Length;
long videoOffset = metadataOffset + metadataLength;
long videoLength = videoBytes.Length;
long hashOffset = videoOffset + videoLength;
long hashLength = hashBytes.Length;

using FileStream output = File.Create(outputExePath);

output.Write(playerBytes);
output.Write(metadataBytes);
output.Write(videoBytes);
output.Write(hashBytes);

Span<byte> footer = stackalloc byte[64];

Encoding.ASCII.GetBytes("SPKGFOOT").CopyTo(footer);
BinaryPrimitives.WriteInt32LittleEndian(footer.Slice(8, 4), 3);
BinaryPrimitives.WriteInt32LittleEndian(footer.Slice(12, 4), 0);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(16, 8), metadataOffset);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(24, 8), metadataLength);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(32, 8), videoOffset);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(40, 8), videoLength);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(48, 8), hashOffset);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(56, 8), hashLength);

output.Write(footer);