namespace ODDGames.Recorder
{
    /// <summary>
    /// Configuration for a recording session.
    /// </summary>
    public class RecorderSettings
    {
        /// <summary>
        /// Output video width in pixels. 0 = auto (uses screen width).
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Output video height in pixels. 0 = auto (uses screen height).
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Target frame rate for the recording.
        /// </summary>
        public int FrameRate { get; set; } = 15;

        /// <summary>
        /// Video bitrate in bits per second. Default is 10 Mbps.
        /// </summary>
        public int VideoBitrate { get; set; } = 2_000_000;

        /// <summary>
        /// Whether to include audio in the recording.
        /// </summary>
        public bool IncludeAudio { get; set; } = true;

        /// <summary>
        /// Output file path. Null = auto-generate in Recordings folder.
        /// </summary>
        public string OutputPath { get; set; }
    }
}
