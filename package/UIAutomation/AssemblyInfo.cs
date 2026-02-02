using System.Runtime.CompilerServices;

#if UNITY_INCLUDE_TESTS
using UnityEngine.TestTools;
#endif

// Allow tests to access internals
[assembly: InternalsVisibleTo("Assembly-CSharp")]
[assembly: InternalsVisibleTo("ODDGames.UIAutomation.Tests.PlayMode")]

// Allow sub-assemblies to access internals
[assembly: InternalsVisibleTo("ODDGames.UIAutomation.VisualBuilder")]

// Disable strict log checking - error logs from game code should not fail tests.
// NOTE: This only applies to tests in THIS assembly. For tests in other assemblies
// that inherit from UITestBase, add this attribute to your own AssemblyInfo.cs:
//   [assembly: TestMustExpectAllLogs(false)]
#if UNITY_INCLUDE_TESTS
[assembly: TestMustExpectAllLogs(false)]
#endif
