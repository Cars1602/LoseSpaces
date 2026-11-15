#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarStationLimitTest
    {
        static LunarStationLimitTest() { EditorApplication.delayCall += RunOnce; }
        private static void RunOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool("LunaStationLimitTest.v2", false)) return;
            SessionState.SetBool("LunaStationLimitTest.v2", true);
            Run();
        }
        [MenuItem("Luna Cero/Probar limite de estaciones")]
        public static void Run()
        {
            var root = new GameObject("Station limit test");
            root.SetActive(false);
            try
            {
                var world = root.AddComponent<LunarEndlessWorld>();
                var child = new GameObject("Station");
                child.transform.SetParent(root.transform);
                var station = child.AddComponent<LunarHealingStation>();
                station.stationKey = "test";
                Check(station.RemainingUses == 3 && station.Ready, "Initial supply");
                for (int i = 0; i < 3; i++)
                {
                    world.StationSupply("test").ReadyAt = 0;
                    Check(station.Consume(), "Consume");
                    Check(!station.Consume() && station.RemainingUses == 2 - i, "Cooldown prevents spending twice");
                }
                world.StationSupply("test").ReadyAt = 0;
                Check(station.Exhausted && !station.Ready && !station.Consume(), "Exhaustion");
                UnityEngine.Object.DestroyImmediate(child);
                child = new GameObject("Recreated station");
                child.transform.SetParent(root.transform);
                station = child.AddComponent<LunarHealingStation>();
                station.stationKey = "test";
                Check(station.Exhausted && !station.Consume(), "Streaming must preserve exhaustion");
                station.stationKey = "other";
                Check(station.RemainingUses == 3 && station.Ready, "Independent stations");
                Debug.Log("LUNA_STATION_LIMIT_OK: 3 uses, cooldown, exhaustion, persistence after recreation, independent stations.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    }
}
#endif

