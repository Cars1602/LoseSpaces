using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Indica si corre una prueba automatica (sin menus). Las pruebas solo existen en el editor
    /// (#if UNITY_EDITOR), asi que en el juego exportado no se las nombra.
    /// </summary>
    public static class LunarAutomation
    {
        public static bool Running
        {
            get
            {
                if (Application.isBatchMode) return true;
#if UNITY_EDITOR
                return Object.FindObjectOfType<LunarCombatSmokeTest>() != null || Object.FindObjectOfType<LunarSurvivalSmokeTest>() != null;
#else
                return false;
#endif
            }
        }
    }
}
