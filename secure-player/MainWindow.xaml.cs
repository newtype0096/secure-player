using secure_player.Playback;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace secure_player
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private FFmpegMemoryVideoPlayer? _player;
        private WriteableBitmap? _bitmap;

        private bool _isDraggingSlider;

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            byte[] bytes = await File.ReadAllBytesAsync("C:\\Users\\M\\Desktop\\hevc_4k25P_main_2.mp4");

            _player = new FFmpegMemoryVideoPlayer();
            _player.FrameReady += OnFrameReady;

            await _player.OpenAsync(bytes);

            PositionSlider.Maximum = _player.Duration.TotalSeconds;
            PositionSlider.Value = 0;

            _player.PositionChanged += OnPositionChanged;
        }

        private void OnPositionChanged(object? sender, TimeSpan position)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isDraggingSlider)
                    return;

                PositionSlider.Value = position.TotalSeconds;
            });
        }

        private void OnFrameReady(object? sender, VideoFrame frame)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_bitmap == null ||
                    _bitmap.PixelWidth != frame.Width ||
                    _bitmap.PixelHeight != frame.Height)
                {
                    _bitmap = new WriteableBitmap(
                        frame.Width,
                        frame.Height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null);

                    VideoImage.Source = _bitmap;
                }

                _bitmap.WritePixels(
                    new Int32Rect(0, 0, frame.Width, frame.Height),
                    frame.BgraBuffer,
                    frame.Stride,
                    0);
            });
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            _player?.Play();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _player?.Pause();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _player?.Stop();
        }

        private void Speed1xButton_Click(object sender, RoutedEventArgs e)
        {
            _player?.SetSpeed(1.0);
        }

        private void Speed2xButton_Click(object sender, RoutedEventArgs e)
        {
            _player?.SetSpeed(2.0);
        }

        private void Speed4xButton_Click(object sender, RoutedEventArgs e)
        {
            _player?.SetSpeed(4.0);
        }

        private void PositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private async void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;

            if (_player == null)
                return;

            await _player.SeekAsync(TimeSpan.FromSeconds(PositionSlider.Value));
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }
    }
}