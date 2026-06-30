using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace ODDGames.Scripting.Unity
{
    /// <summary>
    /// Reusable Unity driver for the engine-agnostic <see cref="ScriptBehaviourManager"/>. A hidden MonoBehaviour that:
    /// ticks every registered script each frame (gated by a host "is playing" predicate, so scripts run only
    /// while the game is in its play state), marshals background-thread work — e.g. the IDE's attach
    /// handler — onto the Unity main thread, and fans game events out to scripts.
    ///
    /// <para>A host game creates one with <see cref="Create"/>, passing its <see cref="IScriptObjectHost"/>
    /// (object listing + attach for the IDE), compile options, the injected-field preamble
    /// (<see cref="UnityScriptConventions.InjectedFields"/>), and an is-playing gate. Scripted objects then
    /// register themselves and route their Unity callbacks through here. Everything Unity-specific but
    /// game-agnostic lives here, so a host only writes its own object/event glue.</para>
    /// </summary>
    public sealed class ScriptRuntime : MonoBehaviour
    {
        private ScriptBehaviourManager _manager;
        private Func<bool> _isPlaying;
        private readonly ConcurrentQueue<Action> _mainThreadWork = new ConcurrentQueue<Action>();

        /// <summary>The underlying engine-agnostic manager (for hosts that need the IDE catalog/attach hooks).</summary>
        public ScriptBehaviourManager Manager => _manager;

        /// <summary>
        /// Spin up the runtime. <paramref name="host"/> supplies the IDE Objects-tab listing/attach;
        /// <paramref name="injectedFields"/> are the typed context fields (e.g.
        /// <see cref="UnityScriptConventions.InjectedFields"/>); <paramref name="isPlaying"/> gates per-frame
        /// ticking (null → always tick).
        /// </summary>
        public static ScriptRuntime Create(IScriptObjectHost host, ScriptCompileOptions options, string injectedFields,
                                           Func<bool> isPlaying = null, Action<string, string> log = null)
        {
            var go = new GameObject("~ScriptRuntime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            var rt = go.AddComponent<ScriptRuntime>();
            rt._manager = new ScriptBehaviourManager(options, host, injectedFields, log ?? DefaultLog);
            rt._isPlaying = isPlaying ?? (() => true);
            return rt;
        }

        /// <summary>Default log sink: surface script errors/warnings in the Unity console (hosts can override).</summary>
        private static void DefaultLog(string level, string message)
        {
            if (level == "error") Debug.LogWarning("[Script] " + message);
            else if (level == "warning") Debug.LogWarning("[Script] " + message);
        }

        private void Update()
        {
            while (_mainThreadWork.TryDequeue(out var work))
            {
                try { work(); }
                catch (Exception ex) { Debug.LogWarning("[ScriptRuntime] " + ex.Message); }
            }

            if (_isPlaying()) _manager.Tick(Time.deltaTime);
        }

        /// <summary>Start running a scripted object's script (Start + per-frame Update).</summary>
        public void Register(IScriptedObject obj) => _manager.Register(obj);

        /// <summary>Stop and tear down a scripted object's script.</summary>
        public void Unregister(string objectId) => _manager.Unregister(objectId);

        /// <summary>Dispatch one object's own callback (e.g. its collision/trigger) to its script.</summary>
        public void Send(string objectId, string callback, params object[] args) => _manager.SendTo(objectId, callback, args);

        /// <summary>Broadcast a global game event to every running script.</summary>
        public void Raise(string gameEvent, params object[] args) => _manager.Raise(gameEvent, args);

        /// <summary>
        /// Run <paramref name="fn"/> on the Unity main thread and block (with timeout) for its result —
        /// for background-thread callers like the IDE's <c>EnumerateObjects</c>/<c>Attach</c>, which must
        /// touch the scene. Returns <c>default</c> on timeout.
        /// </summary>
        public T RunOnMainThread<T>(Func<T> fn, int timeoutSeconds = 8)
        {
            if (fn == null) return default;
            T result = default;
            using (var done = new ManualResetEventSlim(false))
            {
                _mainThreadWork.Enqueue(() =>
                {
                    try { result = fn(); }
                    catch (Exception ex) { Debug.LogWarning("[ScriptRuntime] main-thread work: " + ex.Message); }
                    finally { done.Set(); }
                });
                if (!done.Wait(TimeSpan.FromSeconds(timeoutSeconds))) return default;
            }
            return result;
        }
    }
}
