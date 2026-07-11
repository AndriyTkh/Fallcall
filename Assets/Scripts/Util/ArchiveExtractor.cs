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
        // Written into the target folder only after every entry extracts successfully. A cache is reused
        // only when this marker is present, so a half-written or partially purged folder is never trusted.
        private const string CompleteMarker = ".extracted";

        /// <summary>
        /// Extract <paramref name="archivePath"/> into a sub-folder of persistent storage and return it.
        /// If <paramref name="reuseIfExists"/> and the target holds a *complete* prior extraction (marked
        /// by <see cref="CompleteMarker"/>), the existing copy is reused; otherwise the folder is wiped and
        /// re-extracted, so a partially written or OS-purged cache can never be served half-loaded.
        /// </summary>
        public static string Extract(string archivePath, string category, bool reuseIfExists = true)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return null;

            string name = Path.GetFileNameWithoutExtension(archivePath);
            // persistentDataPath, not temporaryCachePath: the latter lives under the OS temp dir and gets
            // purged by Disk Cleanup / Storage Sense, which was leaving hollow skin folders (empty dir
            // skeletons, no files) that then loaded as a skin with none of its elements.
            string target = Path.Combine(Application.persistentDataPath, "extracted", category, Sanitize(name));

            try
            {
                if (reuseIfExists && File.Exists(Path.Combine(target, CompleteMarker)))
                    return target;

                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.CreateDirectory(target);

                using (var stream = File.OpenRead(archivePath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
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
                }

                File.WriteAllText(Path.Combine(target, CompleteMarker), archivePath);
                return target;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArchiveExtractor] Failed to extract '{archivePath}': {e}");
                // Never return a partial extraction: without the marker the caller won't reuse it, but be
                // explicit so a broken folder isn't scanned as if it were a valid archive.
                return null;
            }
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }
}
