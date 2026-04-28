# Shake-detector contract

Behavioral spec shared by [BugpunchShakeDetector.java](../android-src/bugpunch/src/main/java/au/com/oddgames/bugpunch/BugpunchShakeDetector.java) and [BugpunchShake.mm](../package/Plugins/iOS/BugpunchShake.mm). Same algorithm, two platforms — drift here would mean QA on iPhone gets a different shake feel from QA on Pixel.

## Algorithm

1. Subscribe to the platform accelerometer.
2. Compute magnitude in **m/s²**, gravity-removed:
   - **Android** — `sqrt(x² + y² + z²) − SensorManager.GRAVITY_EARTH` (sensor reports m/s² already).
   - **iOS** — `(sqrt(x² + y² + z²) − 1.0) × 9.81` (sensor reports Gs; subtract 1 g, then convert).
3. If magnitude exceeds the caller-supplied threshold, count a "spike."
4. Two spikes within the **spike window** = a shake. Fire the callback.
5. Suppress further fires for the **cooldown window** after a fire.
6. The spike counter resets if the gap between spikes exceeds the spike window.

## Pinned constants

| name           | value          | Java                          | Obj-C++                          |
|----------------|----------------|-------------------------------|----------------------------------|
| spike window   | **500 ms**     | `now - lastSpikeMs > 500`     | `now - lastSpike > 0.5`          |
| cooldown       | **2000 ms**    | `now - lastShakeMs > 2000`    | `now - lastShake > 2.0`          |
| spikes to fire | **2**          | `spikeCount >= 2`             | `spikes >= 2`                    |
| sample rate    | platform-best-effort | `SENSOR_DELAY_UI`       | `accelerometerUpdateInterval = 1.0/30.0` (≈ 33 ms) |

The threshold is *not* pinned here — `BugpunchDebugMode` passes it in, and tuning lives in config. Both platforms expect **m/s²**.

## Telemetry

When a shake fires, both platforms emit a `shake_fired` analytics event with `{"platform": "android" | "ios"}` so dashboard charts surface platform drift if one side stops firing. If you're tweaking thresholds and the event count diverges sharply between platforms after a release, that's the symptom.

## When changing this

Both files must be updated together. Update this doc to match. Do not add per-platform tuning constants without a paired reason — the goal is "feels the same on both sides." If a platform-specific quirk genuinely needs different math (e.g. iOS exposes user-acceleration without gravity removal in newer APIs), document it here, not just in the code.
