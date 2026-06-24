using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace OsuUnity.Util
{
    /// <summary>
    /// Extracts a zip-based archive (.osz beatmap, .osk skin — both are plain .zip files) to a folder.
    /// Kept dependency-light: only references the core System.IO.Compression assembly that Unity ships.
    /// </summary>
    public static class ArchiveExtractor
    {
        /// <summary>
        /// Extract <paramref name="archivePath"/> into a sub-folder of the temporary cache and return it.
        /// If <paramref name="reuseIfExists"/> and the target already holds files, the existing copy is reused.
        /// </summary>
        public static string Extract(string archivePath, string category, bool reuseIfExists = true)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return null;

            string name = Path.GetFileNameWithoutExtension(archivePath);
            string target = Path.Combine(Application.temporaryCachePath, category, Sanitize(name));

            try
            {
                if (reuseIfExists && Directory.Exists(target) && HasAnyFiles(target))
                    return target;

                Directory.CreateDirectory(target);

                using var stream = File.OpenRead(archivePath);
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
                Debug.LogError($"[ArchiveExtractor] Failed to extract '{archivePath}': {e}");
                return Directory.Exists(target) ? target : null;
            }
        }

        private static bool HasAnyFiles(string folder)
        {
            try { return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length > 0; }
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
