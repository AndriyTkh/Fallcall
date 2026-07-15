using System;
using System.Collections;
using System.Collections.Generic;
using OsuUnity.Gameplay;
using UnityEngine;

// INDEX: Map browser search driver — debounces typing, calls the mirrors via BeatmapDownloader, drops superseded responses and hands back a BrowseSet list (osu!standard only). Owns no UI (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// Turns a stream of keystrokes into at most one in-flight mirror query. Typing is debounced, a
    /// sequence guard drops a slow response that a newer query has already superseded, and the osu!-API-shaped
    /// mirror JSON is mapped into the browser's own <see cref="BrowseSet"/> model (osu!standard only — the rest
    /// of the import pipeline rejects other modes anyway).
    ///
    /// <para>Credential-free by design: mirror search covers the whole browser and official OAuth buys nothing
    /// v1 needs (<c>docs/osu-api.md</c> §4, §8). Owns no widgets — the screen subscribes.</para>
    /// </summary>
    public sealed class MapBrowserSearch : MonoBehaviour
    {
        /// <summary>Raised when a query actually goes out (after the debounce), so the screen can show a status.</summary>
        public event Action Started;

        /// <summary>Raised with the parsed results, or <c>null</c> when every mirror failed / was unreachable.</summary>
        public event Action<List<BrowseSet>> Completed;

        /// <summary>Answers "is this set id already imported?" so results can render the ✓ marker.</summary>
        public Func<int, bool> IsInLibrary;

        [Tooltip("Seconds of keyboard silence before a query is sent to the mirror.")]
        public float debounce = 0.45f;

        private Coroutine _co;
        private int _seq;

        /// <summary>Schedule a search. Each call supersedes the previous one; an empty query is the mirror's default listing.</summary>
        public void Query(string query)
        {
            Cancel();
            _co = StartCoroutine(Run(query ?? ""));
        }

        /// <summary>Drop any pending/in-flight query (its response, if it lands, is ignored).</summary>
        public void Cancel()
        {
            if (_co != null) { StopCoroutine(_co); _co = null; }
            _seq++;
        }

        private IEnumerator Run(string query)
        {
            yield return new WaitForSeconds(debounce);

            int seq = ++_seq;
            Started?.Invoke();

            List<BeatmapDownloader.OnlineBeatmapset> raw = null;
            yield return BeatmapDownloader.Search(query, 0, r => raw = r);
            if (seq != _seq) yield break;   // superseded by a newer query while this one was in flight

            _co = null;
            Completed?.Invoke(raw == null ? null : Map(raw));
        }

        // Mirror JSON → browser model. Sets with no osu!standard difficulty are dropped: the importer would
        // reject them, so showing them would only produce a download that can't be played.
        private List<BrowseSet> Map(List<BeatmapDownloader.OnlineBeatmapset> raw)
        {
            var list = new List<BrowseSet>(raw.Count);
            foreach (var s in raw)
            {
                if (s == null) continue;
                var set = new BrowseSet { Id = s.id, Artist = s.artist ?? "", Title = s.title ?? "", Creator = s.creator ?? "" };

                if (s.beatmaps != null)
                    foreach (var b in s.beatmaps)
                    {
                        if (b == null || b.mode_int != 0) continue;
                        set.Diffs.Add(new BrowseDiff
                        {
                            Version = b.version ?? "",
                            Stars = b.difficulty_rating,
                            Cs = b.cs,
                            Ar = b.ar,
                            Od = b.accuracy,   // osu!-API field names: accuracy = OD, drain = HP
                            Hp = b.drain,
                            Bpm = b.bpm,
                            LengthSec = b.total_length,
                        });
                    }

                if (set.Diffs.Count == 0) continue;
                set.Diffs.Sort((a, b) => a.Stars.CompareTo(b.Stars));   // easiest → hardest, like the local panel
                if (IsInLibrary != null && IsInLibrary(set.Id)) set.Status = BeatmapDownloadStatus.Downloaded;
                list.Add(set);
            }
            return list;
        }
    }
}
