using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.Playback
{
    public sealed class VideoFrame
    {
        public required byte[] BgraBuffer { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int Stride { get; init; }
        public required TimeSpan Timestamp { get; init; }
    }
}
