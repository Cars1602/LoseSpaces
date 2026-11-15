using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    /// <summary>
    /// Navegacion de un escenario fijo a partir de sus colisionadores estaticos (terreno, rocas, nave, base).
    /// Los objetos con Rigidbody (piezas, cajas, jugador) se ignoran. Si 'baked' tiene datos (el generador
    /// del mapa grande los hornea en el editor) se cargan al instante; si no, se construye al empezar.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public sealed class LunarLevelNavigation : MonoBehaviour
    {
        public static bool Ready { get; private set; }
        public Bounds bounds = new Bounds(Vector3.zero, new Vector3(100, 60, 100));
        public float agentRadius = 0.5f;
        public float agentHeight = 2f;
        [Tooltip("Navegacion horneada en el editor (mapas grandes: evita varios segundos de carga)")]
        public NavMeshData baked;
        private NavMeshDataInstance instance;
        private NavMeshData data;

        private void Awake()
        {
            Ready = false;
            if (baked != null) instance = NavMesh.AddNavMeshData(baked);
            else
            {
                data = Build(bounds, agentRadius, agentHeight, 0f);
                if (data != null) instance = NavMesh.AddNavMeshData(data);
            }
            Ready = instance.valid;
            if (!Ready) Debug.LogError("No se pudo construir la navegacion del escenario.", this);
        }

        /// <summary>Construye la malla de navegacion (tambien desde el editor). voxelSize 0 = automatico.</summary>
        public static NavMeshData Build(Bounds bounds, float agentRadius, float agentHeight, float voxelSize)
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            sources.RemoveAll(source =>
            {
                var collider = source.component as Collider;
                return collider != null && (collider.isTrigger || collider.attachedRigidbody != null);
            });
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentSlope = 45f;
            settings.agentClimb = 0.65f;
            if (voxelSize > 0f)
            {
                settings.overrideVoxelSize = true;
                settings.voxelSize = voxelSize;
            }
            return NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        }

        private void OnDestroy()
        {
            Ready = false;
            if (instance.valid) instance.Remove();
            if (data != null) Destroy(data);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
