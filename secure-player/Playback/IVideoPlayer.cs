using System;
using System.Collections.Generic;
using System.Text;

namespace secure_player.Playback
{
    public interface IVideoPlayer : IDisposable
    {
        event EventHandler<VideoFrame>? FrameReady;
        event EventHandler<TimeSpan>? PositionChanged;

        TimeSpan Duration { get; }
        TimeSpan Position { get; }
        bool IsPlaying { get; }

        Task OpenAsync(byte[] mediaBytes, CancellationToken cancellationToken = default);
        void Play();
        void Pause();
        void Stop();
        Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
        void SetSpeed(double speed);
    }
}
