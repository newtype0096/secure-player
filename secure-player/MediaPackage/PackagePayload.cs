using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal sealed class PackagePayload
    {
        public required PackageMetadata Metadata { get; init; }
        public required byte[] VideoBytes { get; init; }
    }
}
