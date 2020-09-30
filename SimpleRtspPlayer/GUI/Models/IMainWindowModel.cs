using System;
using RtspClientSharp;

namespace SimpleRtspPlayer.GUI.Models
{
    interface IMainWindowModel
    {
        event EventHandler<string> StatusChanged;

        IVideoSource VideoSource { get; }
        IAudioSource AudioSource { get; }


        void Start(ConnectionParameters connectionParameters);
        void Stop();
    }
}