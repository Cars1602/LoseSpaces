using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    [DefaultExecutionOrder(-100)]
    public sealed class LunarCombatArena : MonoBehaviour
    {
        private NavMeshData navigation;
        private NavMeshDataInstance navigationInstance;
        private LunarEnemy[] enemies;
        public bool NavigationReady => navigationInstance.valid;

        private void Awake()
        {
            var sources = new List<NavMeshBuildSource>();
            // Only the static level participates: actors and loose items never become baked obstacles.
            foreach (var box in FindObjectsOfType<BoxCollider>())
            {
                if (!box.enabled || box.isTrigger || box.attachedRigidbody != null || box.GetComponentInParent<LunarPickup>() != null) continue;
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center),
                    size = box.size,
                    area = 0
                });
            }
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = 0.5f;
            settings.agentHeight = 2f;
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(90, 30, 90)), Vector3.zero, Quaternion.identity);
            if (navigation != null) navigationInstance = NavMesh.AddNavMeshData(navigation);
            enemies = FindObjectsOfType<LunarEnemy>();
        }

        private void OnDestroy()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation != null) Destroy(navigation);
        }

        private void OnGUI()
        {
            if (enemies == null) return;
            int bosses = 0;
            int weak = 0;
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                if (enemy.isBoss) bosses++; else weak++;
            }
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.Box(new Rect(Screen.width / scale - 310, 15, 290, 75), $"OBJETIVO: ELIMINAR A LOS ENEMIGOS\nJefes restantes: {bosses}/4\nBots debiles restantes: {weak}");
            if (bosses == 0 && weak == 0)
                GUI.Box(new Rect(Screen.width / scale / 2 - 180, 170, 360, 60), "VICTORIA\nLos cuatro jefes y sus bots fueron eliminados.");
            GUI.matrix = old;
        }
    }
}
