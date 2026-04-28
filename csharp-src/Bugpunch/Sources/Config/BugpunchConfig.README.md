# BugpunchConfig

Configuration ScriptableObject for the Bugpunch device connection SDK.

## Setup

1. In Unity: **Assets > Create > ODD Games > Bugpunch Config**
2. Place the created asset in a `Resources/` folder (e.g., `Assets/Resources/BugpunchConfig.asset`)
3. Fill in the fields below

## Fields

### Server Connection

| Field | Description |
|-------|-------------|
| **Server URL** | Your Bugpunch server URL. Use `ws://` for local dev, `wss://` for production. Example: `wss://yourserver.b4a.run` |
| **API Key** | Project API key from the dashboard (Project Settings > API Keys). The SDK authenticates with this on connect. |
| **Project ID** | Auto-filled when the API key is validated. You can leave this empty. |

### Device Identity

| Field | Description |
|-------|-------------|
| **Device Name** | Optional custom name for this device. Defaults to `SystemInfo.deviceName` if empty. |

### Feature Flags

| Flag | Default | Description |
|------|---------|-------------|
| **Enable Hierarchy** | true | Remote GameObject hierarchy inspection from the dashboard |
| **Enable Console** | true | Stream Unity console logs to the dashboard in real time |
| **Enable Screen Capture** | true | Allow remote screenshots and live screen streaming |
| **Enable Script Runner** | true | Allow executing C# scripts remotely on the device |
| **Enable Inspector** | true | Allow remote component property inspection and editing |

### Screen Capture Settings

| Field | Default | Description |
|-------|---------|-------------|
| **Capture Quality** | 75 | JPEG quality (1-100) |
| **Capture Scale** | 0.5 | Resolution scale factor (0.1-1.0). Lower = faster streaming |
| **Stream FPS** | 10 | Max frames per second for live streaming |

### Bug Reporting

| Field | Default | Description |
|-------|---------|-------------|
| **Enable Shake To Report** | true | Shake gesture triggers bug report UI on mobile |
| **Video Buffer Seconds** | 30 | Seconds of video buffered for bug report replays |
| **Bug Report Video FPS** | 10 | Capture FPS for the video buffer |

### Connection

| Field | Default | Description |
|-------|---------|-------------|
| **Auto Connect** | true | Connect to the server automatically when entering play mode |
| **Reconnect Delay** | 5s | Seconds to wait before reconnecting after a disconnect |
| **Heartbeat Interval** | 10s | Seconds between keepalive pings |

## How to Get an API Key

1. Open your Bugpunch dashboard
2. Go to a project's settings page
3. Click **Create API Key**
4. Copy the key into the **API Key** field in your BugpunchConfig asset
