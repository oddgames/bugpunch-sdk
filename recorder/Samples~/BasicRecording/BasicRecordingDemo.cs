using System.Threading.Tasks;
using ODDGames.Recorder;
using UnityEngine;

public class BasicRecordingDemo : MonoBehaviour
{
    RecordingSession _session;

    async void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (_session == null || !_session.IsRecording)
                await StartRecording();
            else
                await StopRecording();
        }
    }

    async Task StartRecording()
    {
        _session = await MediaRecorder.StartAsync(new RecorderSettings
        {
            FrameRate = 30,
            IncludeAudio = true
        });
        Debug.Log("Recording started! Press R to stop.");
    }

    async Task StopRecording()
    {
        string path = await _session.StopAsync();
        _session = null;
        Debug.Log($"Recording saved: {path}");
    }
}
