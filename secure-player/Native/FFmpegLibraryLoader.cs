using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace secure_player.Native
{
    public static class FFmpegLibraryLoader
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            string baseDirectory = AppContext.BaseDirectory;
            string ffmpegDirectory = Path.Combine(baseDirectory, "runtimes", "win-x64", "native");

            ffmpeg.RootPath = ffmpegDirectory;

            ffmpeg.avformat_network_init();

            _initialized = true;
        }
    }
}
