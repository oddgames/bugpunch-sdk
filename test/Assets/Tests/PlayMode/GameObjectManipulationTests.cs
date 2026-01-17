using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// Tests for GameObject manipulation methods: Disable, Enable, Freeze, Teleport, NoClip, Clip.
    /// </summary>
    [TestFixture]
    public class GameObjectManipulationTests
    {
        private GameObject _testObject;
        private GameObject _childObject;

        [SetUp]
        public void Setup()
        {
            // Create test hierarchy with physics components
            _testObject = new GameObject("TestObject");
            _childObject = new GameObject("ChildObject");
            _childObject.transform.SetParent(_testObject.transform);

            // Add rigidbodies
            var rb = _testObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            var childRb = _childObject.AddComponent<Rigidbody>();
            childRb.useGravity = false;

            // Add colliders
            _testObject.AddComponent<BoxCollider>();
            _childObject.AddComponent<SphereCollider>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
                Object.Destroy(_testObject);
        }

        #region Disable/Enable Tests

        [UnityTest]
        public IEnumerator Disable_DeactivatesGameObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.IsTrue(_testObject.activeSelf);

                await new Search().Name("TestObject").Disable();

                Assert.IsFalse(_testObject.activeSelf);
            });
        }

        [UnityTest]
        public IEnumerator Enable_ActivatesInactiveGameObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                _testObject.SetActive(false);
                Assert.IsFalse(_testObject.activeSelf);

                await new Search().Name("TestObject").Enable();

                Assert.IsTrue(_testObject.activeSelf);
            });
        }

        [UnityTest]
        public IEnumerator Disable_ThenEnable_RestoresState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.IsTrue(_testObject.activeSelf);

                await new Search().Name("TestObject").Disable();
                Assert.IsFalse(_testObject.activeSelf);

                await new Search().Name("TestObject").Enable();
                Assert.IsTrue(_testObject.activeSelf);
            });
        }

        #endregion

        #region Freeze Tests

        [UnityTest]
        public IEnumerator Freeze_SetsKinematicAndZerosVelocity()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var rb = _testObject.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                rb.linearVelocity = new Vector3(10, 0, 0);
                rb.angularVelocity = new Vector3(0, 5, 0);

                var state = await new Search().Name("TestObject").Freeze(includeChildren: false);

                Assert.AreEqual(1, state.Count);
                Assert.IsTrue(rb.isKinematic);
                Assert.AreEqual(Vector3.zero, rb.linearVelocity);
                Assert.AreEqual(Vector3.zero, rb.angularVelocity);
            });
        }

        [UnityTest]
        public IEnumerator Freeze_WithChildren_FreezesAllRigidbodies()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var parentRb = _testObject.GetComponent<Rigidbody>();
                var childRb = _childObject.GetComponent<Rigidbody>();
                parentRb.isKinematic = false;
                childRb.isKinematic = false;

                var state = await new Search().Name("TestObject").Freeze(includeChildren: true);

                Assert.AreEqual(2, state.Count);
                Assert.IsTrue(parentRb.isKinematic);
                Assert.IsTrue(childRb.isKinematic);
            });
        }

        #endregion

        #region Teleport Tests

        [UnityTest]
        public IEnumerator Teleport_MovesToPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                _testObject.transform.position = Vector3.zero;
                var targetPos = new Vector3(100, 50, 200);

                await new Search().Name("TestObject").Teleport(targetPos);

                Assert.AreEqual(targetPos, _testObject.transform.position);
            });
        }

        [UnityTest]
        public IEnumerator Teleport_MovesChildrenToo()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                _testObject.transform.position = Vector3.zero;
                _childObject.transform.localPosition = new Vector3(1, 0, 0);
                var targetPos = new Vector3(100, 0, 0);

                await new Search().Name("TestObject").Teleport(targetPos);

                // Child should be at parent position + local offset
                Assert.AreEqual(new Vector3(101, 0, 0), _childObject.transform.position);
            });
        }

        #endregion

        #region NoClip/Clip Tests

        [UnityTest]
        public IEnumerator NoClip_DisablesAllColliders()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var parentCol = _testObject.GetComponent<BoxCollider>();
                var childCol = _childObject.GetComponent<SphereCollider>();
                Assert.IsTrue(parentCol.enabled);
                Assert.IsTrue(childCol.enabled);

                var state = await new Search().Name("TestObject").NoClip();

                Assert.AreEqual(2, state.Count);
                Assert.IsFalse(parentCol.enabled);
                Assert.IsFalse(childCol.enabled);
            });
        }

        [UnityTest]
        public IEnumerator NoClip_WithoutChildren_OnlyDisablesRoot()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var parentCol = _testObject.GetComponent<BoxCollider>();
                var childCol = _childObject.GetComponent<SphereCollider>();

                var state = await new Search().Name("TestObject").NoClip(includeChildren: false);

                Assert.AreEqual(1, state.Count);
                Assert.IsFalse(parentCol.enabled);
                Assert.IsTrue(childCol.enabled); // Child unchanged
            });
        }

        [UnityTest]
        public IEnumerator Clip_EnablesAllColliders()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var parentCol = _testObject.GetComponent<BoxCollider>();
                var childCol = _childObject.GetComponent<SphereCollider>();
                parentCol.enabled = false;
                childCol.enabled = false;

                var state = await new Search().Name("TestObject").Clip();

                Assert.AreEqual(2, state.Count);
                Assert.IsTrue(parentCol.enabled);
                Assert.IsTrue(childCol.enabled);
            });
        }

        [UnityTest]
        public IEnumerator NoClip_ThenClip_RestoresColliders()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var parentCol = _testObject.GetComponent<BoxCollider>();
                var childCol = _childObject.GetComponent<SphereCollider>();

                await new Search().Name("TestObject").NoClip();
                Assert.IsFalse(parentCol.enabled);
                Assert.IsFalse(childCol.enabled);

                await new Search().Name("TestObject").Clip();
                Assert.IsTrue(parentCol.enabled);
                Assert.IsTrue(childCol.enabled);
            });
        }

        #endregion

        #region Static Path Tests

        // Static test class for Static() tests
        public static class TestManager
        {
            public static GameObject Player;
        }

        [UnityTest]
        public IEnumerator Static_Disable_WorksOnGameObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                TestManager.Player = _testObject;
                Assert.IsTrue(_testObject.activeSelf);

                await Search.Static("GameObjectManipulationTests.TestManager.Player").Disable();

                Assert.IsFalse(_testObject.activeSelf);
            });
        }

        [UnityTest]
        public IEnumerator Static_Freeze_WorksOnGameObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                TestManager.Player = _testObject;
                var rb = _testObject.GetComponent<Rigidbody>();
                rb.isKinematic = false;

                await Search.Static("GameObjectManipulationTests.TestManager.Player").Freeze();

                Assert.IsTrue(rb.isKinematic);
            });
        }

        [UnityTest]
        public IEnumerator Static_NoClip_WorksOnGameObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                TestManager.Player = _testObject;
                var col = _testObject.GetComponent<BoxCollider>();
                Assert.IsTrue(col.enabled);

                await Search.Static("GameObjectManipulationTests.TestManager.Player").NoClip();

                Assert.IsFalse(col.enabled);
            });
        }

        #endregion

        #region Restoration Token Tests

        [UnityTest]
        public IEnumerator Disable_RestoreToken_RestoresState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.IsTrue(_testObject.activeSelf);

                var state = await new Search().Name("TestObject").Disable();
                Assert.IsFalse(_testObject.activeSelf);

                state.Restore();
                Assert.IsTrue(_testObject.activeSelf);
            });
        }

        [UnityTest]
        public IEnumerator Enable_RestoreToken_RestoresState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                _testObject.SetActive(false);

                var state = await new Search().Name("TestObject").Enable();
                Assert.IsTrue(_testObject.activeSelf);

                state.Restore();
                Assert.IsFalse(_testObject.activeSelf); // Back to original
            });
        }

        [UnityTest]
        public IEnumerator Freeze_RestoreToken_RestoresVelocityAndKinematic()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var rb = _testObject.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                var originalVelocity = new Vector3(10, 5, 0);
                rb.linearVelocity = originalVelocity;

                var state = await new Search().Name("TestObject").Freeze(includeChildren: false);
                Assert.IsTrue(rb.isKinematic);
                Assert.AreEqual(Vector3.zero, rb.linearVelocity);

                state.Restore();
                Assert.IsFalse(rb.isKinematic);
                Assert.AreEqual(originalVelocity, rb.linearVelocity);
            });
        }

        [UnityTest]
        public IEnumerator NoClip_RestoreToken_RestoresColliderState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var parentCol = _testObject.GetComponent<BoxCollider>();
                var childCol = _childObject.GetComponent<SphereCollider>();
                // Child already disabled before NoClip
                childCol.enabled = false;

                var state = await new Search().Name("TestObject").NoClip();
                Assert.IsFalse(parentCol.enabled);
                Assert.IsFalse(childCol.enabled);

                state.Restore();
                Assert.IsTrue(parentCol.enabled);   // Was enabled, restored to enabled
                Assert.IsFalse(childCol.enabled);   // Was disabled, stays disabled
            });
        }

        [UnityTest]
        public IEnumerator Teleport_RestoreToken_RestoresPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var originalPos = new Vector3(5, 10, 15);
                _testObject.transform.position = originalPos;

                var state = await new Search().Name("TestObject").Teleport(new Vector3(100, 0, 0));
                Assert.AreEqual(new Vector3(100, 0, 0), _testObject.transform.position);

                state.Restore();
                Assert.AreEqual(originalPos, _testObject.transform.position);
            });
        }

        [UnityTest]
        public IEnumerator UsingPattern_AutoRestores()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var col = _testObject.GetComponent<BoxCollider>();
                Assert.IsTrue(col.enabled);

                using (await new Search().Name("TestObject").NoClip())
                {
                    Assert.IsFalse(col.enabled);
                }

                // Auto-restored after using block
                Assert.IsTrue(col.enabled);
            });
        }

        [UnityTest]
        public IEnumerator RestoreToken_OnlyRestoresOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var state = await new Search().Name("TestObject").Disable();
                Assert.IsFalse(_testObject.activeSelf);

                state.Restore();
                Assert.IsTrue(_testObject.activeSelf);

                // Disable again manually
                _testObject.SetActive(false);

                // Second restore should be no-op
                state.Restore();
                Assert.IsFalse(_testObject.activeSelf); // Still disabled
            });
        }

        #endregion

        #region Error Handling Tests

        [UnityTest]
        public IEnumerator Disable_ThrowsOnNotFound()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                try
                {
                    await new Search().Name("NonExistentObject").Disable(searchTime: 0.1f);
                    Assert.Fail("Expected TimeoutException");
                }
                catch (System.TimeoutException)
                {
                    // Expected
                }
            });
        }

        [UnityTest]
        public IEnumerator Freeze_ReturnsZeroWhenNoRigidbodies()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Remove rigidbodies
                Object.Destroy(_testObject.GetComponent<Rigidbody>());
                Object.Destroy(_childObject.GetComponent<Rigidbody>());
                await UniTask.Yield(); // Wait for destroy

                var state = await new Search().Name("TestObject").Freeze();

                Assert.AreEqual(0, state.Count);
            });
        }

        [UnityTest]
        public IEnumerator NoClip_ReturnsZeroWhenNoColliders()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Remove colliders
                Object.Destroy(_testObject.GetComponent<BoxCollider>());
                Object.Destroy(_childObject.GetComponent<SphereCollider>());
                await UniTask.Yield(); // Wait for destroy

                var state = await new Search().Name("TestObject").NoClip();

                Assert.AreEqual(0, state.Count);
            });
        }

        #endregion
    }
}
