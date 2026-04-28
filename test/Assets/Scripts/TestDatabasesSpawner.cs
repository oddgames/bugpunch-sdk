using System;
using System.Collections.Generic;
using System.IO;
using ODDGames.Bugpunch;
using UnityEngine;
using UnityEngine.SceneManagement;

#if !ODD_NO_ODIN
using Sirenix.Serialization;
#endif

#if !ODD_NO_SIAQODB
using Sqo;
#endif

#if !ODD_NO_SQLITE
using SQLite4Unity3d;
#endif

namespace BugpunchTestProject
{
    /// <summary>
    /// Spawns and continuously mutates a JSON, Odin and Siaqodb database on disk
    /// so the Bugpunch dashboard's database viewer has something live to read.
    /// Files land under <c>{persistentDataPath}/TestDBs/</c>.
    /// </summary>
    public class TestDatabasesSpawner : MonoBehaviour
    {
        static readonly string[] TargetSceneNames = { "CityTest", "Test", "Sample", "SampleScene" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var scene = SceneManager.GetActiveScene();
            bool match = false;
            for (int i = 0; i < TargetSceneNames.Length; i++)
                if (scene.name == TargetSceneNames[i]) { match = true; break; }
            if (!match) return;

            if (GameObject.Find("[Bugpunch Test Databases]") != null) return;
            var go = new GameObject("[Bugpunch Test Databases]");
            DontDestroyOnLoad(go);
            go.AddComponent<TestDatabasesSpawner>();
        }

        public float updateInterval = 4f;
        public int maxRows = 50;

        string _root;
        int _tick;

#if !ODD_NO_SIAQODB
        Siaqodb _sqo;
#endif

#if !ODD_NO_SQLITE
        SQLiteConnection _sqlite;
#endif

        readonly List<Player> _jsonPlayers = new();
        readonly List<Match> _jsonMatches = new();
#if !ODD_NO_ODIN
        readonly List<Player> _odinPlayers = new();
        readonly Dictionary<string, int> _odinCounters = new();
#endif

        void Start()
        {
            _root = Path.Combine(Application.persistentDataPath, "TestDBs");
            Directory.CreateDirectory(_root);

            // Allow-list these test databases so the dashboard's "Request More
            // Info" directives can pull them on a crash. Use the path token so
            // native gets the resolved location at config-bundle time.
            const string token = "[PersistentDataPath]/TestDBs";
            const long oneMB = 1024 * 1024;
            Bugpunch.AddAttachmentRule("test-json", token, "*.json", oneMB);
#if !ODD_NO_ODIN
            Bugpunch.AddAttachmentRule("test-odin", token, "*.odin", oneMB);
#endif
#if !ODD_NO_SQLITE
            Bugpunch.AddAttachmentRule("test-sqlite", token, "*.sqlite*", 4 * oneMB);
#endif
#if !ODD_NO_SIAQODB
            Bugpunch.AddAttachmentRule("test-siaqodb", token + "/siaqodb", "*", 4 * oneMB);
#endif

#if !ODD_NO_SIAQODB
            try
            {
                var sqoDir = Path.Combine(_root, "siaqodb");
                Directory.CreateDirectory(sqoDir);
                // License copied from MTD (same ODD Games org).
                SiaqodbConfigurator.SetLicense(@"EkbdozyL56jhJXC1HFYKLv/sTVYVSaTyHLBQRqdQCE0owLUMNkwXhkgYU74woTRO60yWsyhJ0shyRyiWCEaYQA==");
                _sqo = new Siaqodb(sqoDir);
            }
            catch (Exception ex) { Debug.LogWarning("[TestDBs] Siaqodb init failed: " + ex.Message); }
#endif

#if !ODD_NO_SQLITE
            try
            {
                var dbPath = Path.Combine(_root, "game.sqlite");
                _sqlite = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
                _sqlite.CreateTable<SqlitePlayer>();
                _sqlite.CreateTable<SqliteMatch>();
            }
            catch (Exception ex) { Debug.LogWarning("[TestDBs] SQLite init failed: " + ex.Message); }
#endif

            Debug.Log("[TestDBs] root=" + _root);
            InvokeRepeating(nameof(Tick), 0.5f, updateInterval);
        }

        void OnDestroy()
        {
#if !ODD_NO_SIAQODB
            try { _sqo?.Close(); } catch { }
#endif
#if !ODD_NO_SQLITE
            try { _sqlite?.Close(); _sqlite?.Dispose(); } catch { }
#endif
        }

        void Tick()
        {
            _tick++;
            var now = DateTime.UtcNow;
            var newPlayer = new Player
            {
                id = _tick,
                name = "Player_" + _tick,
                level = UnityEngine.Random.Range(1, 60),
                score = UnityEngine.Random.Range(0, 10000),
                online = (_tick % 3) != 0,
                lastSeenUnix = ((DateTimeOffset)now).ToUnixTimeSeconds(),
            };

            // --- JSON ---
            _jsonPlayers.Add(newPlayer);
            while (_jsonPlayers.Count > maxRows) _jsonPlayers.RemoveAt(0);
            // Mutate every existing player's score so the viewer shows real change.
            for (int i = 0; i < _jsonPlayers.Count; i++)
                _jsonPlayers[i].score += UnityEngine.Random.Range(-50, 100);

            _jsonMatches.Add(new Match
            {
                id = _tick,
                playerA = newPlayer.name,
                playerB = "Bot_" + UnityEngine.Random.Range(1, 999),
                winner = UnityEngine.Random.value > 0.5f ? "A" : "B",
                durationSec = UnityEngine.Random.Range(30f, 600f),
            });
            while (_jsonMatches.Count > maxRows) _jsonMatches.RemoveAt(0);

            try
            {
                var json = JsonUtility.ToJson(new JsonRoot { players = _jsonPlayers, matches = _jsonMatches }, true);
                File.WriteAllText(Path.Combine(_root, "game.json"), json);
            }
            catch (Exception ex) { Debug.LogWarning("[TestDBs] JSON write failed: " + ex.Message); }

#if !ODD_NO_ODIN
            try
            {
                _odinPlayers.Add(newPlayer);
                while (_odinPlayers.Count > maxRows) _odinPlayers.RemoveAt(0);

                var key = "scene:" + SceneManager.GetActiveScene().name;
                _odinCounters[key] = (_odinCounters.TryGetValue(key, out var c) ? c : 0) + 1;
                _odinCounters["totalTicks"] = _tick;

                var playersBytes = SerializationUtility.SerializeValue(_odinPlayers, DataFormat.Binary);
                File.WriteAllBytes(Path.Combine(_root, "players.odin"), playersBytes);

                var countersBytes = SerializationUtility.SerializeValue(_odinCounters, DataFormat.JSON);
                File.WriteAllBytes(Path.Combine(_root, "counters.odin"), countersBytes);
            }
            catch (Exception ex) { Debug.LogWarning("[TestDBs] Odin write failed: " + ex.Message); }
#endif

#if !ODD_NO_SIAQODB
            try
            {
                if (_sqo != null)
                {
                    _sqo.StoreObject(newPlayer);
                    _sqo.StoreObject(new SystemEvent { id = _tick, evt = "tick", t = Time.realtimeSinceStartup });
                    _sqo.Flush();
                }
            }
            catch (Exception ex) { Debug.LogWarning("[TestDBs] Siaqodb write failed: " + ex.Message); }
#endif

#if !ODD_NO_SQLITE
            try
            {
                if (_sqlite != null)
                {
                    _sqlite.Insert(new SqlitePlayer
                    {
                        Id = _tick,
                        Name = newPlayer.name,
                        Level = newPlayer.level,
                        Score = newPlayer.score,
                        Online = newPlayer.online ? 1 : 0,
                        LastSeenUnix = newPlayer.lastSeenUnix,
                    });
                    // Bump every existing player's score so the viewer sees mutation.
                    _sqlite.Execute("UPDATE SqlitePlayer SET Score = Score + ? WHERE Id < ?",
                        UnityEngine.Random.Range(-25, 75), _tick);
                    _sqlite.Insert(new SqliteMatch
                    {
                        Id = _tick,
                        PlayerA = newPlayer.name,
                        PlayerB = "Bot_" + UnityEngine.Random.Range(1, 999),
                        Winner = UnityEngine.Random.value > 0.5f ? "A" : "B",
                        DurationSec = UnityEngine.Random.Range(30f, 600f),
                    });
                    // Trim oldest rows to keep the file bounded.
                    _sqlite.Execute("DELETE FROM SqlitePlayer WHERE Id <= ?", _tick - maxRows);
                    _sqlite.Execute("DELETE FROM SqliteMatch WHERE Id <= ?", _tick - maxRows);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[TestDBs] SQLite write failed: " + ex.Message); }
#endif

            if (_tick % 5 == 1)
                Debug.Log($"[TestDBs] tick {_tick} → {_jsonPlayers.Count} players, {_jsonMatches.Count} matches");
        }

        // -------------------------------------------------------------------
        // Records
        // -------------------------------------------------------------------

        [Serializable]
        public class Player
        {
            public int id;
            public string name;
            public int level;
            public int score;
            public bool online;
            public long lastSeenUnix;
        }

        [Serializable]
        public class Match
        {
            public int id;
            public string playerA;
            public string playerB;
            public string winner;
            public float durationSec;
        }

        [Serializable]
        public class SystemEvent
        {
            public int id;
            public string evt;
            public float t;
        }

#if !ODD_NO_SQLITE
        public class SqlitePlayer
        {
            [PrimaryKey] public int Id { get; set; }
            public string Name { get; set; }
            public int Level { get; set; }
            public int Score { get; set; }
            public int Online { get; set; }
            public long LastSeenUnix { get; set; }
        }

        public class SqliteMatch
        {
            [PrimaryKey] public int Id { get; set; }
            public string PlayerA { get; set; }
            public string PlayerB { get; set; }
            public string Winner { get; set; }
            public float DurationSec { get; set; }
        }
#endif

        [Serializable]
        class JsonRoot
        {
            public List<Player> players;
            public List<Match> matches;
        }
    }
}
