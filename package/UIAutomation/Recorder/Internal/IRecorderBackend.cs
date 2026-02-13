using System;
using System.Threading.Tasks;

namespace ODDGames.Recorder.Internal
{
    internal interface IRecorderBackend : IDisposable
    {
        bool IsRecording { get; }
        void Start();
        Task<string> StopAsync();
    }
}
