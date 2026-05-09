using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.MediaPackage
{
    internal sealed class PackageMetadata
    {
        public string? Title { get; set; }
        public string? OriginalFileName { get; set; }
        public string? ContentType { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
