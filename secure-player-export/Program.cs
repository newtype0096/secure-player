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

static void VerifyPackage(string packedExePath)
{
    using FileStream stream = File.OpenRead(packedExePath);

    const string footerMagic = "SPKGFOOT";
    const int footerSize = 64;
    const int version = 3;
    const int sha256HashSize = 32;

    if (stream.Length < footerSize)
        throw new InvalidOperationException("Package footer not found.");

    stream.Position = stream.Length - footerSize;

    Span<byte> footerBuffer = stackalloc byte[footerSize];
    int read = stream.Read(footerBuffer);

    if (read != footerSize)
        throw new InvalidOperationException("Failed to read package footer.");

    string magic = Encoding.ASCII.GetString(footerBuffer[..8]);
    if (magic != footerMagic)
        throw new InvalidOperationException("Invalid package magic.");

    int actualVersion = BinaryPrimitives.ReadInt32LittleEndian(footerBuffer.Slice(8, 4));
    if (actualVersion != version)
        throw new InvalidOperationException($"Unsupported package version: {actualVersion}");

    long metadataOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(16, 8));
    long metadataLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(24, 8));
    long videoOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(32, 8));
    long videoLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(40, 8));
    long hashOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(48, 8));
    long hashLength = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer.Slice(56, 8));

    long packageEnd = stream.Length - footerSize;

    if (!IsValidPackageRange(metadataOffset, metadataLength, packageEnd))
        throw new InvalidOperationException("Invalid metadata range.");

    if (!IsValidPackageRange(videoOffset, videoLength, packageEnd))
        throw new InvalidOperationException("Invalid video range.");

    if (!IsValidPackageRange(hashOffset, hashLength, packageEnd))
        throw new InvalidOperationException("Invalid hash range.");

    if (hashLength != sha256HashSize)
        throw new InvalidOperationException($"Invalid hash length: {hashLength}");

    if (metadataLength > int.MaxValue || videoLength > int.MaxValue || hashLength > int.MaxValue)
        throw new InvalidOperationException("Package section is too large for current verifier.");

    byte[] metadataBytes = ReadRange(stream, metadataOffset, (int)metadataLength);
    byte[] videoBytes = ReadRange(stream, videoOffset, (int)videoLength);
    byte[] expectedHash = ReadRange(stream, hashOffset, (int)hashLength);

    if (!VerifyPackageHash(metadataBytes, videoBytes, expectedHash))
        throw new InvalidOperationException("Package integrity check failed.");

    using JsonDocument metadata = JsonDocument.Parse(metadataBytes);

    Console.WriteLine("Package valid");
    Console.WriteLine($"MetadataLength: {metadataLength}");
    Console.WriteLine($"VideoLength: {videoLength}");
    Console.WriteLine($"HashLength: {hashLength}");

    if (metadata.RootElement.TryGetProperty("Title", out JsonElement title))
        Console.WriteLine($"Title: {title.GetString()}");

    if (metadata.RootElement.TryGetProperty("OriginalFileName", out JsonElement originalFileName))
        Console.WriteLine($"OriginalFileName: {originalFileName.GetString()}");

    if (metadata.RootElement.TryGetProperty("ContentType", out JsonElement contentType))
        Console.WriteLine($"ContentType: {contentType.GetString()}");

    if (metadata.RootElement.TryGetProperty("CreatedAtUtc", out JsonElement createdAtUtc))
        Console.WriteLine($"CreatedAtUtc: {createdAtUtc.GetString()}");
}

static bool IsValidPackageRange(long offset, long length, long packageEnd)
{
    if (offset < 0 || length <= 0)
        return false;

    if (offset > packageEnd)
        return false;

    if (length > packageEnd - offset)
        return false;

    return true;
}

static byte[] ReadRange(FileStream stream, long offset, int length)
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

static bool VerifyPackageHash(byte[] metadataBytes, byte[] videoBytes, byte[] expectedHash)
{
    using SHA256 sha256 = SHA256.Create();

    byte[] combined = new byte[metadataBytes.Length + videoBytes.Length];

    Buffer.BlockCopy(metadataBytes, 0, combined, 0, metadataBytes.Length);
    Buffer.BlockCopy(videoBytes, 0, combined, metadataBytes.Length, videoBytes.Length);

    byte[] actualHash = sha256.ComputeHash(combined);

    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
}

if (args.Length >= 1 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: secure-player-export verify <packed-exe>");
        Environment.ExitCode = 1;
        return;
    }

    try
    {
        VerifyPackage(args[1]);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Package invalid: {ex.Message}");
        Environment.ExitCode = 1;
    }

    return;
}

if (args.Length >= 1 && args[0].Equals("pack", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length != 4)
    {
        Console.Error.WriteLine("Usage: secure-player-export pack <player-exe> <video-file> <output-exe>");
        Environment.ExitCode = 1;
        return;
    }

    string playerExePath = args[1];
    string videoPath = args[2];
    string outputExePath = args[3];

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
}