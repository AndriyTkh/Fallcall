using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace OsuUnity.Gameplay
{
    /// <summary>Downloads .osz archives from public osu! mirrors (no auth/API key required).</summary>
    public static class BeatmapDownloader
    {
        public static string[] MirrorUrls(int setId) => new[]
        {
            $"https://api.nerinyan.moe/d/{setId}",
            $"https://catboy.best/d/{setId}",
        };

        public static IEnumerator Download(string url, string destPath, Action<float> onProgress, Action<bool> onDone)
        {
            using var req = UnityWebRequest.Get(url);
            req.downloadHandler = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };
            var op = req.SendWebRequest();

            while (!op.isDone)
            {
                onProgress?.Invoke(req.downloadProgress);
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BeatmapDownloader] Download failed ({url}): {req.error}");
                onDone(false);
                yield break;
            }

            onProgress?.Invoke(1f);
            onDone(true);
        }
    }
}
