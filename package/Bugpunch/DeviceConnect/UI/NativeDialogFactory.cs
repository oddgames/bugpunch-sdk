namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public static class NativeDialogFactory
    {
        static INativeDialog _instance;

        public static INativeDialog Create()
        {
            if (_instance != null) return _instance;

#if UNITY_ANDROID && !UNITY_EDITOR
            CrashOverlayCallback.EnsureExists();
            ReportOverlayCallback.EnsureExists();
            _instance = new AndroidDialog();
#elif UNITY_IOS && !UNITY_EDITOR
            _instance = new IOSDialog();
#else
            // Editor + all standalone platforms (Windows, Mac, Linux)
            _instance = new UIToolkitDialog();
#endif
            return _instance;
        }
    }
}
