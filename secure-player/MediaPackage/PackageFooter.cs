using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal readonly record struct PackageFooter(
        int Version,
        long VideoOffset,
        long VideoLength);
}
