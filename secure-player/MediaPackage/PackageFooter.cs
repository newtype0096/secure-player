using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal readonly record struct PackageFooter(
        int Version,
        long MetadataOffset,
        long MetadataLength,
        long VideoOffset,
        long VideoLength,
        long HashOffset,
        long HashLength);
}
