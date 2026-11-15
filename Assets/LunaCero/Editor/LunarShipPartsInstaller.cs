using System;
using UnityEditor;
using UnityEngine;

namespace LunaCero.Editor
{
    /// <summary>
    /// Convierte los modelos de las carpetas "Motor de propusion", "Modulo de comunicaciones" y
    /// "Nucleo de energia" en prefabs (Resources/LunarWeapons/part-*) con su textura, para usarlos
    /// como piezas de la nave repartidas por el mapa. Se ejecuta una vez al abrir Unity.
    /// </summary>
    [InitializeOnLoad]
    public static class LunarShipPartsInstaller
    {
        private const string Key = "ShipParts20.v1";
        static LunarShipPartsInstaller() { EditorApplication.delayCall += Once; }

        private static void Once()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling) { EditorApplication.delayCall += Once; return; }
            if (EditorPrefs.GetBool(Application.dataPath + Key, false)) return;
            try { Install(); EditorPrefs.SetBool(Application.dataPath + Key, true); }
            catch (Exception e) { Debug.LogError("LUNA_SHIP_PARTS_FAILED: " + e); }
        }

        [MenuItem("Luna Cero/Instalar modelos de piezas de la nave")]
        public static void Install()
        {
            LunarArsenalModelInstaller.Build("Motor de propulsion", "part-motor", 1.1f, "Assets/Motor de propusion");
            LunarArsenalModelInstaller.Build("Modulo de comunicaciones", "part-comunicaciones", 1.0f, "Assets/Módulo de comunicaciones");
            LunarArsenalModelInstaller.Build("Nucleo de energia", "part-nucleo", 0.9f, "Assets/Núcleo de energía");
            AssetDatabase.SaveAssets();
            Debug.Log("LUNA_SHIP_PARTS_OK: motor de propulsion, modulo de comunicaciones y nucleo de energia listos como piezas.");
        }
    }
}
