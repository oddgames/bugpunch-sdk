# Keep all WebRTC Java classes — they're called from native JNI (libwebrtc.so)
# and R8/ProGuard strips them because there are no Java-side references.
-keep class org.webrtc.** { *; }

# Keep Bugpunch Java classes
-keep class au.com.oddgames.bugpunch.** { *; }
