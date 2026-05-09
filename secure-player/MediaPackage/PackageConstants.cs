using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal static class PackageConstants
    {
        public const string FooterMagic = "SPKGFOOT";
        public const int FooterSize = 112;
        public const int Version = 4;

        public const int Sha256HashSize = 32;
        public const int SaltSize = 16;
        public const int NonceSize = 12;
        public const int TagSize = 16;

        public const int Pbkdf2Iterations = 200_000;
        public const int AesKeySize = 32;
    }
}
