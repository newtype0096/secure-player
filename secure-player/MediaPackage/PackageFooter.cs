using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal readonly record struct PackageFooter(
        int Version,
        long MetadataOffset,
        long MetadataLength,
        long EncryptedVideoOffset,
        long EncryptedVideoLength,
        long SaltOffset,
        long SaltLength,
        long NonceOffset,
        long NonceLength,
        long TagOffset,
        long TagLength,
        long HashOffset,
        long HashLength);
}
