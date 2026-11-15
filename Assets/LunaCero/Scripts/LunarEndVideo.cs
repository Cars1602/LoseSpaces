using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace LunaCero
{
    /// <summary>
    /// Videos de cierre opcionales. Basta con copiar el archivo a Assets/StreamingAssets/Videos/
    /// (por ejemplo final.mp4); si no existe, el juego usa su animacion de siempre.
    /// Espacio, Enter o Esc saltan el video.
    /// </summary>
    public static class LunarEndVideo
    {
        private static readonly string[] Extensions = { ".mp4", ".webm", ".mov" };

        public static string Find(string name)
        {
            foreach (var extension in Extensions)
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Videos", name + extension);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Reproduce el video a pantalla completa. 'done' recibe true si se llego a reproducir.</summary>
        public static IEnumerator Play(string name, Action<bool> done)
        {
            string path = Find(name);
            if (path == null) { done?.Invoke(false); yield break; }
            var camera = Camera.main;
            var go = new GameObject("Video " + name);
            var player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.url = "file://" + path.Replace('\\', '/');
            player.renderMode = camera != null ? VideoRenderMode.CameraNearPlane : VideoRenderMode.APIOnly;
            player.targetCamera = camera;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.isLooping = false;
            bool failed = false;
            player.errorReceived += (_, message) => { failed = true; Debug.LogWarning("Video " + name + ": " + message); };
            player.Prepare();
            float timeout = Time.realtimeSinceStartup + 6f;
            while (!player.isPrepared && !failed && Time.realtimeSinceStartup < timeout) yield return null;
            if (!player.isPrepared || failed) { UnityEngine.Object.Destroy(go); done?.Invoke(false); yield break; }
            // Fondo negro detras del video (bandas si no coincide la proporcion).
            CameraClearFlags clear = camera != null ? camera.clearFlags : CameraClearFlags.SolidColor;
            int mask = camera != null ? camera.cullingMask : 0;
            Color background = camera != null ? camera.backgroundColor : Color.black;
            if (camera != null) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 0; }
            player.Play();
            float started = Time.realtimeSinceStartup;
            while (!failed)
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape)) break;
                if (Time.realtimeSinceStartup - started > 0.5f && !player.isPlaying) break;
                yield return null;
            }
            if (camera != null) { camera.clearFlags = clear; camera.backgroundColor = background; camera.cullingMask = mask; }
            UnityEngine.Object.Destroy(go);
            done?.Invoke(true);
        }
    }
}
