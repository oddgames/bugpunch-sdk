using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Read/write Unity runtime settings (Time, Physics, Quality, Render, Audio,
    /// Application, layers, etc.). One service, one poll route — the dashboard
    /// hits /settings every ~1.5s to stay in sync with whatever the game code
    /// or other tools are doing to these globals.
    ///
    /// All Unity API access must run on the main thread. The router is invoked
    /// from a coroutine on the main thread, so this is fine.
    /// </summary>
    public class SettingsService
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── Top-level: all groups in one payload ────────────────────────────

        public string GetAll()
        {
            var sb = new StringBuilder(2048);
            sb.Append("{");
            sb.Append("\"time\":");        sb.Append(GetTime());
            sb.Append(",\"physics\":");    sb.Append(GetPhysics());
            sb.Append(",\"physics2d\":");  sb.Append(GetPhysics2D());
            sb.Append(",\"quality\":");    sb.Append(GetQuality());
            sb.Append(",\"render\":");     sb.Append(GetRender());
            sb.Append(",\"audio\":");      sb.Append(GetAudio());
            sb.Append(",\"application\":");sb.Append(GetApplication());
            sb.Append(",\"shader\":");     sb.Append(GetShader());
            sb.Append(",\"layers\":");     sb.Append(GetLayers());
            sb.Append("}");
            return sb.ToString();
        }

        public string GetGroup(string group)
        {
            switch (group)
            {
                case "time":        return GetTime();
                case "physics":     return GetPhysics();
                case "physics2d":   return GetPhysics2D();
                case "quality":     return GetQuality();
                case "render":      return GetRender();
                case "audio":       return GetAudio();
                case "application": return GetApplication();
                case "shader":      return GetShader();
                case "layers":      return GetLayers();
                default:            return "{\"error\":\"unknown group\"}";
            }
        }

        // ── Time ────────────────────────────────────────────────────────────

        public string GetTime()
        {
            var sb = new StringBuilder(256);
            sb.Append("{");
            F(sb, "timeScale", Time.timeScale); sb.Append(",");
            F(sb, "fixedDeltaTime", Time.fixedDeltaTime); sb.Append(",");
            F(sb, "maximumDeltaTime", Time.maximumDeltaTime); sb.Append(",");
            F(sb, "maximumParticleDeltaTime", Time.maximumParticleDeltaTime); sb.Append(",");
            sb.Append("\"captureFramerate\":").Append(Time.captureFramerate);
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyTime(string body)
        {
            var changed = 0;
            if (TryNum(body, "timeScale", out var ts))            { Time.timeScale = Mathf.Max(0f, ts); changed++; }
            if (TryNum(body, "fixedDeltaTime", out var fdt))      { Time.fixedDeltaTime = Mathf.Max(0.0001f, fdt); changed++; }
            if (TryNum(body, "maximumDeltaTime", out var mdt))    { Time.maximumDeltaTime = Mathf.Max(0.001f, mdt); changed++; }
            if (TryNum(body, "maximumParticleDeltaTime", out var mpdt)) { Time.maximumParticleDeltaTime = Mathf.Max(0.001f, mpdt); changed++; }
            if (TryInt(body, "captureFramerate", out var cf))     { Time.captureFramerate = Mathf.Max(0, cf); changed++; }
            return Ok(changed);
        }

        // ── Physics 3D ──────────────────────────────────────────────────────

        public string GetPhysics()
        {
            var sb = new StringBuilder(512);
            sb.Append("{");
            sb.Append("\"gravity\":"); V3(sb, Physics.gravity); sb.Append(",");
            F(sb, "defaultContactOffset", Physics.defaultContactOffset); sb.Append(",");
            F(sb, "sleepThreshold", Physics.sleepThreshold); sb.Append(",");
            F(sb, "bounceThreshold", Physics.bounceThreshold); sb.Append(",");
            sb.Append("\"defaultSolverIterations\":").Append(Physics.defaultSolverIterations).Append(",");
            sb.Append("\"defaultSolverVelocityIterations\":").Append(Physics.defaultSolverVelocityIterations).Append(",");
            sb.Append("\"queriesHitTriggers\":").Append(Physics.queriesHitTriggers ? "true" : "false").Append(",");
            sb.Append("\"queriesHitBackfaces\":").Append(Physics.queriesHitBackfaces ? "true" : "false").Append(",");
#if UNITY_2022_2_OR_NEWER
            sb.Append("\"simulationMode\":\"").Append(Esc(Physics.simulationMode.ToString())).Append("\",");
#else
            sb.Append("\"autoSimulation\":").Append(Physics.autoSimulation ? "true" : "false").Append(",");
#endif
            sb.Append("\"autoSyncTransforms\":").Append(Physics.autoSyncTransforms ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyPhysics(string body)
        {
            int changed = 0;
            if (TryVec3(body, "gravity", out var g))                  { Physics.gravity = g; changed++; }
            if (TryNum(body, "defaultContactOffset", out var dco))    { Physics.defaultContactOffset = Mathf.Max(0.0001f, dco); changed++; }
            if (TryNum(body, "sleepThreshold", out var st))           { Physics.sleepThreshold = Mathf.Max(0f, st); changed++; }
            if (TryNum(body, "bounceThreshold", out var bt))          { Physics.bounceThreshold = Mathf.Max(0f, bt); changed++; }
            if (TryInt(body, "defaultSolverIterations", out var dsi)) { Physics.defaultSolverIterations = Mathf.Max(1, dsi); changed++; }
            if (TryInt(body, "defaultSolverVelocityIterations", out var dsvi)) { Physics.defaultSolverVelocityIterations = Mathf.Max(1, dsvi); changed++; }
            if (TryBool(body, "queriesHitTriggers", out var qht))     { Physics.queriesHitTriggers = qht; changed++; }
            if (TryBool(body, "queriesHitBackfaces", out var qhb))    { Physics.queriesHitBackfaces = qhb; changed++; }
#if UNITY_2022_2_OR_NEWER
            var sm = JsonStr(body, "simulationMode");
            if (sm != null && Enum.TryParse<SimulationMode>(sm, out var smVal)) { Physics.simulationMode = smVal; changed++; }
#else
            if (TryBool(body, "autoSimulation", out var asim))        { Physics.autoSimulation = asim; changed++; }
#endif
            if (TryBool(body, "autoSyncTransforms", out var ast))     { Physics.autoSyncTransforms = ast; changed++; }
            return Ok(changed);
        }

        // ── Physics 2D ──────────────────────────────────────────────────────

        public string GetPhysics2D()
        {
            var sb = new StringBuilder(512);
            sb.Append("{");
            sb.Append("\"gravity\":"); V2(sb, Physics2D.gravity); sb.Append(",");
            F(sb, "defaultContactOffset", Physics2D.defaultContactOffset); sb.Append(",");
            sb.Append("\"velocityIterations\":").Append(Physics2D.velocityIterations).Append(",");
            sb.Append("\"positionIterations\":").Append(Physics2D.positionIterations).Append(",");
            F(sb, "bounceThreshold", Physics2D.bounceThreshold); sb.Append(",");
            F(sb, "maxLinearCorrection", Physics2D.maxLinearCorrection); sb.Append(",");
            F(sb, "maxAngularCorrection", Physics2D.maxAngularCorrection); sb.Append(",");
            F(sb, "maxTranslationSpeed", Physics2D.maxTranslationSpeed); sb.Append(",");
            F(sb, "maxRotationSpeed", Physics2D.maxRotationSpeed); sb.Append(",");
            F(sb, "baumgarteScale", Physics2D.baumgarteScale); sb.Append(",");
            F(sb, "baumgarteTOIScale", Physics2D.baumgarteTOIScale); sb.Append(",");
            F(sb, "timeToSleep", Physics2D.timeToSleep); sb.Append(",");
            F(sb, "linearSleepTolerance", Physics2D.linearSleepTolerance); sb.Append(",");
            F(sb, "angularSleepTolerance", Physics2D.angularSleepTolerance); sb.Append(",");
            sb.Append("\"queriesHitTriggers\":").Append(Physics2D.queriesHitTriggers ? "true" : "false").Append(",");
            sb.Append("\"queriesStartInColliders\":").Append(Physics2D.queriesStartInColliders ? "true" : "false").Append(",");
            sb.Append("\"callbacksOnDisable\":").Append(Physics2D.callbacksOnDisable ? "true" : "false").Append(",");
#if UNITY_2022_2_OR_NEWER
            sb.Append("\"simulationMode\":\"").Append(Esc(Physics2D.simulationMode.ToString())).Append("\"");
#else
            sb.Append("\"autoSimulation\":").Append(Physics2D.autoSimulation ? "true" : "false");
#endif
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyPhysics2D(string body)
        {
            int changed = 0;
            if (TryVec2(body, "gravity", out var g))                  { Physics2D.gravity = g; changed++; }
            if (TryNum(body, "defaultContactOffset", out var dco))    { Physics2D.defaultContactOffset = Mathf.Max(0.0001f, dco); changed++; }
            if (TryInt(body, "velocityIterations", out var vi))       { Physics2D.velocityIterations = Mathf.Max(1, vi); changed++; }
            if (TryInt(body, "positionIterations", out var pi))       { Physics2D.positionIterations = Mathf.Max(1, pi); changed++; }
            if (TryNum(body, "bounceThreshold", out var vt))          { Physics2D.bounceThreshold = Mathf.Max(0f, vt); changed++; }
            if (TryNum(body, "maxLinearCorrection", out var mlc))     { Physics2D.maxLinearCorrection = Mathf.Max(0f, mlc); changed++; }
            if (TryNum(body, "maxAngularCorrection", out var mac))    { Physics2D.maxAngularCorrection = Mathf.Max(0f, mac); changed++; }
            if (TryNum(body, "maxTranslationSpeed", out var mts))     { Physics2D.maxTranslationSpeed = Mathf.Max(0f, mts); changed++; }
            if (TryNum(body, "maxRotationSpeed", out var mrs))        { Physics2D.maxRotationSpeed = Mathf.Max(0f, mrs); changed++; }
            if (TryNum(body, "baumgarteScale", out var bs))           { Physics2D.baumgarteScale = bs; changed++; }
            if (TryNum(body, "baumgarteTOIScale", out var bts))       { Physics2D.baumgarteTOIScale = bts; changed++; }
            if (TryNum(body, "timeToSleep", out var tts))             { Physics2D.timeToSleep = Mathf.Max(0f, tts); changed++; }
            if (TryNum(body, "linearSleepTolerance", out var lst))    { Physics2D.linearSleepTolerance = Mathf.Max(0f, lst); changed++; }
            if (TryNum(body, "angularSleepTolerance", out var ast))   { Physics2D.angularSleepTolerance = Mathf.Max(0f, ast); changed++; }
            if (TryBool(body, "queriesHitTriggers", out var qht))     { Physics2D.queriesHitTriggers = qht; changed++; }
            if (TryBool(body, "queriesStartInColliders", out var qsc)){ Physics2D.queriesStartInColliders = qsc; changed++; }
            if (TryBool(body, "callbacksOnDisable", out var cod))     { Physics2D.callbacksOnDisable = cod; changed++; }
#if UNITY_2022_2_OR_NEWER
            var sm = JsonStr(body, "simulationMode");
            if (sm != null && Enum.TryParse<SimulationMode2D>(sm, out var smVal)) { Physics2D.simulationMode = smVal; changed++; }
#else
            if (TryBool(body, "autoSimulation", out var asim))        { Physics2D.autoSimulation = asim; changed++; }
#endif
            return Ok(changed);
        }

        // ── Quality ─────────────────────────────────────────────────────────

        public string GetQuality()
        {
            var sb = new StringBuilder(512);
            sb.Append("{");
            sb.Append("\"currentLevel\":").Append(QualitySettings.GetQualityLevel()).Append(",");
            sb.Append("\"levels\":[");
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(Esc(names[i])).Append("\"");
            }
            sb.Append("],");
            sb.Append("\"vSyncCount\":").Append(QualitySettings.vSyncCount).Append(",");
            sb.Append("\"antiAliasing\":").Append(QualitySettings.antiAliasing).Append(",");
            sb.Append("\"shadows\":\"").Append(QualitySettings.shadows).Append("\",");
            sb.Append("\"shadowResolution\":\"").Append(QualitySettings.shadowResolution).Append("\",");
            sb.Append("\"shadowProjection\":\"").Append(QualitySettings.shadowProjection).Append("\",");
            sb.Append("\"shadowmaskMode\":\"").Append(QualitySettings.shadowmaskMode).Append("\",");
            F(sb, "shadowDistance", QualitySettings.shadowDistance); sb.Append(",");
            F(sb, "shadowNearPlaneOffset", QualitySettings.shadowNearPlaneOffset); sb.Append(",");
            sb.Append("\"shadowCascades\":").Append(QualitySettings.shadowCascades).Append(",");
            sb.Append("\"pixelLightCount\":").Append(QualitySettings.pixelLightCount).Append(",");
            F(sb, "lodBias", QualitySettings.lodBias); sb.Append(",");
            sb.Append("\"maximumLODLevel\":").Append(QualitySettings.maximumLODLevel).Append(",");
            sb.Append("\"anisotropicFiltering\":\"").Append(QualitySettings.anisotropicFiltering).Append("\",");
            sb.Append("\"softParticles\":").Append(QualitySettings.softParticles ? "true" : "false").Append(",");
            sb.Append("\"realtimeReflectionProbes\":").Append(QualitySettings.realtimeReflectionProbes ? "true" : "false").Append(",");
            sb.Append("\"billboardsFaceCameraPosition\":").Append(QualitySettings.billboardsFaceCameraPosition ? "true" : "false").Append(",");
            F(sb, "resolutionScalingFixedDPIFactor", QualitySettings.resolutionScalingFixedDPIFactor); sb.Append(",");
            sb.Append("\"skinWeights\":\"").Append(QualitySettings.skinWeights).Append("\",");
            sb.Append("\"asyncUploadTimeSlice\":").Append(QualitySettings.asyncUploadTimeSlice).Append(",");
            sb.Append("\"asyncUploadBufferSize\":").Append(QualitySettings.asyncUploadBufferSize);
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyQuality(string body)
        {
            int changed = 0;
            if (TryInt(body, "currentLevel", out var lvl))            { QualitySettings.SetQualityLevel(Mathf.Clamp(lvl, 0, QualitySettings.names.Length - 1), true); changed++; }
            if (TryInt(body, "vSyncCount", out var vs))               { QualitySettings.vSyncCount = Mathf.Clamp(vs, 0, 4); changed++; }
            if (TryInt(body, "antiAliasing", out var aa))             { QualitySettings.antiAliasing = aa; changed++; }
            ApplyEnum<ShadowQuality>(body, "shadows", v => QualitySettings.shadows = v, ref changed);
            ApplyEnum<ShadowResolution>(body, "shadowResolution", v => QualitySettings.shadowResolution = v, ref changed);
            ApplyEnum<ShadowProjection>(body, "shadowProjection", v => QualitySettings.shadowProjection = v, ref changed);
            ApplyEnum<ShadowmaskMode>(body, "shadowmaskMode", v => QualitySettings.shadowmaskMode = v, ref changed);
            if (TryNum(body, "shadowDistance", out var sd))           { QualitySettings.shadowDistance = Mathf.Max(0f, sd); changed++; }
            if (TryNum(body, "shadowNearPlaneOffset", out var snpo))  { QualitySettings.shadowNearPlaneOffset = snpo; changed++; }
            if (TryInt(body, "shadowCascades", out var sc))           { QualitySettings.shadowCascades = sc; changed++; }
            if (TryInt(body, "pixelLightCount", out var plc))         { QualitySettings.pixelLightCount = Mathf.Max(0, plc); changed++; }
            if (TryNum(body, "lodBias", out var lb))                  { QualitySettings.lodBias = Mathf.Max(0f, lb); changed++; }
            if (TryInt(body, "maximumLODLevel", out var mll))         { QualitySettings.maximumLODLevel = Mathf.Max(0, mll); changed++; }
            ApplyEnum<AnisotropicFiltering>(body, "anisotropicFiltering", v => QualitySettings.anisotropicFiltering = v, ref changed);
            if (TryBool(body, "softParticles", out var sp))           { QualitySettings.softParticles = sp; changed++; }
            if (TryBool(body, "realtimeReflectionProbes", out var rrp)){ QualitySettings.realtimeReflectionProbes = rrp; changed++; }
            if (TryBool(body, "billboardsFaceCameraPosition", out var bfcp)){ QualitySettings.billboardsFaceCameraPosition = bfcp; changed++; }
            if (TryNum(body, "resolutionScalingFixedDPIFactor", out var rsf)){ QualitySettings.resolutionScalingFixedDPIFactor = Mathf.Max(0.01f, rsf); changed++; }
            ApplyEnum<SkinWeights>(body, "skinWeights", v => QualitySettings.skinWeights = v, ref changed);
            if (TryInt(body, "asyncUploadTimeSlice", out var auts))   { QualitySettings.asyncUploadTimeSlice = Mathf.Max(1, auts); changed++; }
            if (TryInt(body, "asyncUploadBufferSize", out var aubs))  { QualitySettings.asyncUploadBufferSize = Mathf.Max(2, aubs); changed++; }
            return Ok(changed);
        }

        // ── Render Settings ─────────────────────────────────────────────────

        public string GetRender()
        {
            var sb = new StringBuilder(512);
            sb.Append("{");
            sb.Append("\"fog\":").Append(RenderSettings.fog ? "true" : "false").Append(",");
            sb.Append("\"fogMode\":\"").Append(RenderSettings.fogMode).Append("\",");
            sb.Append("\"fogColor\":"); Col(sb, RenderSettings.fogColor); sb.Append(",");
            F(sb, "fogDensity", RenderSettings.fogDensity); sb.Append(",");
            F(sb, "fogStartDistance", RenderSettings.fogStartDistance); sb.Append(",");
            F(sb, "fogEndDistance", RenderSettings.fogEndDistance); sb.Append(",");
            sb.Append("\"ambientMode\":\"").Append(RenderSettings.ambientMode).Append("\",");
            sb.Append("\"ambientLight\":"); Col(sb, RenderSettings.ambientLight); sb.Append(",");
            sb.Append("\"ambientSkyColor\":"); Col(sb, RenderSettings.ambientSkyColor); sb.Append(",");
            sb.Append("\"ambientEquatorColor\":"); Col(sb, RenderSettings.ambientEquatorColor); sb.Append(",");
            sb.Append("\"ambientGroundColor\":"); Col(sb, RenderSettings.ambientGroundColor); sb.Append(",");
            F(sb, "ambientIntensity", RenderSettings.ambientIntensity); sb.Append(",");
            sb.Append("\"subtractiveShadowColor\":"); Col(sb, RenderSettings.subtractiveShadowColor); sb.Append(",");
            F(sb, "reflectionIntensity", RenderSettings.reflectionIntensity); sb.Append(",");
            sb.Append("\"reflectionBounces\":").Append(RenderSettings.reflectionBounces).Append(",");
            sb.Append("\"defaultReflectionMode\":\"").Append(RenderSettings.defaultReflectionMode).Append("\",");
            sb.Append("\"defaultReflectionResolution\":").Append(RenderSettings.defaultReflectionResolution).Append(",");
            F(sb, "haloStrength", RenderSettings.haloStrength); sb.Append(",");
            F(sb, "flareStrength", RenderSettings.flareStrength); sb.Append(",");
            F(sb, "flareFadeSpeed", RenderSettings.flareFadeSpeed); sb.Append(",");
            sb.Append("\"skybox\":\"").Append(Esc(RenderSettings.skybox != null ? RenderSettings.skybox.name : "")).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyRender(string body)
        {
            int changed = 0;
            if (TryBool(body, "fog", out var fog))                    { RenderSettings.fog = fog; changed++; }
            ApplyEnum<FogMode>(body, "fogMode", v => RenderSettings.fogMode = v, ref changed);
            if (TryColor(body, "fogColor", out var fc))               { RenderSettings.fogColor = fc; changed++; }
            if (TryNum(body, "fogDensity", out var fd))               { RenderSettings.fogDensity = Mathf.Max(0f, fd); changed++; }
            if (TryNum(body, "fogStartDistance", out var fsd))        { RenderSettings.fogStartDistance = fsd; changed++; }
            if (TryNum(body, "fogEndDistance", out var fed))          { RenderSettings.fogEndDistance = fed; changed++; }
            ApplyEnum<UnityEngine.Rendering.AmbientMode>(body, "ambientMode", v => RenderSettings.ambientMode = v, ref changed);
            if (TryColor(body, "ambientLight", out var al))           { RenderSettings.ambientLight = al; changed++; }
            if (TryColor(body, "ambientSkyColor", out var ask))       { RenderSettings.ambientSkyColor = ask; changed++; }
            if (TryColor(body, "ambientEquatorColor", out var aec))   { RenderSettings.ambientEquatorColor = aec; changed++; }
            if (TryColor(body, "ambientGroundColor", out var agc))    { RenderSettings.ambientGroundColor = agc; changed++; }
            if (TryNum(body, "ambientIntensity", out var ai))         { RenderSettings.ambientIntensity = Mathf.Max(0f, ai); changed++; }
            if (TryColor(body, "subtractiveShadowColor", out var ssc)){ RenderSettings.subtractiveShadowColor = ssc; changed++; }
            if (TryNum(body, "reflectionIntensity", out var ri))      { RenderSettings.reflectionIntensity = Mathf.Clamp01(ri); changed++; }
            if (TryInt(body, "reflectionBounces", out var rb))        { RenderSettings.reflectionBounces = Mathf.Max(1, rb); changed++; }
            ApplyEnum<UnityEngine.Rendering.DefaultReflectionMode>(body, "defaultReflectionMode", v => RenderSettings.defaultReflectionMode = v, ref changed);
            if (TryInt(body, "defaultReflectionResolution", out var drr)){ RenderSettings.defaultReflectionResolution = Mathf.Max(16, drr); changed++; }
            if (TryNum(body, "haloStrength", out var hs))             { RenderSettings.haloStrength = Mathf.Clamp01(hs); changed++; }
            if (TryNum(body, "flareStrength", out var fls))           { RenderSettings.flareStrength = Mathf.Clamp01(fls); changed++; }
            if (TryNum(body, "flareFadeSpeed", out var ffs))          { RenderSettings.flareFadeSpeed = Mathf.Max(0f, ffs); changed++; }
            return Ok(changed);
        }

        // ── Audio ───────────────────────────────────────────────────────────

        public string GetAudio()
        {
            var sb = new StringBuilder(128);
            sb.Append("{");
            F(sb, "volume", AudioListener.volume); sb.Append(",");
            sb.Append("\"pause\":").Append(AudioListener.pause ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyAudio(string body)
        {
            int changed = 0;
            if (TryNum(body, "volume", out var v))   { AudioListener.volume = Mathf.Clamp01(v); changed++; }
            if (TryBool(body, "pause", out var p))   { AudioListener.pause = p; changed++; }
            return Ok(changed);
        }

        // ── Application / Screen ────────────────────────────────────────────

        public string GetApplication()
        {
            var sb = new StringBuilder(256);
            sb.Append("{");
            sb.Append("\"targetFrameRate\":").Append(Application.targetFrameRate).Append(",");
            sb.Append("\"runInBackground\":").Append(Application.runInBackground ? "true" : "false").Append(",");
            sb.Append("\"backgroundLoadingPriority\":\"").Append(Application.backgroundLoadingPriority).Append("\",");
            sb.Append("\"sleepTimeout\":").Append(Screen.sleepTimeout).Append(",");
            sb.Append("\"fullScreen\":").Append(Screen.fullScreen ? "true" : "false").Append(",");
            sb.Append("\"fullScreenMode\":\"").Append(Screen.fullScreenMode).Append("\",");
            F(sb, "brightness", Screen.brightness); sb.Append(",");
            sb.Append("\"orientation\":\"").Append(Screen.orientation).Append("\",");
            sb.Append("\"width\":").Append(Screen.width).Append(",");
            sb.Append("\"height\":").Append(Screen.height).Append(",");
            sb.Append("\"currentResolution\":\"").Append(Screen.currentResolution.width).Append("x").Append(Screen.currentResolution.height).Append("@").Append(Screen.currentResolution.refreshRateRatio.value.ToString("F0", Inv)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyApplication(string body)
        {
            int changed = 0;
            if (TryInt(body, "targetFrameRate", out var tfr))      { Application.targetFrameRate = tfr; changed++; }
            if (TryBool(body, "runInBackground", out var rib))     { Application.runInBackground = rib; changed++; }
            ApplyEnum<ThreadPriority>(body, "backgroundLoadingPriority", v => Application.backgroundLoadingPriority = v, ref changed);
            if (TryInt(body, "sleepTimeout", out var sto))         { Screen.sleepTimeout = sto; changed++; }
            if (TryNum(body, "brightness", out var br))            { Screen.brightness = Mathf.Clamp01(br); changed++; }
            ApplyEnum<ScreenOrientation>(body, "orientation", v => Screen.orientation = v, ref changed);
            ApplyEnum<FullScreenMode>(body, "fullScreenMode", v => Screen.fullScreenMode = v, ref changed);
            return Ok(changed);
        }

        // ── Shader globals ──────────────────────────────────────────────────

        public string GetShader()
        {
            var sb = new StringBuilder(256);
            sb.Append("{");
            sb.Append("\"globalMaximumLOD\":").Append(Shader.globalMaximumLOD).Append(",");
            sb.Append("\"globalRenderPipeline\":\"").Append(Esc(Shader.globalRenderPipeline ?? "")).Append("\",");
            sb.Append("\"wireframe\":").Append(GL.wireframe ? "true" : "false").Append(",");
            sb.Append("\"srpBatching\":").Append(UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching ? "true" : "false").Append(",");
            sb.Append("\"lightsLinearIntensity\":").Append(UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity ? "true" : "false").Append(",");
            sb.Append("\"lightsColorTemperature\":").Append(UnityEngine.Rendering.GraphicsSettings.lightsUseColorTemperature ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        public string ApplyShader(string body)
        {
            int changed = 0;
            if (TryInt(body, "globalMaximumLOD", out var gml)) { Shader.globalMaximumLOD = Mathf.Max(0, gml); changed++; }
            var grp = JsonStr(body, "globalRenderPipeline");
            if (grp != null) { Shader.globalRenderPipeline = grp; changed++; }
            if (TryBool(body, "wireframe", out var wf)) { GL.wireframe = wf; changed++; }
            if (TryBool(body, "srpBatching", out var srp)) { UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = srp; changed++; }
            if (TryBool(body, "lightsLinearIntensity", out var lli)) { UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity = lli; changed++; }
            if (TryBool(body, "lightsColorTemperature", out var lct)) { UnityEngine.Rendering.GraphicsSettings.lightsUseColorTemperature = lct; changed++; }
            return Ok(changed);
        }

        // ── Layers + collision matrix ───────────────────────────────────────

        public string GetLayers()
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"names\":[");
            for (int i = 0; i < 32; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(Esc(LayerMask.LayerToName(i) ?? "")).Append("\"");
            }
            sb.Append("],\"matrix3d\":[");
            for (int i = 0; i < 32; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("[");
                for (int j = 0; j < 32; j++)
                {
                    if (j > 0) sb.Append(",");
                    // GetIgnoreLayerCollision returns true when collision is ignored. Invert for matrix semantics.
                    sb.Append(Physics.GetIgnoreLayerCollision(i, j) ? "0" : "1");
                }
                sb.Append("]");
            }
            sb.Append("],\"matrix2d\":[");
            for (int i = 0; i < 32; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("[");
                for (int j = 0; j < 32; j++)
                {
                    if (j > 0) sb.Append(",");
                    sb.Append(Physics2D.GetIgnoreLayerCollision(i, j) ? "0" : "1");
                }
                sb.Append("]");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public string ApplyLayers(string body)
        {
            // Body: { "kind":"3d"|"2d", "layerA":int, "layerB":int, "collide":bool }
            var kind = JsonStr(body, "kind") ?? "3d";
            if (!TryInt(body, "layerA", out var a)) return "{\"ok\":false,\"error\":\"layerA required\"}";
            if (!TryInt(body, "layerB", out var b)) return "{\"ok\":false,\"error\":\"layerB required\"}";
            if (!TryBool(body, "collide", out var collide)) return "{\"ok\":false,\"error\":\"collide required\"}";
            a = Mathf.Clamp(a, 0, 31);
            b = Mathf.Clamp(b, 0, 31);
            if (kind == "2d")
                Physics2D.IgnoreLayerCollision(a, b, !collide);
            else
                Physics.IgnoreLayerCollision(a, b, !collide);
            return "{\"ok\":true}";
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static string Ok(int changed) => $"{{\"ok\":true,\"changed\":{changed}}}";

        static void F(StringBuilder sb, string name, float v)
            => sb.Append("\"").Append(name).Append("\":").Append(v.ToString("R", Inv));

        static void V2(StringBuilder sb, Vector2 v)
            => sb.Append("{\"x\":").Append(v.x.ToString("R", Inv))
                 .Append(",\"y\":").Append(v.y.ToString("R", Inv)).Append("}");

        static void V3(StringBuilder sb, Vector3 v)
            => sb.Append("{\"x\":").Append(v.x.ToString("R", Inv))
                 .Append(",\"y\":").Append(v.y.ToString("R", Inv))
                 .Append(",\"z\":").Append(v.z.ToString("R", Inv)).Append("}");

        static void Col(StringBuilder sb, Color c)
            => sb.Append("{\"r\":").Append(c.r.ToString("R", Inv))
                 .Append(",\"g\":").Append(c.g.ToString("R", Inv))
                 .Append(",\"b\":").Append(c.b.ToString("R", Inv))
                 .Append(",\"a\":").Append(c.a.ToString("R", Inv)).Append("}");

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";

        // Read a number key from a flat JSON body. Returns false if missing or
        // non-numeric. Accepts both bare numbers and quoted-string numbers.
        static bool TryNum(string body, string key, out float value)
        {
            value = 0f;
            var raw = RequestRouter.JsonVal(body, key);
            if (raw == null) return false;
            return float.TryParse(raw, NumberStyles.Float, Inv, out value);
        }

        static bool TryInt(string body, string key, out int value)
        {
            value = 0;
            var raw = RequestRouter.JsonVal(body, key);
            if (raw == null) return false;
            if (int.TryParse(raw, NumberStyles.Integer, Inv, out value)) return true;
            // Allow floats like "60.0" — coerce to int.
            if (float.TryParse(raw, NumberStyles.Float, Inv, out var f)) { value = (int)f; return true; }
            return false;
        }

        static bool TryBool(string body, string key, out bool value)
        {
            value = false;
            var raw = RequestRouter.JsonVal(body, key);
            if (raw == null) return false;
            raw = raw.Trim().Trim('"');
            if (raw == "true" || raw == "True" || raw == "1") { value = true; return true; }
            if (raw == "false" || raw == "False" || raw == "0") { value = false; return true; }
            return false;
        }

        // Vector2 from "{\"x\":..,\"y\":..}" embedded under a key.
        static bool TryVec2(string body, string key, out Vector2 v)
        {
            v = default;
            var sub = ExtractObject(body, key);
            if (sub == null) return false;
            if (!TryNum(sub, "x", out var x)) return false;
            if (!TryNum(sub, "y", out var y)) return false;
            v = new Vector2(x, y);
            return true;
        }

        static bool TryVec3(string body, string key, out Vector3 v)
        {
            v = default;
            var sub = ExtractObject(body, key);
            if (sub == null) return false;
            if (!TryNum(sub, "x", out var x)) return false;
            if (!TryNum(sub, "y", out var y)) return false;
            if (!TryNum(sub, "z", out var z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        static bool TryColor(string body, string key, out Color c)
        {
            c = default;
            var sub = ExtractObject(body, key);
            if (sub == null) return false;
            if (!TryNum(sub, "r", out var r)) return false;
            if (!TryNum(sub, "g", out var g)) return false;
            if (!TryNum(sub, "b", out var b)) return false;
            TryNum(sub, "a", out var a);
            if (a == 0f && !sub.Contains("\"a\"")) a = 1f;
            c = new Color(r, g, b, a);
            return true;
        }

        // Extract a balanced { ... } object value for a key from a flat body.
        static string ExtractObject(string body, string key)
        {
            if (string.IsNullOrEmpty(body)) return null;
            var needle = "\"" + key + "\"";
            int i = body.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = body.IndexOf(':', i + needle.Length);
            if (colon < 0) return null;
            int j = colon + 1;
            while (j < body.Length && (body[j] == ' ' || body[j] == '\t')) j++;
            if (j >= body.Length || body[j] != '{') return null;
            int depth = 0;
            bool inStr = false;
            int start = j;
            for (; j < body.Length; j++)
            {
                char c = body[j];
                if (c == '\\' && inStr) { j++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return body.Substring(start, j - start + 1); }
            }
            return null;
        }

        static string JsonStr(string body, string key)
        {
            var raw = RequestRouter.JsonVal(body, key);
            if (raw == null) return null;
            return raw.Trim().Trim('"');
        }

        static void ApplyEnum<TEnum>(string body, string key, Action<TEnum> setter, ref int changed) where TEnum : struct
        {
            var s = JsonStr(body, key);
            if (s == null) return;
            if (Enum.TryParse<TEnum>(s, true, out var v))
            {
                setter(v);
                changed++;
            }
        }
    }
}
