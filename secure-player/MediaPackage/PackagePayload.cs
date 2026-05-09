using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal sealed class PackagePayload
    {
        public required PackageMetadata Metadata { get; init; }
        public required byte[] EncryptedVideoBytes { get; init; }
        public required byte[] Salt { get; init; }
        public required byte[] Nonce { get; init; }
        public required byte[] Tag { get; init; }
    }
}
