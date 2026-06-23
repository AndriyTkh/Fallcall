using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace OsuUnity.Gameplay
{
    /// <summary>Loads audio and background images from a beatmap folder at runtime.</summary>
    public static class AssetLoader
    {
        public static IEnumerator LoadAudio(string path, Action<AudioClip> onDone)
        {
            if (!File.Exists(path)) { onDone(null); yield break; }

            AudioType type = AudioTypeFromExtension(path);
            string url = PathToUrl(path);

            using var req = UnityWebRequestMultimedia.GetAudioClip(url, type);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AssetLoader] Audio load failed ({path}): {req.error}");
                onDone(null);
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            onDone(clip);
        }

        public static IEnumerator LoadTexture(string path, Action<Texture2D> onDone)
        {
            if (!File.Exists(path)) { onDone(null); yield break; }

            using var req = UnityWebRequestTexture.GetTexture(PathToUrl(path));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AssetLoader] Texture load failed ({path}): {req.error}");
                onDone(null);
                yield break;
            }

            onDone(DownloadHandlerTexture.GetContent(req));
        }

        private static AudioType AudioTypeFromExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".mp3":
                default: return AudioType.MPEG;
            }
        }

        private static string PathToUrl(string path)
        {
            // UnityWebRequest needs a file:// URI; Uri handles spaces / special chars.
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }
    }
}
