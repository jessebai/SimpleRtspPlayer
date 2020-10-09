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
using System.Security.Authentication;
using RtspClientSharp.RawFrames;
using RtspClientSharp.Rtsp;
using System.Collections.Generic;
using RtspClientSharp.RawFrames;
using RtspClientSharp.RawFrames.Audio;
using SimpleRtspPlayer.RawFramesDecoding;
using SimpleRtspPlayer.RawFramesDecoding.DecodedFrames;
using SimpleRtspPlayer.RawFramesDecoding.FFmpeg;
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
        private bool _audioSilentFlag = false;

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
        //public event EventHandler<string> StatusChanged;
        public IVideoSource I_VideoSource => _realtimeVideoSource;
        public IAudioSource I_AudioSource => _realtimeAudioSource;
        public string DeviceAddress { get; set; } = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        private Task _workTask = Task.CompletedTask;
        private CancellationTokenSource _cancellationTokenSource;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
        private ConnectionParameters _connectionParameters;
        private readonly Dictionary<FFmpegAudioCodecId, FFmpegAudioDecoder> _audioDecodersMap =
            new Dictionary<FFmpegAudioCodecId, FFmpegAudioDecoder>();
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
            _connectionParameters = connectionParameters;

            //if (_rawFramesSource != null)
            //    return;

            //_rawFramesSource = new RawFramesSource(connectionParameters);
            ////_rawFramesSource.ConnectionStatusChanged += ConnectionStatusChanged;

            //_realtimeVideoSource.SetRawFramesSource(_rawFramesSource);
            //_realtimeAudioSource.SetRawFramesSource(_rawFramesSource);
            //_rawFramesSource.Start();

            _cancellationTokenSource = new CancellationTokenSource();

            CancellationToken token = _cancellationTokenSource.Token;

            _workTask = _workTask.ContinueWith(async p =>
            {
                await ReceiveAsync(token);
            }, token);


            //_mainWindowModel.Start(connectionParameters);
            //_mainWindowModel.StatusChanged += MainWindowModelOnStatusChanged;
        }
        private void OnAudioStopClick(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            ChartAudio1.Series.Clear();
            audioSeriesData.Clear();
            ChartAudio1.ChartAreas.Clear();
            _audioSilentFlag = false;
        }
        private void OnAudioSilentClick(object sender, RoutedEventArgs e)
        {
            if (_audioSilentFlag)//now silent
            {
                CancelSilent();
                _audioSilentFlag = false;
            }
            else
            {
                Silent();
                _audioSilentFlag = true;
            }
        }

        private async Task ReceiveAsync(CancellationToken token)
        {
            try
            {
                using (var rtspClient = new RtspClient(_connectionParameters))
                {
                    rtspClient.FrameReceived += RtspClientOnFrameReceived;

                    while (true)
                    {
                        //OnStatusChanged("Connecting...");

                        try
                        {
                            await rtspClient.ConnectAsync(token);
                        }
                        catch (InvalidCredentialException)
                        {
                            //OnStatusChanged("Invalid login and/or password");
                            await Task.Delay(RetryDelay, token);
                            continue;
                        }
                        catch (RtspClientException e)
                        {
                            //OnStatusChanged(e.ToString());
                            await Task.Delay(RetryDelay, token);
                            continue;
                        }

                        //OnStatusChanged("Receiving frames...");

                        try
                        {
                            await rtspClient.ReceiveAsync(token);
                        }
                        catch (RtspClientException e)
                        {
                            //OnStatusChanged(e.ToString());
                            await Task.Delay(RetryDelay, token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RtspClientOnFrameReceived(object sender, RawFrame rawFrame)
        {
            if (!(rawFrame is RawAudioFrame rawAudioFrame))
                return;

            FFmpegAudioDecoder decoder = GetDecoderForFrame(rawAudioFrame);

            if (!decoder.TryDecode(rawAudioFrame))
                return;

            IDecodedAudioFrame decodedFrame = decoder.GetDecodedFrame(new AudioConversionParameters() { OutBitsPerSample = 16 });
            System.Windows.Application.Current.Dispatcher.Invoke(_invalidateAction, DispatcherPriority.Send, decodedFrame);
        }
        private FFmpegAudioDecoder GetDecoderForFrame(RawAudioFrame audioFrame)
        {
            FFmpegAudioCodecId codecId = DetectCodecId(audioFrame);

            if (!_audioDecodersMap.TryGetValue(codecId, out FFmpegAudioDecoder decoder))
            {
                int bitsPerCodedSample = 0;

                if (audioFrame is RawG726Frame g726Frame)
                    bitsPerCodedSample = g726Frame.BitsPerCodedSample;

                decoder = FFmpegAudioDecoder.CreateDecoder(codecId, bitsPerCodedSample);
                _audioDecodersMap.Add(codecId, decoder);
            }

            return decoder;
        }
        private FFmpegAudioCodecId DetectCodecId(RawAudioFrame audioFrame)
        {
            if (audioFrame is RawAACFrame)
                return FFmpegAudioCodecId.AAC;
            if (audioFrame is RawG711AFrame)
                return FFmpegAudioCodecId.G711A;
            if (audioFrame is RawG711UFrame)
                return FFmpegAudioCodecId.G711U;
            if (audioFrame is RawG726Frame)
                return FFmpegAudioCodecId.G726;

            throw new ArgumentOutOfRangeException(nameof(audioFrame));
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

