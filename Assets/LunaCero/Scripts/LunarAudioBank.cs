using System;
using UnityEngine;
namespace LunaCero
{
    public sealed class LunarAudioBank : ScriptableObject
    {
        [Serializable] public struct Entry { public string key; public AudioClip clip; }
        public Entry[] entries;
        public AudioClip Find(string key) { foreach(var e in entries) if(e.key == key) return e.clip; return null; }
    }
}
