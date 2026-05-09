using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal static class PackageConstants
    {
        public const string FooterMagic = "SPKGFOOT";
        public const int FooterSize = 64;
        public const int Version = 3;
        public const int Sha256HashSize = 32;
    }
}
