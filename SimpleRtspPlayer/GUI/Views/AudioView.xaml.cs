using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SimpleRtspPlayer.RawFramesDecoding;
using SimpleRtspPlayer.RawFramesDecoding.DecodedFrames;
using System.Windows.Forms.DataVisualization.Charting;
using NAudio.Wave;
using System.Collections;
using System.Net;
using RtspClientSharp;
using System.Security.Authentication;
using RtspClientSharp.RawFrames;
using RtspClientSharp.Rtsp;
using System.Collections.Generic;
using RtspClientSharp.RawFrames.Audio;
using SimpleRtspPlayer.RawFramesDecoding.FFmpeg;

namespace SimpleRtspPlayer.GUI.Views
{
    public partial class AudioView
    {
        private readonly Action<IDecodedAudioFrame> _invalidateAction;
        private ArrayList audioSeriesData  = new ArrayList();
        System.Data.DataTable chartDataTable = new System.Data.DataTable("MyTable");
        public Series seriesAudio;
        public PCMPlayer audioPlayer = new PCMPlayer();
        private bool _audioSilentFlag = false;
        private const string RtspPrefix = "rtsp://";
        private const string HttpPrefix = "http://";
        private string _status = string.Empty;
        //public string DeviceAddress { get; set; } = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
        public string DeviceAddress { get; set; } = "rtsp://192.168.199.86:8554/live/4";
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
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            _workTask = _workTask.ContinueWith(async p =>
            {
                await ReceiveAsync(token);
            }, token);
        }
        private void OnAudioStopClick(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            ChartAudio.Series.Clear();
            audioSeriesData.Clear();
            ChartAudio.ChartAreas.Clear();
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
            //ChartAudio.Titles.Add("音频1");
            seriesAudio = ChartAudio.Series.Add("Audio");
            seriesAudio.ChartType = SeriesChartType.Spline;
            ChartAudio.ChartAreas.Add("1");
            seriesAudio.ChartArea = "1";
            ChartAudio.ChartAreas[0].AxisX.Enabled = AxisEnabled.False;
            ChartAudio.ChartAreas[0].AxisY.Enabled = AxisEnabled.False;
            ChartAudio.ChartAreas[0].AxisY.Minimum = 0;
            ChartAudio.ChartAreas[0].AxisY.Maximum = 65535;
            chartDataTable.Columns.Add("soundData", typeof(int));
            //ChartAudio.Series[0].Points.DataBindY(audioSeriesData);
        }

        public long frames = 0;
        public readonly int partOfSingleFrame = 20;
        public readonly int partOfTotalFrames = 3;
        //convert decodedAudioFrame to wave and sound
        private void Invalidate(IDecodedAudioFrame decodedAudioFrame)
        {
            frames++;
            audioSeriesData.Clear();
            chartDataTable.Rows.Clear();
            int len = decodedAudioFrame.DecodedBytes.Count;
            int singleLen = decodedAudioFrame.Format.BitPerSample / 8;
            if (decodedAudioFrame.Format.BitPerSample == 16)
            {
                for (int i = 0; i < len / partOfSingleFrame; i = i + singleLen)
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
                    chartDataTable.Rows.Add(audioData);
                }
            }
            else if (decodedAudioFrame.Format.BitPerSample == 8)
            {
                for (int i = 0; i < len / partOfSingleFrame; i = i + singleLen)
                {
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

            if (frames % partOfTotalFrames == 0)
            {
                DrawChart();
            }

            if (audioPlayer.WaveFormat == null)
            {
                audioPlayer.SetPcmPlayer(decodedAudioFrame.Format.SampleRate, decodedAudioFrame.Format.BitPerSample, decodedAudioFrame.Format.Channels);
            }

            audioPlayer.PlayData(decodedAudioFrame.DecodedBytes.Array);
            
            return;
        }

        public void DrawChart()
        {
            //ChartAudio.Series.Clear();
            //ChartAudio.ChartAreas.Clear();
            ChartAudio.Series[0].Points.DataBindY(audioSeriesData);
            //ChartAudio.Series[0].Points.DataBindY
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

