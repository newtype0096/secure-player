using System.Buffers.Binary;
using System.Text;

string playerExePath = args[0];
string videoPath = args[1];
string outputExePath = args[2];

byte[] playerBytes = File.ReadAllBytes(playerExePath);
byte[] videoBytes = File.ReadAllBytes(videoPath);

long videoOffset = playerBytes.Length;
long videoLength = videoBytes.Length;

using FileStream output = File.Create(outputExePath);

output.Write(playerBytes);
output.Write(videoBytes);

Span<byte> footer = stackalloc byte[32];

Encoding.ASCII.GetBytes("SPKGFOOT").CopyTo(footer);
BinaryPrimitives.WriteInt32LittleEndian(footer.Slice(8, 4), 1);
BinaryPrimitives.WriteInt32LittleEndian(footer.Slice(12, 4), 0);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(16, 8), videoOffset);
BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(24, 8), videoLength);

output.Write(footer);