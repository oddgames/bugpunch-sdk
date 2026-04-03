using PaxScript.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDFramework
{
    public class RemoteIDE : MonoBehaviour, WebServer.IAuthentication
    {

        public class ScriptException : Exception
        {

            private string _stackTrace;

            public override string StackTrace => _stackTrace;

            public ScriptException(string message, string stackTrace) : base(message)
            {
                this._stackTrace = stackTrace;
            }
        }

        private static readonly RemoteIDE remoteIDEServer;
        private static readonly List<Log> log = new List<Log>();
        private static readonly DateTime startTime = DateTime.Now;

        public int Port { get; set; } = 9080;

        static void OnLog(string m, string s, LogType t)
        {
            lock (log)
            {

                var l = new Log() { recid = log.Count, second = (DateTime.Now - startTime).TotalSeconds, message = m, stacktrace = s, type = t.ToString() };

                if (t == LogType.Error || t == LogType.Exception)
                    l.w2ui.style = "background-color: #ff6666";
                else if (t == LogType.Warning)
                    l.w2ui.style = "background-color: #ffff99";

                log.Add(l);
            }
        }

        public static void Stop()
        {
            if (remoteIDEServer != null)
                GameObject.Destroy(remoteIDEServer.gameObject);
        }

        Camera remoteCamera;
        WebServer server;
        PaxScripter script;
        CameraTexture cameraTexture;
        WsServer wsServer;

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;

            if (server != null)
                server.Stop();

            if (wsServer != null)
                wsServer.Stop();
        }

        Transform FindTransformByInstanceID(int instanceId)
        {
            if (instanceId == transform.GetInstanceID())
                return null;

            foreach (var t in GameObject.FindObjectsOfType<Transform>())
                if (t.GetInstanceID() == instanceId)
                    return t;

            return null;
        }

        IEnumerable<Transform> GetChildTransforms(Transform transform)
        {
            for (int x = 0; x < transform.childCount; x++)
                yield return transform.GetChild(x);
        }

        void GetChildren(Record record, IEnumerable<Transform> transforms)
        {
            var list = transforms as IList<Transform> ?? transforms.ToArray();
            record.w2ui = new Children();
            record.w2ui.children = new Record[list.Count];

            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                Record child = new Record()
                {
                    recid = t.GetInstanceID(),
                    name = t.name
                };

                record.w2ui.children[i] = child;

                GetChildren(child, GetChildTransforms(t));
            }
        }

        [System.Serializable]
        public class Inspector
        {
            public string text;
            public int id;
        }

        [System.Serializable]
        public class TreeNode
        {
            public int id;
            public string name;
            public bool hasChildren;
        }

        [System.Serializable]
        public class FieldDesc
        {
            public string name;
            public string type;
            public string[] options;
            public bool isPrivate;
        }

        [System.Serializable]
        public class MemberDesc
        {
            public string name;
            public string kind;
            public string returns;
            public string signature;
            public bool isStatic;
        }

        [System.Serializable]
        public class SignatureDesc
        {
            public string label;
            public ParameterDesc[] parameters;
        }

        [System.Serializable]
        public class ParameterDesc
        {
            public string label;
        }

        [System.Serializable]
        public class TypeDesc
        {
            public string n;
            public string ns;
        }

        [System.Serializable]
        public class ScriptResult
        {
            public bool ok;
            public ScriptErrorDesc[] errors;
        }

        [System.Serializable]
        public class ScriptErrorDesc
        {
            public int line;
            public string message;
        }

        [System.Serializable]
        public class MethodDesc
        {
            public string name;
            public string signature;
            public bool hasParams;
            public bool isPrivate;
            public bool isStatic;
        }

        [System.Serializable]
        public class InvokeResult
        {
            public bool ok;
            public string result;
            public string error;
        }

        private WebServerResponse GetHierarchy()
        {

   
            Record[] result = new Record[SceneManager.sceneCount + 1];

            for (int x = 0; x < result.Length - 1; x++)
            {

                var scene = SceneManager.GetSceneAt(x);

                result[x] = new Record()
                {
                    recid = scene.buildIndex,
                    name = scene.name
                };

                GetChildren(result[x], scene.GetRootGameObjects().Select(go => go.transform));

            }

            result[result.Length - 1] = new Record()
            {
                recid = this.gameObject.scene.buildIndex,
                name = this.gameObject.scene.name
            };

            GetChildren(result[result.Length - 1], this.gameObject.scene.GetRootGameObjects().Select(go => go.transform));
            
            return WebServerResponse.AsJson(result);

     
        }

        static float ParseJsonFloat(string json, string key, float fallback)
        {
            int idx = json.IndexOf("\"" + key + "\"");
            if (idx < 0) return fallback;
            idx = json.IndexOf(':', idx);
            if (idx < 0) return fallback;
            idx++;
            int end = idx;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            if (float.TryParse(json.Substring(idx, end - idx).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                return val;
            return fallback;
        }

        static FieldDesc[] GetSerializableFields(Type componentType)
        {
            var fields = new List<FieldDesc>();
            foreach (var f in componentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.IsStatic || f.IsInitOnly) continue;
                if (f.IsDefined(typeof(NonSerializedAttribute), true)) continue;
                if (f.IsDefined(typeof(HideInInspector), true)) continue;
                if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), true)) continue;

                var desc = new FieldDesc { name = f.Name, isPrivate = !f.IsPublic };
                var ft = f.FieldType;

                if (ft == typeof(int) || ft == typeof(long)) desc.type = "int";
                else if (ft == typeof(float) || ft == typeof(double)) desc.type = "float";
                else if (ft == typeof(bool)) desc.type = "bool";
                else if (ft == typeof(string)) desc.type = "string";
                else if (ft == typeof(Vector2)) desc.type = "Vector2";
                else if (ft == typeof(Vector3)) desc.type = "Vector3";
                else if (ft == typeof(Vector4)) desc.type = "Vector4";
                else if (ft == typeof(Color)) desc.type = "Color";
                else if (ft == typeof(Quaternion)) desc.type = "Quaternion";
                else if (ft == typeof(Rect)) desc.type = "Rect";
                else if (ft.IsEnum)
                {
                    desc.type = "enum";
                    desc.options = Enum.GetNames(ft);
                }
                else desc.type = "object";

                fields.Add(desc);
            }
            return fields.ToArray();
        }

        /// Returns ALL instance fields (public and private) for debug mode inspection.
        static FieldDesc[] GetAllFields(Type componentType)
        {
            var fields = new List<FieldDesc>();
            foreach (var f in componentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.IsStatic) continue;

                var desc = new FieldDesc { name = f.Name, isPrivate = !f.IsPublic };
                var ft = f.FieldType;

                if (ft == typeof(int) || ft == typeof(long)) desc.type = "int";
                else if (ft == typeof(float) || ft == typeof(double)) desc.type = "float";
                else if (ft == typeof(bool)) desc.type = "bool";
                else if (ft == typeof(string)) desc.type = "string";
                else if (ft == typeof(Vector2)) desc.type = "Vector2";
                else if (ft == typeof(Vector3)) desc.type = "Vector3";
                else if (ft == typeof(Vector4)) desc.type = "Vector4";
                else if (ft == typeof(Color)) desc.type = "Color";
                else if (ft == typeof(Quaternion)) desc.type = "Quaternion";
                else if (ft == typeof(Rect)) desc.type = "Rect";
                else if (ft.IsEnum)
                {
                    desc.type = "enum";
                    desc.options = Enum.GetNames(ft);
                }
                else desc.type = "object";

                fields.Add(desc);
            }
            return fields.ToArray();
        }

        public void Initialize(int remotePort)
        {
            Port = remotePort;

            Application.logMessageReceivedThreaded += OnLog;

            GameObject.DontDestroyOnLoad(this.gameObject);

            remoteCamera = this.gameObject.AddComponent<Camera>();
            remoteCamera.enabled = false;
            remoteCamera.clearFlags = CameraClearFlags.SolidColor;
            remoteCamera.backgroundColor = Color.magenta;
            remoteCamera.useOcclusionCulling = false;
            remoteCamera.allowHDR = false;
            remoteCamera.allowDynamicResolution = true;
            remoteCamera.allowMSAA = false;

            cameraTexture = new CameraTexture();
            _ = Coroutiner.Shared; // Pre-init on main thread

            server = new WebServer(Port, this);

            server["/hierarchy"] = async (req) =>
            {

                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    return GetHierarchy();
                });


            };

            server["/hierarchy", "POST"] = async (req) =>
            {

                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    return GetHierarchy();
                });


            };

            // Returns scenes as root nodes for lazy tree
            server["/scenes"] = async (req) =>
            {
                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    var scenes = new List<TreeNode>();
                    for (int x = 0; x < SceneManager.sceneCount; x++)
                    {
                        var scene = SceneManager.GetSceneAt(x);
                        scenes.Add(new TreeNode { id = -(x + 1), name = scene.name, hasChildren = scene.rootCount > 0 });
                    }
                    // DontDestroyOnLoad scene
                    var ddol = this.gameObject.scene;
                    scenes.Add(new TreeNode { id = -(SceneManager.sceneCount + 1), name = ddol.name, hasChildren = ddol.rootCount > 0 });
                    return WebServerResponse.AsJson(scenes.ToArray());
                });
            };

            // Returns direct children (lazy tree expansion)
            server["/children"] = async (req) =>
            {
                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    int id = req.Query<int>("id");

                    // Negative IDs = scene roots (1-based negated)
                    if (id < 0)
                    {
                        int sceneIdx = (-id) - 1;
                        Scene scene;
                        if (sceneIdx < SceneManager.sceneCount)
                            scene = SceneManager.GetSceneAt(sceneIdx);
                        else
                            scene = this.gameObject.scene;

                        return WebServerResponse.AsJson(scene.GetRootGameObjects()
                            .Select(go => new TreeNode { id = go.transform.GetInstanceID(), name = go.name, hasChildren = go.transform.childCount > 0 })
                            .ToArray());
                    }

                    // Positive IDs = transform instance IDs
                    var t = FindTransformByInstanceID(id);
                    if (t == null)
                        return WebServerResponse.AsJson(Array.Empty<TreeNode>());

                    var children = new TreeNode[t.childCount];
                    for (int i = 0; i < t.childCount; i++)
                    {
                        var child = t.GetChild(i);
                        children[i] = new TreeNode { id = child.GetInstanceID(), name = child.name, hasChildren = child.childCount > 0 };
                    }
                    return WebServerResponse.AsJson(children);
                });
            };

            server["/apply"] = async (req) =>
            {

                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    ODDFramework.Bug.SetTag("propertiesChanged", "true", "RemoteIDE");

                    int instanceId = req.Query<int>("instanceId");
                    int componentId = req.Query<int>("componentId");

                    var t = FindTransformByInstanceID(instanceId);
                    if (t == null)
                        return WebServerResponse.AsError("Unable to locate target");

                    foreach (var m in t.GetComponents<Component>())
                    {
                        if (m.GetInstanceID() == componentId)
                        {
                            try
                            {
                                JsonUtility.FromJsonOverwrite(req.Text, m);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"Unable to overwrite {instanceId}:{componentId} with '{req.Text}'");
                                Debug.LogException(e);
                            }

                            return WebServerResponse.OK;
                        }
                    }

                    return WebServerResponse.AsError("Unable to locate target");

                });

            };

            server["/inspect"] = async (req) =>
            {

                return await ThreadSafe.UnityThread.Invoke(() =>
                {

                    int instanceId = req.Query<int>("instanceid");

                    var t = FindTransformByInstanceID(instanceId);
                    if (t != null)
                        return WebServerResponse.AsJson(t.GetComponents<Component>().Select(b => new Inspector() { text = b.GetType().FullName, id = b.GetInstanceID() }).ToArray());

                    return WebServerResponse.OK;

                });

            };

            server["/component"] = async (req) =>
            {

                return await ThreadSafe.UnityThread.Invoke(() =>
                {

                    int instanceId = req.Query<int>("instanceid");
                    int componentId = req.Query<int>("componentid");

                    var t = FindTransformByInstanceID(instanceId);
                    if (t != null)
                    {
                        foreach (var m in t.GetComponents<Component>())
                        {
                            if (m.GetInstanceID() == componentId)
                                return WebServerResponse.AsJson(JsonUtility.ToJson(m));
                        }
                    }

                    return WebServerResponse.AsError("Component not found");

                });

            };

            server["/fields"] = async (req) =>
            {
                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    int instanceId = req.Query<int>("instanceid");
                    int componentId = req.Query<int>("componentid");
                    bool debug = req.Query<string>("debug") == "true";

                    var t = FindTransformByInstanceID(instanceId);
                    if (t == null)
                        return WebServerResponse.AsError("Transform not found");

                    foreach (var m in t.GetComponents<Component>())
                    {
                        if (m.GetInstanceID() == componentId)
                            return WebServerResponse.AsJson(debug ? GetAllFields(m.GetType()) : GetSerializableFields(m.GetType()));
                    }

                    return WebServerResponse.AsError("Component not found");
                });
            };

            // Returns methods on a component (for invoke buttons). Debug mode includes private/static.
            server["/methods"] = async (req) =>
            {
                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    int instanceId = req.Query<int>("instanceid");
                    int componentId = req.Query<int>("componentid");
                    bool debug = req.Query<string>("debug") == "true";

                    var bindingFlags = debug
                        ? BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly
                        : BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                    var t = FindTransformByInstanceID(instanceId);
                    if (t == null)
                        return WebServerResponse.AsJson("[]");

                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c.GetInstanceID() == componentId)
                        {
                            var methods = new List<MethodDesc>();
                            foreach (var m in c.GetType().GetMethods(bindingFlags))
                            {
                                if (m.IsSpecialName) continue; // skip property getters/setters
                                var pars = m.GetParameters();
                                var parStr = pars.Length > 0
                                    ? string.Join(", ", pars.Select(p => p.ParameterType.Name + " " + p.Name))
                                    : "";
                                methods.Add(new MethodDesc
                                {
                                    name = m.Name,
                                    signature = m.ReturnType.Name + " " + m.Name + "(" + parStr + ")",
                                    hasParams = pars.Length > 0,
                                    isPrivate = !m.IsPublic,
                                    isStatic = m.IsStatic
                                });
                            }
                            return WebServerResponse.AsJson(JSONWriter.ToJson(methods.ToArray()));
                        }
                    }
                    return WebServerResponse.AsJson("[]");
                });
            };

            // Invokes a method on a component instance. Debug mode allows private/static methods. Supports args.
            server["/invoke", "POST"] = async (req) =>
            {
                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    int instanceId = req.Query<int>("instanceid");
                    int componentId = req.Query<int>("componentid");
                    string methodName = req.Query<string>("method");
                    bool debug = req.Query<string>("debug") == "true";
                    string argsStr = req.Query<string>("args");

                    var bindingFlags = debug
                        ? BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                        : BindingFlags.Public | BindingFlags.Instance;

                    var t = FindTransformByInstanceID(instanceId);
                    if (t == null)
                        return WebServerResponse.AsJson(JSONWriter.ToJson(new InvokeResult { ok = false, error = "Transform not found" }));

                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c.GetInstanceID() == componentId)
                        {
                            MethodInfo method;
                            if (!string.IsNullOrEmpty(argsStr))
                            {
                                // Find method by name only (args provided), pick first match with right param count
                                var argParts = argsStr.Split(',');
                                method = c.GetType().GetMethods(bindingFlags)
                                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == argParts.Length);
                            }
                            else
                            {
                                method = c.GetType().GetMethod(methodName, bindingFlags, null, Type.EmptyTypes, null);
                            }

                            if (method == null)
                                return WebServerResponse.AsJson(JSONWriter.ToJson(new InvokeResult { ok = false, error = "Method not found: " + methodName }));

                            try
                            {
                                object[] invokeArgs = null;
                                if (!string.IsNullOrEmpty(argsStr))
                                {
                                    var argParts = argsStr.Split(',');
                                    var pars = method.GetParameters();
                                    invokeArgs = new object[pars.Length];
                                    for (int i = 0; i < pars.Length; i++)
                                        invokeArgs[i] = Convert.ChangeType(argParts[i].Trim(), pars[i].ParameterType, CultureInfo.InvariantCulture);
                                }

                                var instance = method.IsStatic ? null : c;
                                var ret = method.Invoke(instance, invokeArgs);
                                return WebServerResponse.AsJson(JSONWriter.ToJson(new InvokeResult
                                {
                                    ok = true,
                                    result = ret != null ? ret.ToString() : "void"
                                }));
                            }
                            catch (Exception e)
                            {
                                var inner = e.InnerException ?? e;
                                return WebServerResponse.AsJson(JSONWriter.ToJson(new InvokeResult
                                {
                                    ok = false,
                                    error = inner.GetType().Name + ": " + inner.Message
                                }));
                            }
                        }
                    }
                    return WebServerResponse.AsJson(JSONWriter.ToJson(new InvokeResult { ok = false, error = "Component not found" }));
                });
            };

            server["/hierarchy/delete", "POST"] = async (Request req) =>
            {

                await ThreadSafe.UnityThread.Invoke(() =>
                {

                    int instanceId = req.Query<int>("instanceid");

                    var t = FindTransformByInstanceID(instanceId);
                    if (t != null)
                        GameObject.Destroy(t.gameObject);

                });

                return WebServerResponse.OK;

            };

            server["/capture"] = async (req) =>
            {
                var instanceid = req.Query<int>("id");
                var scale = req.Query<float>("scale");
                var qStr = req.Query<string>("quality");
                int quality = Mathf.Clamp(string.IsNullOrEmpty(qStr) ? 50 : int.Parse(qStr), 10, 100);

                // Run entire capture on Unity thread — Update() requires main thread for WaitForEndOfFrame
                byte[] jpeg = await Task.Factory.StartNew(async () =>
                {
                    if (instanceid == -1)
                        cameraTexture.Camera = null;
                    else if (cameraTexture.Camera == null || cameraTexture.Camera.GetInstanceID() != instanceid)
                        cameraTexture.Camera = Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.GetInstanceID().Equals(instanceid)).FirstOrDefault();

                    cameraTexture.Scale = Mathf.Clamp(scale, 0.2f, 1);

                    await cameraTexture.Update();

                    return cameraTexture.Texture.EncodeToJPG(quality);
                }, System.Threading.CancellationToken.None, TaskCreationOptions.None, ThreadSafe.UnityThread.Scheduler).Unwrap();

                return new WebServerResponse()
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    ContentType = "image/jpeg",
                    Stream = new System.IO.MemoryStream(jpeg)
                };
            };

            server["/"] = async (req) => await WebServerResponse.AsResource("RemoteIDE/index");

            server["/favicon.ico"] = (req) => Task.FromResult(WebServerResponse.OK);

            server["/log"] = (req) =>
            {

                int logId = req.Query<int>("logId");

                Log[] snapshot;
                lock (log)
                {
                    // Binary search: log IDs are sequential, so skip directly to the right index
                    int startIndex = logId + 1;
                    if (startIndex < 0) startIndex = 0;
                    if (startIndex >= log.Count)
                        snapshot = Array.Empty<Log>();
                    else
                        snapshot = log.GetRange(startIndex, log.Count - startIndex).ToArray();
                }

                return Task.FromResult<WebServerResponse>(WebServerResponse.AsJson(snapshot));

            };

            server["/run", "POST"] = async (req) =>
            {
                var result = await ThreadSafe.UnityThread.Invoke(() =>
                {
                    Bug.SetTag("codeRan", "true", "RemoteIDE");

                    try
                    {
                        if (script == null)
                        {
                            script = new PaxScripter();
                            script.scripter.SearchProtected = true;
                        }

                        script.ResetModules();
                        script.AddModule("1");
                        script.AddCode("1", req.Text);
                        script.Compile();

                        if (!script.HasErrors)
                            script.Run(RunMode.Run);

                        if (script.HasErrors)
                        {
                            var errors = new List<ScriptErrorDesc>();
                            foreach (ScriptError error in script.Error_List)
                            {
                                string message = error.E != null
                                    ? $"{error.E.GetType().Name}: {error.E.Message}"
                                    : error.Message;

                                // LineNumber is 0-based from PaxScript
                                errors.Add(new ScriptErrorDesc { line = error.LineNumber + 1, message = message });

                                Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "{0}", new object[] { message });
                            }
                            return new ScriptResult { ok = false, errors = errors.ToArray() };
                        }

                        return new ScriptResult { ok = true, errors = Array.Empty<ScriptErrorDesc>() };
                    }
                    catch (Exception e)
                    {
                        var msg = $"{e.GetType().Name}: {e.Message}";
                        Debug.LogWarning($"Scripting Engine exception: {msg}");
                        return new ScriptResult { ok = false, errors = new[] { new ScriptErrorDesc { line = 1, message = msg } } };
                    }
                });

                return WebServerResponse.AsJson(JSONWriter.ToJson(result));
            };

            server["/cameras", "POST"] = async (req) =>
            {

                return await ThreadSafe.UnityThread.Invoke(() =>
                {
                    return WebServerResponse.AsJson(
                        Resources.FindObjectsOfTypeAll<Camera>()
                            .Where(c => c.isActiveAndEnabled && c.targetTexture == null)
                            .Select(c => new Item() { id = c.GetInstanceID(), text = c.name })
                            .Concat(new Item() { id = -1, text = "[Game]" })
                            .ToArray());
                });

            };

            Dictionary<string, Type> typeByName = null;
            Dictionary<string, Type> typeByNameCI = null;
            Dictionary<string, Type> typeByFullName = null;
            List<Type> allTypes = null;
            string cachedTypes = null;
            var memberCache = new Dictionary<string, string>();

            void EnsureTypeLookup()
            {
                if (typeByName != null) return;
                typeByName = new Dictionary<string, Type>();
                typeByNameCI = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                typeByFullName = new Dictionary<string, Type>();
                allTypes = new List<Type>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic) continue;
                    try
                    {
                        foreach (var t in asm.GetExportedTypes())
                        {
                            if (t.IsPublic && !t.IsNested)
                            {
                                allTypes.Add(t);
                                typeByName[t.Name] = t;
                                if (!typeByNameCI.ContainsKey(t.Name))
                                    typeByNameCI[t.Name] = t;
                                if (t.FullName != null)
                                    typeByFullName[t.FullName] = t;
                            }
                        }
                    }
                    catch { }
                }
            }

            bool TryGetType(string name, out Type t)
            {
                EnsureTypeLookup();
                return typeByName.TryGetValue(name, out t)
                    || typeByNameCI.TryGetValue(name, out t)
                    || typeByFullName.TryGetValue(name, out t);
            }

            server["/types"] = (req) =>
            {
                if (cachedTypes == null)
                {
                    EnsureTypeLookup();
                    cachedTypes = JSONWriter.ToJson(allTypes
                        .OrderBy(t => t.Name)
                        .Select(t => new TypeDesc { n = t.Name, ns = t.Namespace ?? "" })
                        .ToArray());
                }
                return Task.FromResult(WebServerResponse.AsJson(cachedTypes));
            };

            string cachedNamespaces = null;
            server["/namespaces"] = (req) =>
            {
                if (cachedNamespaces == null)
                {
                    EnsureTypeLookup();
                    var roots = new HashSet<string>();
                    foreach (var t in allTypes)
                    {
                        if (string.IsNullOrEmpty(t.Namespace)) continue;
                        var dot = t.Namespace.IndexOf('.');
                        roots.Add(dot >= 0 ? t.Namespace.Substring(0, dot) : t.Namespace);
                    }
                    cachedNamespaces = JSONWriter.ToJson(roots.OrderBy(n => n).ToArray());
                }
                return Task.FromResult(WebServerResponse.AsJson(cachedNamespaces));
            };

            // Returns all overloads of a method for signature help
            server["/signatures"] = (req) =>
            {
                string typeName = req.Query<string>("type");
                string methodName = req.Query<string>("method");
                if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
                    return Task.FromResult(WebServerResponse.AsJson("[]"));

                if (!TryGetType(typeName, out Type type))
                    return Task.FromResult(WebServerResponse.AsJson("[]"));

                var sigs = new List<SignatureDesc>();
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (m.Name != methodName || m.IsSpecialName) continue;
                    var pars = m.GetParameters();
                    var parStr = string.Join(", ", pars.Select(p => p.ParameterType.Name + " " + p.Name));
                    sigs.Add(new SignatureDesc
                    {
                        label = m.ReturnType.Name + " " + m.Name + "(" + parStr + ")",
                        parameters = pars.Select(p => new ParameterDesc { label = p.ParameterType.Name + " " + p.Name }).ToArray()
                    });
                }

                // Constructors when methodName matches type
                if (methodName == type.Name)
                {
                    foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var pars = c.GetParameters();
                        var parStr = string.Join(", ", pars.Select(p => p.ParameterType.Name + " " + p.Name));
                        sigs.Add(new SignatureDesc
                        {
                            label = type.Name + "(" + parStr + ")",
                            parameters = pars.Select(p => new ParameterDesc { label = p.ParameterType.Name + " " + p.Name }).ToArray()
                        });
                    }
                }

                return Task.FromResult(WebServerResponse.AsJson(JSONWriter.ToJson(sigs.ToArray())));
            };

            // Resolves a member chain to its final type name via return types
            // e.g. "Transform.position" → "Vector3", "GameObject.transform.position" → "Vector3"
            // With ?info=true returns {type, isEnum, isValueType} for assignment context
            server["/resolve"] = (req) =>
            {
                string chain = req.Query<string>("chain");
                if (string.IsNullOrEmpty(chain))
                    return Task.FromResult(WebServerResponse.AsJson("\"\""));

                var parts = chain.Split('.');
                Type current = null;
                int start = 0;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (TryGetType(parts[i], out current))
                    {
                        start = i + 1;
                        break;
                    }
                }

                if (current == null)
                    return Task.FromResult(WebServerResponse.AsJson("\"\""));

                var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase;
                for (int i = start; i < parts.Length; i++)
                {
                    var prop = current.GetProperty(parts[i], flags);
                    if (prop != null) { current = prop.PropertyType; continue; }
                    var fld = current.GetField(parts[i], flags);
                    if (fld != null) { current = fld.FieldType; continue; }
                    var mtd = current.GetMethods(flags)
                        .FirstOrDefault(m => string.Equals(m.Name, parts[i], StringComparison.OrdinalIgnoreCase) && !m.IsSpecialName);
                    if (mtd != null) { current = mtd.ReturnType; continue; }
                    return Task.FromResult(WebServerResponse.AsJson("\"\""));
                }

                if (req.Query<string>("info") == "true")
                    return Task.FromResult(WebServerResponse.AsJson(
                        "{\"type\":\"" + current.Name + "\",\"isEnum\":" + (current.IsEnum ? "true" : "false") +
                        ",\"isValueType\":" + (current.IsValueType ? "true" : "false") + "}"));

                return Task.FromResult(WebServerResponse.AsJson("\"" + current.Name + "\""));
            };

            server["/members"] = (req) =>
            {
                string typeName = req.Query<string>("type");
                if (string.IsNullOrEmpty(typeName))
                    return Task.FromResult(WebServerResponse.AsJson("[]"));

                if (!memberCache.TryGetValue(typeName, out string json))
                {
                    if (TryGetType(typeName, out Type type))
                    {
                        var members = new List<MemberDesc>();
                        var seen = new HashSet<string>();

                        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                        {
                            if (p.DeclaringType == typeof(object)) continue;
                            if (seen.Add(p.Name))
                            {
                                var getter = p.GetGetMethod();
                                members.Add(new MemberDesc
                                {
                                    name = p.Name, kind = "property", returns = p.PropertyType.Name,
                                    isStatic = getter != null && getter.IsStatic,
                                    signature = p.PropertyType.Name + " " + p.Name + " { " + (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}"
                                });
                            }
                        }

                        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                        {
                            if (f.IsSpecialName) continue;
                            if (seen.Add(f.Name))
                                members.Add(new MemberDesc
                                {
                                    name = f.Name, kind = "field", returns = f.FieldType.Name,
                                    isStatic = f.IsStatic,
                                    signature = f.FieldType.Name + " " + f.Name
                                });
                        }

                        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                        {
                            if (m.IsSpecialName || m.DeclaringType == typeof(object)) continue;
                            if (seen.Add(m.Name))
                            {
                                var pars = m.GetParameters();
                                var parStr = string.Join(", ", pars.Select(pr => pr.ParameterType.Name + " " + pr.Name));
                                members.Add(new MemberDesc
                                {
                                    name = m.Name, kind = "method", returns = m.ReturnType.Name,
                                    isStatic = m.IsStatic,
                                    signature = m.ReturnType.Name + " " + m.Name + "(" + parStr + ")"
                                });
                            }
                        }

                        json = JSONWriter.ToJson(members.OrderBy(m => m.name).ToArray());
                    }
                    else
                    {
                        // Check if it's a namespace and return types + sub-namespaces
                        var nsMembers = new List<MemberDesc>();
                        var subNs = new HashSet<string>();
                        var nsPrefix = typeName + ".";
                        foreach (var t in allTypes)
                        {
                            if (t.Namespace == typeName)
                                nsMembers.Add(new MemberDesc { name = t.Name, kind = "type", returns = "" });
                            else if (t.Namespace != null && t.Namespace.StartsWith(nsPrefix))
                            {
                                var rest = t.Namespace.Substring(nsPrefix.Length);
                                var dot = rest.IndexOf('.');
                                subNs.Add(dot >= 0 ? rest.Substring(0, dot) : rest);
                            }
                        }
                        foreach (var ns in subNs)
                            nsMembers.Add(new MemberDesc { name = ns, kind = "namespace", returns = "" });

                        json = nsMembers.Count > 0
                            ? JSONWriter.ToJson(nsMembers.OrderBy(m => m.name).ToArray())
                            : "[]";
                    }
                    memberCache[typeName] = json;
                }

                return Task.FromResult(WebServerResponse.AsJson(json));
            };

            server["/fly"] = async (req) =>
            {

                await ThreadSafe.UnityThread.Invoke(() =>
                {
                    float lookX = req.Query<float>("lx");
                    float lookY = req.Query<float>("ly");
                    float moveX = req.Query<float>("mx");
                    float moveY = req.Query<float>("my");

                    if (lookX != 0 || lookY != 0)
                    {
                        float newRotationX = transform.localEulerAngles.y + lookX * 5;
                        float newRotationY = transform.localEulerAngles.x - lookY * 5;
                        transform.localEulerAngles = new Vector3(newRotationY, newRotationX, 0f);
                    }

                    if (moveX != 0 || moveY != 0)
                    {
                        transform.position += transform.right * 5 * moveX;
                        transform.position += -transform.forward * 5 * moveY;
                    }
                });

                return WebServerResponse.OK;

            };

            server["/camera"] = async (req) =>
            {

                await ThreadSafe.UnityThread.Invoke(() =>
                {
                    if (int.TryParse(req.Text, out int instanceid))
                        cameraTexture.Camera = Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.GetInstanceID().Equals(instanceid)).FirstOrDefault();

                });

                return WebServerResponse.OK;


            };

            // WebSocket camera stream on Port+1 (raw TCP, bypasses HttpListener WS limitations)
            try
            {
            wsServer = new WsServer(Port + 1, async (ws) =>
            {
                int wsCamId = -1;
                float wsScale = 0.5f;
                int wsInterval = 100;
                int wsQuality = 50;

                // Background receive loop for control messages
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (ws.IsOpen)
                        {
                            var text = await ws.ReceiveTextAsync();
                            if (text == null) break;

                            if (text.Contains("\"lx\""))
                            {
                                float lx = ParseJsonFloat(text, "lx", 0);
                                float ly = ParseJsonFloat(text, "ly", 0);
                                float mx = ParseJsonFloat(text, "mx", 0);
                                float my = ParseJsonFloat(text, "my", 0);

                                if (lx != 0 || ly != 0 || mx != 0 || my != 0)
                                {
                                    await ThreadSafe.UnityThread.Invoke(() =>
                                    {
                                        if (lx != 0 || ly != 0)
                                        {
                                            float newRotationX = transform.localEulerAngles.y + lx * 5;
                                            float newRotationY = transform.localEulerAngles.x - ly * 5;
                                            transform.localEulerAngles = new Vector3(newRotationY, newRotationX, 0f);
                                        }
                                        if (mx != 0 || my != 0)
                                        {
                                            transform.position += transform.right * 5 * mx;
                                            transform.position += -transform.forward * 5 * my;
                                        }
                                    });
                                }
                            }
                            else
                            {
                                wsCamId = (int)ParseJsonFloat(text, "cameraId", wsCamId);
                                wsScale = ParseJsonFloat(text, "scale", wsScale);
                                wsInterval = (int)ParseJsonFloat(text, "interval", wsInterval);
                                wsQuality = (int)ParseJsonFloat(text, "quality", wsQuality);
                            }
                        }
                    }
                    catch { }
                });

                // Frame push loop
                try
                {
                    while (ws.IsOpen)
                    {
                        await ThreadSafe.UnityThread.Invoke(() =>
                        {
                            if (wsCamId == -1)
                                cameraTexture.Camera = null;
                            else if (cameraTexture.Camera == null || cameraTexture.Camera.GetInstanceID() != wsCamId)
                                cameraTexture.Camera = Resources.FindObjectsOfTypeAll<Camera>().FirstOrDefault(c => c.GetInstanceID() == wsCamId);
                            cameraTexture.Scale = Mathf.Clamp(wsScale, 0.2f, 1);
                        });

                        await cameraTexture.Update();

                        byte[] jpeg = await ThreadSafe.UnityThread.Invoke(() =>
                            cameraTexture.Texture.EncodeToJPG(Mathf.Clamp(wsQuality, 10, 100))
                        );

                        if (ws.IsOpen)
                            await ws.SendBinaryAsync(jpeg);

                        await Task.Delay(Math.Max(16, wsInterval));
                    }
                }
                catch { }
            });
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteIDE] WsServer failed to start on port {Port + 1}: {e.Message}");
            }

        }

        [System.Serializable]
        protected class Root<T>
        {
            public T root;

            public Root() { }

            public Root(T root)
            {
                this.root = root;
            }
        }


        [System.Serializable]
        public class Item
        {
            public int id;
            public string text;
        }

        [System.Serializable]
        public class Record
        {
            public int recid;
            public string name;
            public Children w2ui;
        }

        [System.Serializable]
        public class Children
        {
            public Record[] children;
        }

        [System.Serializable]
        public class Log
        {
            public int recid;
            public double second;
            public string type;
            public string message;
            public string stacktrace;
            public W2UI w2ui = new W2UI();

            [System.Serializable]
            public class W2UI
            {
                public string style;
            }
        }

        public Task<bool> IsAuthenticated(IPrincipal identity, string hostAddress)
        {
            return Task.FromResult(true);
        }

    }
}
