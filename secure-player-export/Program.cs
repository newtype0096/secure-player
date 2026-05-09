using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const int SaltSize = 16;
const int NonceSize = 12;
const int TagSize = 16;
const int AesKeySize = 32;
const int Pbkdf2Iterations = 200_000;

static (byte[] EncryptedVideoBytes, byte[] Salt, byte[] Nonce, byte[] Tag)
EncryptVideo(byte[] videoBytes, string password)
{
    byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
    byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
    byte[] tag = new byte[TagSize];
    byte[] encrypted = new byte[videoBytes.Length];

    byte[] key = Rfc2898DeriveBytes.Pbkdf2(
        password,
        salt,
        Pbkdf2Iterations,
        HashAlgorithmName.SHA256,
        AesKeySize);

    using AesGcm aes = new(key, TagSize);
    aes.Encrypt(nonce, videoBytes, encrypted, tag);

    CryptographicOperations.ZeroMemory(key);

    return (encrypted, salt, nonce, tag);
}

static string ReadPassword()
{
    Console.Write("Password: ");

    StringBuilder password = new();

    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
                Console.Write("\b \b");
            }

            continue;
        }

        password.Append(key.KeyChar);
        Console.Write("*");
    }

    return password.ToString();
}

static byte[] ComputePackageHash(byte[] metadataBytes, byte[] encryptedVideoBytes, byte[] salt, byte[] nonce, byte[] tag)
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

    return sha256.ComputeHash(combined);
}

static void VerifyPackage(string packedExePath)
{
    using FileStream stream = File.OpenRead(packedExePath);

    const string footerMagic = "SPKGFOOT";
    const int footerSize = 112;
    const int version = 4;
    const int sha256HashSize = 32;
    const int saltSize = 16;
    const int nonceSize = 12;
    const int tagSize = 16;

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

    long packageEnd = stream.Length - footerSize;

    if (!IsValidPackageRange(metadataOffset, metadataLength, packageEnd))
        throw new InvalidOperationException("Invalid metadata range.");

    if (!IsValidPackageRange(encryptedVideoOffset, encryptedVideoLength, packageEnd))
        throw new InvalidOperationException("Invalid video range.");

    if (!IsValidPackageRange(saltOffset, saltLength, packageEnd))
        throw new InvalidOperationException("Invalid salt range.");

    if (!IsValidPackageRange(nonceOffset, nonceLength, packageEnd))
        throw new InvalidOperationException("Invalid nonce range.");

    if (!IsValidPackageRange(tagOffset, tagLength, packageEnd))
        throw new InvalidOperationException("Invalid tag range.");

    if (!IsValidPackageRange(hashOffset, hashLength, packageEnd))
        throw new InvalidOperationException("Invalid hash range.");

    if (saltLength != saltSize)
        throw new InvalidOperationException($"Invalid salt length: {saltLength}");

    if (nonceLength != nonceSize)
        throw new InvalidOperationException($"Invalid nonce length: {nonceLength}");

    if (tagLength != tagSize)
        throw new InvalidOperationException($"Invalid tag length: {tagLength}");

    if (hashLength != sha256HashSize)
        throw new InvalidOperationException($"Invalid hash length: {hashLength}");

    if (metadataLength > int.MaxValue || encryptedVideoLength > int.MaxValue || hashLength > int.MaxValue)
        throw new InvalidOperationException("Package section is too large for current verifier.");

    byte[] metadataBytes = ReadRange(stream, metadataOffset, (int)metadataLength);
    byte[] encryptedVideoBytes = ReadRange(stream, encryptedVideoOffset, (int)encryptedVideoLength);
    byte[] salt = ReadRange(stream, saltOffset, (int)saltLength);
    byte[] nonce = ReadRange(stream, nonceOffset, (int)nonceLength);
    byte[] tag = ReadRange(stream, tagOffset, (int)tagLength);
    byte[] expectedHash = ReadRange(stream, hashOffset, (int)hashLength);

    if (!VerifyPackageHash(metadataBytes, encryptedVideoBytes, salt, nonce, tag, expectedHash))
        throw new InvalidOperationException("Package integrity check failed.");

    using JsonDocument metadata = JsonDocument.Parse(metadataBytes);

    Console.WriteLine("Package valid");
    Console.WriteLine($"MetadataLength: {metadataLength}");
    Console.WriteLine($"VideoLength: {encryptedVideoLength}");
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

static bool VerifyPackageHash(
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

    string password = ReadPassword();
    Console.Write("Confirm ");
    string confirm = ReadPassword();

    if (password != confirm)
        throw new InvalidOperationException("Passwords do not match.");

    var encrypted = EncryptVideo(videoBytes, password);

    byte[] encryptedVideoBytes = encrypted.EncryptedVideoBytes;
    byte[] salt = encrypted.Salt;
    byte[] nonce = encrypted.Nonce;
    byte[] tag = encrypted.Tag;

    byte[] hashBytes = ComputePackageHash(metadataBytes, encryptedVideoBytes, salt, nonce, tag);

    long metadataOffset = playerBytes.Length;
    long metadataLength = metadataBytes.Length;

    long encryptedVideoOffset = metadataOffset + metadataLength;
    long encryptedVideoLength = encryptedVideoBytes.Length;

    long saltOffset = encryptedVideoOffset + encryptedVideoLength;
    long saltLength = salt.Length;

    long nonceOffset = saltOffset + saltLength;
    long nonceLength = nonce.Length;

    long tagOffset = nonceOffset + nonceLength;
    long tagLength = tag.Length;

    long hashOffset = tagOffset + tagLength;
    long hashLength = hashBytes.Length;

    using FileStream output = File.Create(outputExePath);

    output.Write(playerBytes);
    output.Write(metadataBytes);
    output.Write(encryptedVideoBytes);
    output.Write(salt);
    output.Write(nonce);
    output.Write(tag);
    output.Write(hashBytes);

    Span<byte> footer = stackalloc byte[112];

    Encoding.ASCII.GetBytes("SPKGFOOT").CopyTo(footer);
    BinaryPrimitives.WriteInt32LittleEndian(footer.Slice(8, 4), 4);
    BinaryPrimitives.WriteInt32LittleEndian(footer.Slice(12, 4), 0);

    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(16, 8), metadataOffset);
    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(24, 8), metadataLength);

    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(32, 8), encryptedVideoOffset);
    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(40, 8), encryptedVideoLength);

    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(48, 8), saltOffset);
    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(56, 8), saltLength);

    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(64, 8), nonceOffset);
    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(72, 8), nonceLength);

    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(80, 8), tagOffset);
    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(88, 8), tagLength);

    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(96, 8), hashOffset);
    BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(104, 8), hashLength);

    output.Write(footer);
}