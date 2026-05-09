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

        private readonly object _stateLock = new();
        private bool _isOpened;
        private bool _reachedEnd;

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

            _isOpened = true;
            _reachedEnd = false;
            Position = TimeSpan.Zero;

            return Task.CompletedTask;
        }

        public void Play()
        {
            lock (_stateLock)
            {
                if (!_isOpened)
                    return;

                if (_isPlaying)
                    return;

                if (_playbackTask != null && !_playbackTask.IsCompleted)
                    return;

                if (_reachedEnd)
                {
                    SeekAsync(TimeSpan.Zero).GetAwaiter().GetResult();
                    _reachedEnd = false;
                }

                _playbackCts?.Dispose();
                _playbackCts = new CancellationTokenSource();

                _isPlaying = true;
                _playbackTask = Task.Run(() => DecodeLoop(_playbackCts.Token));
            }
        }

        public void Pause()
        {
            lock (_stateLock)
            {
                if (!_isPlaying)
                    return;

                _isPlaying = false;
                _playbackCts?.Cancel();
            }
        }

        public void Stop()
        {
            Pause();

            if (!_isOpened)
                return;

            SeekAsync(TimeSpan.Zero).GetAwaiter().GetResult();

            _reachedEnd = false;
            Position = TimeSpan.Zero;
            PositionChanged?.Invoke(this, Position);
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
            AVPacket* packet = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();
            AVFrame* bgraFrame = ffmpeg.av_frame_alloc();
            SwsContext* swsContext = null;

            try
            {
                int width = _codecContext->width;
                int height = _codecContext->height;
                int stride = width * 4;
                int bufferSize = stride * height;

                byte[] managedBuffer = new byte[bufferSize];

                fixed (byte* bufferPtr = managedBuffer)
                {
                    byte_ptrArray4 dstData = default;
                    int_array4 dstLinesize = default;

                    dstData[0] = bufferPtr;
                    dstLinesize[0] = stride;

                    swsContext = ffmpeg.sws_getContext(
                        width,
                        height,
                        _codecContext->pix_fmt,
                        width,
                        height,
                        AVPixelFormat.AV_PIX_FMT_BGRA,
                        (int)SwsFlags.SWS_BILINEAR,
                        null,
                        null,
                        null);

                    if (swsContext == null)
                        throw new InvalidOperationException("Failed to create sws context.");

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int readResult = ffmpeg.av_read_frame(_formatContext, packet);

                        if (readResult == ffmpeg.AVERROR_EOF)
                        {
                            _reachedEnd = true;
                            break;
                        }

                        if (readResult < 0)
                            break;

                        try
                        {
                            if (packet->stream_index != _videoStreamIndex)
                                continue;

                            ThrowIfError(ffmpeg.avcodec_send_packet(_codecContext, packet));

                            while (!cancellationToken.IsCancellationRequested)
                            {
                                int receiveResult = ffmpeg.avcodec_receive_frame(_codecContext, frame);

                                if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) ||
                                    receiveResult == ffmpeg.AVERROR_EOF)
                                    break;

                                ThrowIfError(receiveResult);

                                ffmpeg.sws_scale(
                                    swsContext,
                                    frame->data,
                                    frame->linesize,
                                    0,
                                    height,
                                    dstData,
                                    dstLinesize);

                                byte[] copy = new byte[bufferSize];
                                Buffer.BlockCopy(managedBuffer, 0, copy, 0, bufferSize);

                                TimeSpan timestamp = GetFrameTimestamp(frame);

                                Position = timestamp;
                                PositionChanged?.Invoke(this, Position);

                                FrameReady?.Invoke(this, new VideoFrame
                                {
                                    BgraBuffer = copy,
                                    Width = width,
                                    Height = height,
                                    Stride = stride,
                                    Timestamp = timestamp
                                });

                                Thread.Sleep(TimeSpan.FromMilliseconds(33 / _speed));
                            }
                        }
                        finally
                        {
                            ffmpeg.av_packet_unref(packet);
                        }
                    }
                }
            }
            finally
            {
                if (swsContext != null)
                    ffmpeg.sws_freeContext(swsContext);

                ffmpeg.av_frame_free(&bgraFrame);
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);

                lock (_stateLock)
                {
                    _isPlaying = false;
                }
            }
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

        private TimeSpan GetFrameTimestamp(AVFrame* frame)
        {
            long pts = frame->best_effort_timestamp;
            if (pts < 0 || _formatContext == null || _videoStreamIndex < 0)
                return Position;

            AVRational timeBase = _formatContext->streams[_videoStreamIndex]->time_base;
            double seconds = pts * ffmpeg.av_q2d(timeBase);
            return TimeSpan.FromSeconds(seconds);
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

            _isOpened = false;
            _reachedEnd = false;
        }

        public void Dispose()
        {
            CloseCurrent();
            _playbackCts?.Dispose();
        }
    }
}
