using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

using ODDGames.Bugpunch;


namespace ODDGames.Bugpunch.Tests
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

        [Test]
        public async Task Disable_DeactivatesGameObject()
        {
            await Async.DelayFrames(1);

            Assert.IsTrue(_testObject.activeSelf);

            await new Search().Name("TestObject").Disable();

            Assert.IsFalse(_testObject.activeSelf);
        }

        [Test]
        public async Task Enable_ActivatesInactiveGameObject()
        {
            await Async.DelayFrames(1);

            _testObject.SetActive(false);
            Assert.IsFalse(_testObject.activeSelf);

            await new Search().Name("TestObject").Enable();

            Assert.IsTrue(_testObject.activeSelf);
        }

        [Test]
        public async Task Disable_ThenEnable_RestoresState()
        {
            await Async.DelayFrames(1);

            Assert.IsTrue(_testObject.activeSelf);

            await new Search().Name("TestObject").Disable();
            Assert.IsFalse(_testObject.activeSelf);

            await new Search().Name("TestObject").Enable();
            Assert.IsTrue(_testObject.activeSelf);
        }

        #endregion

        #region Freeze Tests

        [Test]
        public async Task Freeze_SetsKinematicAndZerosVelocity()
        {
            await Async.DelayFrames(1);

            var rb = _testObject.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.linearVelocity = new Vector3(10, 0, 0);
            rb.angularVelocity = new Vector3(0, 5, 0);

            var state = await new Search().Name("TestObject").Freeze(includeChildren: false);

            Assert.AreEqual(1, state.Count);
            Assert.IsTrue(rb.isKinematic);
            Assert.AreEqual(Vector3.zero, rb.linearVelocity);
            Assert.AreEqual(Vector3.zero, rb.angularVelocity);
        }

        [Test]
        public async Task Freeze_WithChildren_FreezesAllRigidbodies()
        {
            await Async.DelayFrames(1);

            var parentRb = _testObject.GetComponent<Rigidbody>();
            var childRb = _childObject.GetComponent<Rigidbody>();
            parentRb.isKinematic = false;
            childRb.isKinematic = false;

            var state = await new Search().Name("TestObject").Freeze(includeChildren: true);

            Assert.AreEqual(2, state.Count);
            Assert.IsTrue(parentRb.isKinematic);
            Assert.IsTrue(childRb.isKinematic);
        }

        #endregion

        #region Teleport Tests

        [Test]
        public async Task Teleport_MovesToPosition()
        {
            await Async.DelayFrames(1);

            _testObject.transform.position = Vector3.zero;
            var targetPos = new Vector3(100, 50, 200);

            await new Search().Name("TestObject").Teleport(targetPos);

            Assert.AreEqual(targetPos, _testObject.transform.position);
        }

        [Test]
        public async Task Teleport_MovesChildrenToo()
        {
            await Async.DelayFrames(1);

            _testObject.transform.position = Vector3.zero;
            _childObject.transform.localPosition = new Vector3(1, 0, 0);
            var targetPos = new Vector3(100, 0, 0);

            await new Search().Name("TestObject").Teleport(targetPos);

            // Child should be at parent position + local offset
            Assert.AreEqual(new Vector3(101, 0, 0), _childObject.transform.position);
        }

        #endregion

        #region NoClip/Clip Tests

        [Test]
        public async Task NoClip_DisablesAllColliders()
        {
            await Async.DelayFrames(1);

            var parentCol = _testObject.GetComponent<BoxCollider>();
            var childCol = _childObject.GetComponent<SphereCollider>();
            Assert.IsTrue(parentCol.enabled);
            Assert.IsTrue(childCol.enabled);

            var state = await new Search().Name("TestObject").NoClip();

            Assert.AreEqual(2, state.Count);
            Assert.IsFalse(parentCol.enabled);
            Assert.IsFalse(childCol.enabled);
        }

        [Test]
        public async Task NoClip_WithoutChildren_OnlyDisablesRoot()
        {
            await Async.DelayFrames(1);

            var parentCol = _testObject.GetComponent<BoxCollider>();
            var childCol = _childObject.GetComponent<SphereCollider>();

            var state = await new Search().Name("TestObject").NoClip(includeChildren: false);

            Assert.AreEqual(1, state.Count);
            Assert.IsFalse(parentCol.enabled);
            Assert.IsTrue(childCol.enabled); // Child unchanged
        }

        [Test]
        public async Task Clip_EnablesAllColliders()
        {
            await Async.DelayFrames(1);

            var parentCol = _testObject.GetComponent<BoxCollider>();
            var childCol = _childObject.GetComponent<SphereCollider>();
            parentCol.enabled = false;
            childCol.enabled = false;

            var state = await new Search().Name("TestObject").Clip();

            Assert.AreEqual(2, state.Count);
            Assert.IsTrue(parentCol.enabled);
            Assert.IsTrue(childCol.enabled);
        }

        [Test]
        public async Task NoClip_ThenClip_RestoresColliders()
        {
            await Async.DelayFrames(1);

            var parentCol = _testObject.GetComponent<BoxCollider>();
            var childCol = _childObject.GetComponent<SphereCollider>();

            await new Search().Name("TestObject").NoClip();
            Assert.IsFalse(parentCol.enabled);
            Assert.IsFalse(childCol.enabled);

            await new Search().Name("TestObject").Clip();
            Assert.IsTrue(parentCol.enabled);
            Assert.IsTrue(childCol.enabled);
        }

        #endregion

        #region Static Path Tests

        // Static test class for Static() tests
        public static class TestManager
        {
            public static GameObject Player;
        }

        [Test]
        public async Task Static_Disable_WorksOnGameObject()
        {
            await Async.DelayFrames(1);

            TestManager.Player = _testObject;
            Assert.IsTrue(_testObject.activeSelf);

            await Search.Reflect("GameObjectManipulationTests.TestManager.Player").Disable();

            Assert.IsFalse(_testObject.activeSelf);
        }

        [Test]
        public async Task Static_Freeze_WorksOnGameObject()
        {
            await Async.DelayFrames(1);

            TestManager.Player = _testObject;
            var rb = _testObject.GetComponent<Rigidbody>();
            rb.isKinematic = false;

            await Search.Reflect("GameObjectManipulationTests.TestManager.Player").Freeze();

            Assert.IsTrue(rb.isKinematic);
        }

        [Test]
        public async Task Static_NoClip_WorksOnGameObject()
        {
            await Async.DelayFrames(1);

            TestManager.Player = _testObject;
            var col = _testObject.GetComponent<BoxCollider>();
            Assert.IsTrue(col.enabled);

            await Search.Reflect("GameObjectManipulationTests.TestManager.Player").NoClip();

            Assert.IsFalse(col.enabled);
        }

        #endregion

        #region Restoration Token Tests

        [Test]
        public async Task Disable_RestoreToken_RestoresState()
        {
            await Async.DelayFrames(1);

            Assert.IsTrue(_testObject.activeSelf);

            var state = await new Search().Name("TestObject").Disable();
            Assert.IsFalse(_testObject.activeSelf);

            state.Restore();
            Assert.IsTrue(_testObject.activeSelf);
        }

        [Test]
        public async Task Enable_RestoreToken_RestoresState()
        {
            await Async.DelayFrames(1);

            _testObject.SetActive(false);

            var state = await new Search().Name("TestObject").Enable();
            Assert.IsTrue(_testObject.activeSelf);

            state.Restore();
            Assert.IsFalse(_testObject.activeSelf); // Back to original
        }

        [Test]
        public async Task Freeze_RestoreToken_RestoresVelocityAndKinematic()
        {
            await Async.DelayFrames(1);

            var rb = _testObject.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            var originalVelocity = new Vector3(10, 5, 0);
            rb.linearVelocity = originalVelocity;

            var state = await new Search().Name("TestObject").Freeze(includeChildren: false);
            Assert.IsTrue(rb.isKinematic);
            Assert.AreEqual(Vector3.zero, rb.linearVelocity);

            state.Restore();
            Assert.IsFalse(rb.isKinematic);
            // Use tolerance for floating point comparison
            Assert.AreEqual(originalVelocity.x, rb.linearVelocity.x, 0.01f, "Velocity X mismatch");
            Assert.AreEqual(originalVelocity.y, rb.linearVelocity.y, 0.01f, "Velocity Y mismatch");
            Assert.AreEqual(originalVelocity.z, rb.linearVelocity.z, 0.01f, "Velocity Z mismatch");
        }

        [Test]
        public async Task NoClip_RestoreToken_RestoresColliderState()
        {
            await Async.DelayFrames(1);

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
        }

        [Test]
        public async Task Teleport_RestoreToken_RestoresPosition()
        {
            await Async.DelayFrames(1);

            var originalPos = new Vector3(5, 10, 15);
            _testObject.transform.position = originalPos;

            var state = await new Search().Name("TestObject").Teleport(new Vector3(100, 0, 0));
            Assert.AreEqual(new Vector3(100, 0, 0), _testObject.transform.position);

            state.Restore();
            // Use tolerance for floating point comparison (physics may cause minor drift)
            Assert.AreEqual(originalPos.x, _testObject.transform.position.x, 0.25f, "Position X mismatch");
            Assert.AreEqual(originalPos.y, _testObject.transform.position.y, 0.25f, "Position Y mismatch");
            Assert.AreEqual(originalPos.z, _testObject.transform.position.z, 0.25f, "Position Z mismatch");
        }

        [Test]
        public async Task UsingPattern_AutoRestores()
        {
            await Async.DelayFrames(1);

            var col = _testObject.GetComponent<BoxCollider>();
            Assert.IsTrue(col.enabled);

            using (await new Search().Name("TestObject").NoClip())
            {
                Assert.IsFalse(col.enabled);
            }

            // Auto-restored after using block
            Assert.IsTrue(col.enabled);
        }

        [Test]
        public async Task RestoreToken_OnlyRestoresOnce()
        {
            await Async.DelayFrames(1);

            var state = await new Search().Name("TestObject").Disable();
            Assert.IsFalse(_testObject.activeSelf);

            state.Restore();
            Assert.IsTrue(_testObject.activeSelf);

            // Disable again manually
            _testObject.SetActive(false);

            // Second restore should be no-op
            state.Restore();
            Assert.IsFalse(_testObject.activeSelf); // Still disabled
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public async Task Disable_ThrowsOnNotFound()
        {
            await Async.DelayFrames(1);

            try
            {
                await new Search().Name("NonExistentObject").Disable(searchTime: 0.1f);
                Assert.Fail("Expected TimeoutException");
            }
            catch (System.TimeoutException)
            {
                // Expected
            }
        }

        [Test]
        public async Task Freeze_ReturnsZeroWhenNoRigidbodies()
        {
            await Async.DelayFrames(1);

            // Remove rigidbodies
            Object.Destroy(_testObject.GetComponent<Rigidbody>());
            Object.Destroy(_childObject.GetComponent<Rigidbody>());
            await Async.DelayFrames(1); // Wait for destroy

            var state = await new Search().Name("TestObject").Freeze();

            Assert.AreEqual(0, state.Count);
        }

        [Test]
        public async Task NoClip_ReturnsZeroWhenNoColliders()
        {
            await Async.DelayFrames(1);

            // Remove colliders
            Object.Destroy(_testObject.GetComponent<BoxCollider>());
            Object.Destroy(_childObject.GetComponent<SphereCollider>());
            await Async.DelayFrames(1); // Wait for destroy

            var state = await new Search().Name("TestObject").NoClip();

            Assert.AreEqual(0, state.Count);
        }

        #endregion
    }
}
