#if UNITY_INCLUDE_TESTS
using System.Runtime.CompilerServices;

// Allow tests to access internals
[assembly: InternalsVisibleTo("Assembly-CSharp")]
[assembly: InternalsVisibleTo("ODDGames.Bugpunch.Tests.PlayMode")]

// Allow sub-assemblies to access internals
[assembly: InternalsVisibleTo("ODDGames.Bugpunch.Editor")]
#endif
