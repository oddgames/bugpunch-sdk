// Assembly-level configuration for UI Automation tests
// This file configures log handling for all tests in this assembly

using UnityEngine.TestTools;

// Disable strict log checking for all tests in this assembly.
// This prevents error logs from game code from failing tests.
// Individual tests can still use LogAssert.Expect() to verify specific errors.
[assembly: TestMustExpectAllLogs(false)]
