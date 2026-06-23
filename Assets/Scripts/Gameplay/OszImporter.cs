using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>Extracts a .osz archive (a renamed .zip) to a working directory.</summary>
    public static class OszImporter
    {
        /// <summary>
        /// Extract an .osz to a folder under the temporary cache directory and return that folder.
        /// If the archive was already extracted it is reused.
        /// </summary>
        public static string Extract(string oszPath)
        {
            string name = Path.GetFileNameWithoutExtension(oszPath);
            string target = Path.Combine(Application.temporaryCachePath, "osu_beatmaps", Sanitize(name));

            try
            {
                if (Directory.Exists(target) && HasOsuFiles(target))
                    return target;

                Directory.CreateDirectory(target);

                using var stream = File.OpenRead(oszPath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                    string destPath = Path.Combine(target, entry.FullName);
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    // Copy manually so we only depend on the core System.IO.Compression assembly
                    // (ZipFileExtensions.ExtractToFile lives in an assembly Unity doesn't reference).
                    using var entryStream = entry.Open();
                    using var outStream = File.Create(destPath);
                    entryStream.CopyTo(outStream);
                }
                return target;
            }
            catch (Exception e)
            {
                Debug.LogError($"[OszImporter] Failed to extract '{oszPath}': {e}");
                return Directory.Exists(target) ? target : null;
            }
        }

        public static List<string> FindOsuFiles(string folder)
        {
            var list = new List<string>();
            if (folder == null || !Directory.Exists(folder)) return list;
            list.AddRange(Directory.GetFiles(folder, "*.osu", SearchOption.AllDirectories));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static bool HasOsuFiles(string folder)
        {
            try { return Directory.GetFiles(folder, "*.osu", SearchOption.AllDirectories).Length > 0; }
            catch { return false; }
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }
}
