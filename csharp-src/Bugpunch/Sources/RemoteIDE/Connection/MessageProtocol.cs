namespace ODDGames.Bugpunch.RemoteIDE
{
    /// <summary>
    /// WebSocket message types for the Bugpunch tunnel protocol
    /// </summary>
    public static class MessageProtocol
    {
        public const string TYPE_REGISTER = "register";
        public const string TYPE_REGISTERED = "registered";
        public const string TYPE_REQUEST = "request";
        public const string TYPE_RESPONSE = "response";
        public const string TYPE_HEARTBEAT = "heartbeat";
        public const string TYPE_STREAM = "stream";
    }
}
