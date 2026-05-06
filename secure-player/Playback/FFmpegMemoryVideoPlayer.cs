using FFmpeg.AutoGen;
using secure_player.Native;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace secure_player.Playback
{
    public unsafe sealed class FFmpegMemoryVideoPlayer : IVideoPlayer
    {
        public event EventHandler<VideoFrame>? FrameReady;
        public event EventHandler<TimeSpan>? PositionChanged;

        private MemoryMediaSource? _source;
        private AVFormatContext* _formatContext;
        private AVCodecContext* _codecContext;
        private AVIOContext* _avioContext;
        private byte* _avioBuffer;

        private int _videoStreamIndex = -1;
        private CancellationTokenSource? _playbackCts;
        private Task? _playbackTask;
        private bool _isPlaying;
        private double _speed = 1.0;

        private avio_alloc_context_read_packet? _readPacket;
        private avio_alloc_context_seek? _seek;

        public TimeSpan Duration { get; private set; }
        public TimeSpan Position { get; private set; }
        public bool IsPlaying => _isPlaying;

        public Task OpenAsync(byte[] mediaBytes, CancellationToken cancellationToken = default)
        {
            FFmpegLibraryLoader.Initialize();

            CloseCurrent();

            _source = new MemoryMediaSource(mediaBytes);

            _readPacket = ReadPacket;
            _seek = Seek;

            int avioBufferSize = 64 * 1024;
            _avioBuffer = (byte*)ffmpeg.av_malloc((ulong)avioBufferSize);

            _avioContext = ffmpeg.avio_alloc_context(
                _avioBuffer,
                avioBufferSize,
                0,
                null,
                _readPacket,
                null,
                _seek);

            _formatContext = ffmpeg.avformat_alloc_context();
            _formatContext->pb = _avioContext;
            _formatContext->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

            AVFormatContext* ctx = _formatContext;

            int openResult = ffmpeg.avformat_open_input(&ctx, null, null, null);
            ThrowIfError(openResult);

            _formatContext = ctx;

            ThrowIfError(ffmpeg.avformat_find_stream_info(_formatContext, null));

            _videoStreamIndex = ffmpeg.av_find_best_stream(
                _formatContext,
                AVMediaType.AVMEDIA_TYPE_VIDEO,
                -1,
                -1,
                null,
                0);

            if (_videoStreamIndex < 0)
                throw new InvalidOperationException("Video stream not found.");

            AVStream* stream = _formatContext->streams[_videoStreamIndex];
            AVCodecParameters* codecParameters = stream->codecpar;

            AVCodec* codec = ffmpeg.avcodec_find_decoder(codecParameters->codec_id);
            if (codec == null)
                throw new InvalidOperationException("Decoder not found.");

            _codecContext = ffmpeg.avcodec_alloc_context3(codec);
            ThrowIfError(ffmpeg.avcodec_parameters_to_context(_codecContext, codecParameters));
            ThrowIfError(ffmpeg.avcodec_open2(_codecContext, codec, null));

            if (_formatContext->duration > 0)
                Duration = TimeSpan.FromSeconds(_formatContext->duration / (double)ffmpeg.AV_TIME_BASE);

            return Task.CompletedTask;
        }

        public void Play()
        {
            if (_isPlaying)
                return;

            _isPlaying = true;
            _playbackCts = new CancellationTokenSource();
            _playbackTask = Task.Run(() => DecodeLoop(_playbackCts.Token));
        }

        public void Pause()
        {
            _isPlaying = false;
            _playbackCts?.Cancel();
        }

        public void Stop()
        {
            Pause();
            Position = TimeSpan.Zero;
        }

        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            if (_formatContext == null || _videoStreamIndex < 0)
                return Task.CompletedTask;

            long timestamp = (long)(position.TotalSeconds / ffmpeg.av_q2d(_formatContext->streams[_videoStreamIndex]->time_base));

            int result = ffmpeg.av_seek_frame(
                _formatContext,
                _videoStreamIndex,
                timestamp,
                ffmpeg.AVSEEK_FLAG_BACKWARD);

            ThrowIfError(result);
            ffmpeg.avcodec_flush_buffers(_codecContext);

            Position = position;
            PositionChanged?.Invoke(this, Position);

            return Task.CompletedTask;
        }

        public void SetSpeed(double speed)
        {
            _speed = Math.Clamp(speed, 0.25, 4.0);
        }

        private int ReadPacket(void* opaque, byte* buffer, int bufferSize)
        {
            if (_source == null)
                return ffmpeg.AVERROR_EOF;

            Span<byte> destination = new(buffer, bufferSize);
            int read = _source.Read(destination);

            return read == 0 ? ffmpeg.AVERROR_EOF : read;
        }

        private long Seek(void* opaque, long offset, int whence)
        {
            return _source?.Seek(offset, whence) ?? -1;
        }

        private void DecodeLoop(CancellationToken cancellationToken)
        {

        }

        private static void ThrowIfError(int error)
        {
            if (error >= 0)
                return;

            Span<byte> buffer = stackalloc byte[1024];
            fixed (byte* ptr = buffer)
            {
                ffmpeg.av_strerror(error, ptr, (ulong)buffer.Length);
                string message = Marshal.PtrToStringAnsi((IntPtr)ptr) ?? $"FFmpeg error {error}";
                throw new InvalidOperationException(message);
            }
        }

        private void CloseCurrent()
        {
            Pause();

            if (_codecContext != null)
            {
                AVCodecContext* codec = _codecContext;
                ffmpeg.avcodec_free_context(&codec);
                _codecContext = null;
            }

            if (_formatContext != null)
            {
                AVFormatContext* format = _formatContext;
                ffmpeg.avformat_close_input(&format);
                _formatContext = null;
            }

            if (_avioContext != null)
            {
                AVIOContext* avio = _avioContext;
                ffmpeg.avio_context_free(&avio);
                _avioContext = null;
            }
        }

        public void Dispose()
        {
            CloseCurrent();
            _playbackCts?.Dispose();
        }
    }
}
