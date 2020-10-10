namespace SimpleRtspPlayer.GUI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            AudioView1.DeviceAddress = "rtsp://192.168.199.86:8554/live/5";
            AudioView2.DeviceAddress = "rtsp://192.168.199.86:8554/live/4";
            //AudioView1.DeviceAddress = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
        }
        public void TextBox_TextChanged(object sender, System.EventArgs e)
        {

        }
         

    }
}