using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SimpleRtspPlayer.RawFramesDecoding;
using SimpleRtspPlayer.RawFramesDecoding.DecodedFrames;
using PixelFormat = SimpleRtspPlayer.RawFramesDecoding.PixelFormat;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections;
using GalaSoft.MvvmLight.Command;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using RtspClientSharp;
using SimpleRtspPlayer.GUI.Models;
using SimpleRtspPlayer.RawFramesReceiving;

namespace SimpleRtspPlayer.GUI.Views
{
    /// <summary>
    /// Interaction logic for AudioView.xaml
    /// </summary>
    public partial class AudioView
    {
        private readonly Action<IDecodedAudioFrame> _invalidateAction;
        private ArrayList audioSeriesData  = new ArrayList();
        public PCMPlayer audioPlayer = new PCMPlayer();

        public static readonly DependencyProperty AudioSourceProperty = DependencyProperty.Register(nameof(AudioSource),
            typeof(IAudioSource),
            typeof(AudioView),
            new FrameworkPropertyMetadata(OnAudioSourceChanged));
        public IAudioSource AudioSource
        {
            get => (IAudioSource)GetValue(AudioSourceProperty);
            set => SetValue(AudioSourceProperty, value);
        }

        //private bool _startButtonEnabled = true;
        //private bool _stopButtonEnabled;
        //private bool _silentButtonEnabled;
        private const string RtspPrefix = "rtsp://";
        private const string HttpPrefix = "http://";
        private string _status = string.Empty;
        private readonly RealtimeVideoSource _realtimeVideoSource = new RealtimeVideoSource();
        private readonly RealtimeAudioSource _realtimeAudioSource = new RealtimeAudioSource();
        private IRawFramesSource _rawFramesSource;
        public event EventHandler<string> StatusChanged;
        public IVideoSource I_VideoSource => _realtimeVideoSource;
        public IAudioSource I_AudioSource => _realtimeAudioSource;
        public string DeviceAddress { get; set; } = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public RelayCommand AudioStartClickCommand { get; }
        public RelayCommand AudioStopClickCommand { get; }
        public RelayCommand AudioSilentClickCommand { get; }
        private void OnAudioStartClick(object sender, RoutedEventArgs e)
        {
            string address = DeviceAddress;

            if (!address.StartsWith(RtspPrefix) && !address.StartsWith(HttpPrefix))
                address = RtspPrefix + address;

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri deviceUri))
            {
                System.Windows.MessageBox.Show("Invalid device address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var credential = new NetworkCredential(Login, Password);
            var connectionParameters = new ConnectionParameters(deviceUri, credential);


            if (_rawFramesSource != null)
                return;

            _rawFramesSource = new RawFramesSource(connectionParameters);
            //_rawFramesSource.ConnectionStatusChanged += ConnectionStatusChanged;

            _realtimeVideoSource.SetRawFramesSource(_rawFramesSource);
            _realtimeAudioSource.SetRawFramesSource(_rawFramesSource);

            _rawFramesSource.Start();

            //_mainWindowModel.Start(connectionParameters);
            //_mainWindowModel.StatusChanged += MainWindowModelOnStatusChanged;
        }
        private void OnAudioStopClick(object sender, RoutedEventArgs e)
        {

        }
        private void OnAudioSilentClick(object sender, RoutedEventArgs e)
        {

        }

        public AudioView()
        {
            InitializeComponent();
            _invalidateAction = Invalidate;
            ChartAudio1.Titles.Add("音频1");
        }

        private static void OnAudioSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (AudioView)d;

            if (e.OldValue is IAudioSource oldAudioSource)
                oldAudioSource.FrameReceived -= view.OnFrameReceived;

            if (e.NewValue is IAudioSource newAudioSource)
                newAudioSource.FrameReceived += view.OnFrameReceived;
        }

        private void OnFrameReceived(object sender, IDecodedAudioFrame decodedFrame)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(_invalidateAction, DispatcherPriority.Send, decodedFrame);
        }

        private void Invalidate(IDecodedAudioFrame decodedAudioFrame)
        {
            ChartAudio1.Series.Clear();
            audioSeriesData.Clear();
            ChartAudio1.ChartAreas.Clear();
            Series seriesAudio = ChartAudio1.Series.Add("Audio");
            seriesAudio.ChartType = SeriesChartType.Spline;
            ChartAudio1.ChartAreas.Add("1");
            seriesAudio.ChartArea = "1";

            int len = decodedAudioFrame.DecodedBytes.Count;
            int singleLen = decodedAudioFrame.Format.BitPerSample / 8;
            if(decodedAudioFrame.Format.BitPerSample == 16)
            {
                for (int i = 0; i < len; i = i + singleLen)
                {
                    //only consider the bitPerSample == 16
                    int audioData = 0;
                    if (decodedAudioFrame.Format.Channels != 1)
                    {
                        audioData = (decodedAudioFrame.DecodedBytes.Array[i] * 255 + decodedAudioFrame.DecodedBytes.Array[i + 1] +
                            decodedAudioFrame.DecodedBytes.Array[i + 2] * 255 + decodedAudioFrame.DecodedBytes.Array[i + 3]) / 2;
                        i = i + singleLen;
                    }
                    else
                        audioData = decodedAudioFrame.DecodedBytes.Array[i] * 255 + decodedAudioFrame.DecodedBytes.Array[i + 1];
                    audioSeriesData.Add(audioData);
                }
            }
            else if(decodedAudioFrame.Format.BitPerSample == 8)
            {
                for (int i = 0; i < len; i = i + singleLen)
                {
                    //only consider the bitPerSample == 16
                    int audioData = 0;
                    if (decodedAudioFrame.Format.Channels != 1)
                    {
                        audioData = (decodedAudioFrame.DecodedBytes.Array[i] + decodedAudioFrame.DecodedBytes.Array[i + 1]) / 2;
                        i = i + singleLen;
                    }
                    else
                        audioData = decodedAudioFrame.DecodedBytes.Array[i];
                    audioSeriesData.Add(audioData);
                }
            }
            ChartAudio1.Series[0].Points.DataBindY(audioSeriesData);

            if(audioPlayer.WaveFormat == null)
                audioPlayer.SetPcmPlayer(decodedAudioFrame.Format.SampleRate, decodedAudioFrame.Format.BitPerSample, decodedAudioFrame.Format.Channels);

            audioPlayer.PlayData(decodedAudioFrame.DecodedBytes.Array);

            return;
        }

        public void Silent()
        {
            audioPlayer.SilentPlay();
        }

        public void CancelSilent()
        {
            audioPlayer.CancelSilentPlay();
        }

    }

    public class PCMPlayer
    {
        private MonoToStereoProvider16 monoToStereoProvider16;
        private BufferedWaveProvider bufferedWaveProvider;
        private WaveOut waveOut;
        private bool isRunning = false;
        
        public PCMPlayer()
        {
        }
        public PCMPlayer(int sampleRate, int bitsPerSample, int channels)
        {
            WaveFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
            bufferedWaveProvider = new BufferedWaveProvider(WaveFormat);
            waveOut = new WaveOut();
            if (channels == 1)
            {
                monoToStereoProvider16 = new MonoToStereoProvider16(bufferedWaveProvider);
                waveOut.Init(monoToStereoProvider16);
            }
            else if (channels == 2)
            {
                waveOut.Init(bufferedWaveProvider);
            }
            waveOut.Play();
            isRunning = true;
        }
        public void SetPcmPlayer(int sampleRate, int bitsPerSample, int channels)
        {
            WaveFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
            bufferedWaveProvider = new BufferedWaveProvider(WaveFormat);
            waveOut = new WaveOut();
            if (channels == 1)
            {
                monoToStereoProvider16 = new MonoToStereoProvider16(bufferedWaveProvider);
                waveOut.Init(monoToStereoProvider16);
            }
            else if(channels == 2)
            {
                waveOut.Init(bufferedWaveProvider);
            }
            waveOut.Play();
            isRunning = true;
        }

        public void PlayData(byte[] data)
        {
            if (!isRunning) return;
            bufferedWaveProvider.AddSamples(data, 0, data.Length);
        }

        public void SilentPlay()
        {
            isRunning = false;
        }

        public void CancelSilentPlay()
        {
            isRunning = true;
        }

        public void ClosePlay()
        {
            isRunning = false;
            waveOut.Stop();
            waveOut.Dispose();
        }
        public WaveFormat WaveFormat { get; private set; }
    }
}

