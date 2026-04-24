using UnityEngine;
using UnityEngine.SceneManagement;

// Flat namespace on purpose: nesting under "Bugpunch.*" would shadow the
// ODDGames.Bugpunch.Bugpunch static class for any test script that does
// `using ODDGames.Bugpunch;` (e.g. DebugTools.cs, StoryboardDemo.cs).
namespace BugpunchTestProject
{
    /// <summary>
    /// Spawns a small zoo of test objects (static / kinematic / dynamic / mesh collider)
    /// so the Remote IDE scene-camera render modes and collider bounding-box tiers
    /// have something moving to show.
    ///
    /// Auto-runs in play mode when the active scene is one of the SDK test scenes.
    /// Does not touch production scenes.
    /// </summary>
    public static class BugpunchTestObjectsSpawner
    {
        static readonly string[] TargetSceneNames = { "CityTest", "Test", "Sample", "SampleScene" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnSceneLoaded()
        {
            var scene = SceneManager.GetActiveScene();
            bool match = false;
            for (int i = 0; i < TargetSceneNames.Length; i++)
            {
                if (scene.name == TargetSceneNames[i]) { match = true; break; }
            }
            if (!match) return;

            if (GameObject.Find("[Bugpunch Test Objects]") != null) return;
            Spawn();
        }

        static void Spawn()
        {
            var root = new GameObject("[Bugpunch Test Objects]");
            var origin = new Vector3(0, 0.5f, 0);

            // --- Tier 0 (static): large floor platform with BoxCollider ---
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "TestPlatform (static)";
            platform.transform.SetParent(root.transform, false);
            platform.transform.position = origin + new Vector3(0, -0.5f, 0);
            platform.transform.localScale = new Vector3(20, 1, 20);
            TintMaterial(platform, new Color(0.25f, 0.25f, 0.28f));

            // --- Tier 0 (static): mesh-collider object (tests mesh shape serialization) ---
            var meshObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            meshObj.name = "TestMeshCollider (static)";
            meshObj.transform.SetParent(root.transform, false);
            meshObj.transform.position = origin + new Vector3(-4, 1, 0);
            // Replace the default CapsuleCollider with a MeshCollider so the
            // SceneCameraService.SerializeShape "mesh" branch is exercised.
            Object.Destroy(meshObj.GetComponent<CapsuleCollider>());
            var mc = meshObj.AddComponent<MeshCollider>();
            mc.convex = true;
            TintMaterial(meshObj, new Color(0.55f, 0.35f, 0.15f));

            // --- Tier 1 (kinematic): rotating cube ---
            var kinematic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kinematic.name = "TestKinematicCube (tier 1)";
            kinematic.transform.SetParent(root.transform, false);
            kinematic.transform.position = origin + new Vector3(0, 1.5f, 0);
            var kRb = kinematic.AddComponent<Rigidbody>();
            kRb.isKinematic = true;
            kRb.useGravity = false;
            kinematic.AddComponent<KinematicSpinner>();
            TintMaterial(kinematic, new Color(0.2f, 0.55f, 0.85f));

            // --- Tier 2 (dynamic): bouncing sphere ---
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "TestDynamicSphere (tier 2)";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.position = origin + new Vector3(3, 6, 0);
            var sRb = sphere.AddComponent<Rigidbody>();
            sRb.mass = 1f;
            sRb.linearDamping = 0.1f;
            var bouncyMat = new PhysicsMaterial("BugpunchBouncy")
            {
                bounciness = 0.9f,
                dynamicFriction = 0.2f,
                staticFriction = 0.2f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
            };
            sphere.GetComponent<SphereCollider>().material = bouncyMat;
            TintMaterial(sphere, new Color(0.85f, 0.35f, 0.2f));

            // --- Tier 2 (dynamic): rolling capsule ---
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "TestDynamicCapsule (tier 2)";
            capsule.transform.SetParent(root.transform, false);
            capsule.transform.position = origin + new Vector3(-3, 6, 2);
            capsule.transform.rotation = Quaternion.Euler(0, 0, 90); // on its side so it rolls
            var cRb = capsule.AddComponent<Rigidbody>();
            cRb.mass = 1f;
            cRb.angularDamping = 0.05f;
            TintMaterial(capsule, new Color(0.5f, 0.85f, 0.35f));

            // --- Tier 1 (kinematic) on rails: a cube that drifts back and forth ---
            var drifter = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drifter.name = "TestKinematicDrifter (tier 1)";
            drifter.transform.SetParent(root.transform, false);
            drifter.transform.position = origin + new Vector3(5, 1, 0);
            drifter.transform.localScale = new Vector3(1, 1, 3);
            var dRb = drifter.AddComponent<Rigidbody>();
            dRb.isKinematic = true;
            dRb.useGravity = false;
            drifter.AddComponent<KinematicDrifter>();
            TintMaterial(drifter, new Color(0.75f, 0.55f, 0.85f));

            Debug.Log("[Bugpunch.TestObjects] Spawned test objects (static/kinematic/dynamic/mesh) for wireframe + collider-tier testing.");
        }

        static void TintMaterial(GameObject go, Color color)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            // Use sharedMaterial.color if a standard shader is present; fall back
            // to a throwaway instance.
            var mat = new Material(r.sharedMaterial) { color = color };
            r.sharedMaterial = mat;
        }
    }

    internal class KinematicSpinner : MonoBehaviour
    {
        public Vector3 eulerPerSecond = new Vector3(30, 60, 0);

        void Update()
        {
            transform.Rotate(eulerPerSecond * Time.deltaTime, Space.Self);
        }
    }

    internal class KinematicDrifter : MonoBehaviour
    {
        public float amplitude = 3f;
        public float period = 4f;
        Vector3 _origin;

        void Awake() { _origin = transform.position; }

        void Update()
        {
            float t = Mathf.Sin(Time.time * (Mathf.PI * 2f / period));
            var p = _origin; p.x += amplitude * t;
            transform.position = p;
        }
    }
}
