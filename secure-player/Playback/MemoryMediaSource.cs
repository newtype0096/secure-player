using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.Playback
{
    internal sealed class MemoryMediaSource
    {
        private readonly byte[] _buffer;
        private long _position;

        public MemoryMediaSource(byte[] buffer)
        {
            _buffer = buffer;
        }

        public int Read(Span<byte> destination)
        {
            long remaining = _buffer.Length - _position;
            if (remaining <= 0)
                return 0;

            int count = (int)Math.Min(destination.Length, remaining);
            _buffer.AsSpan((int)_position, count).CopyTo(destination);
            _position += count;
            return count;
        }

        public long Seek(long offset, int whence)
        {
            const int SeekSet = 0;
            const int SeekCur = 1;
            const int SeekEnd = 2;
            const int AvSeekSize = 0x10000;

            if (whence == AvSeekSize)
                return _buffer.Length;

            long next = whence switch
            {
                SeekSet => offset,
                SeekCur => _position + offset,
                SeekEnd => _buffer.Length + offset,
                _ => -1
            };

            if (next < 0 || next > _buffer.Length)
                return -1;

            _position = next;
            return _position;
        }
    }
}
