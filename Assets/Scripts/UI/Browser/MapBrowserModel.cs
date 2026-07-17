using System.Collections.Generic;
using OsuUnity.Gameplay;
using UnityEngine;

// INDEX: Map browser data model — the mirror-search result shape (BrowseSet/BrowseDiff), the client-side filter/sort rules, and every string the browser renders. Pure C#, no uGUI, so the layout and the screen can both read it (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// What the browser knows about a beatmapset, independent of how it is drawn. Built from
    /// <see cref="BeatmapDownloader.OnlineBeatmapset"/> (mirror search, osu!-API-shaped) and narrowed to
    /// osu!standard, which is all the importer accepts.
    /// </summary>
    public sealed class BrowseSet
    {
        public int Id;
        public string Artist = "", Title = "", Creator = "";
        public readonly List<BrowseDiff> Diffs = new List<BrowseDiff>();
        public BeatmapDownloadStatus Status = BeatmapDownloadStatus.NotDownloaded;

        // Set-level metadata the mirror returns (osu!-API shape). Ranked/… state is the mirror's own
        // string (ranked/loved/graveyard…); the two booleans back the Extra filters (client-side, §7 —
        // the mirror ignores its own e= param, but every result already carries these flags).
        public string RankState = "";
        public bool Video, Storyboard;
        public long PlayCount;
        public int FavouriteCount;

        public double StarsLo => Diffs.Count > 0 ? Diffs[0].Stars : 0;                  // Diffs are sorted easiest → hardest
        public double StarsHi => Diffs.Count > 0 ? Diffs[Diffs.Count - 1].Stars : 0;
    }

    /// <summary>One osu!standard difficulty of a <see cref="BrowseSet"/>, as the mirror reports it.</summary>
    public sealed class BrowseDiff
    {
        public string Version = "";
        public double Stars, Cs, Ar, Od, Hp, Bpm;
        public int LengthSec;
    }

    /// <summary>
    /// Beatmap category = the osu! listing's leaderboard-state row. Applied <b>server-side</b> via the
    /// mirror's <c>status</c> int (verified on osu.direct 2026-07-16): a single int, so the osu default
    /// "Has Leaderboard" (ranked∪approved∪qualified∪loved) is expressed by omitting the param — the
    /// mirror's own default listing. Favourites / My Maps are login-scoped and so absent (the browser is
    /// credential-free by design, <c>docs/osu-api.md</c> §4/§8).
    /// </summary>
    public enum BrowseCategory { HasLeaderboard, Ranked, Qualified, Loved, Pending, Wip, Graveyard }

    /// <summary>
    /// Result ordering, matching the osu! listing's "Sort by" row. Applied <b>server-side</b>: the mirror
    /// sorts far better than we could over the 50-row page it returns. Each maps to a mirror-sortable
    /// attribute (<c>docs/osu-api.md</c> §7) — osu!'s "Rating" is absent because no such attribute exists
    /// on the mirror.
    /// </summary>
    public enum BrowseSort { Title, Artist, Difficulty, Ranked, Plays, Favourites }

    /// <summary>Maps the category / sort choices onto the mirror-search query parameters.</summary>
    public static class BrowseQuery
    {
        /// <summary>The osu! listing order of the category words (first is the default row, "Has Leaderboard").</summary>
        public static readonly string[] CategoryWords =
            { "Has Leaderboard", "Ranked", "Qualified", "Loved", "Pending", "WIP", "Graveyard" };

        public static readonly string[] SortWords =
            { "Title", "Artist", "Difficulty", "Ranked", "Plays", "Favourites" };

        public static readonly string[] ExtraWords = { "Has Video", "Has Storyboard" };

        /// <summary>The mirror <c>status</c> int for a category, or <c>null</c> for "Has Leaderboard" (omit).</summary>
        public static int? Status(BrowseCategory c) => c switch
        {
            BrowseCategory.Ranked => 1,
            BrowseCategory.Qualified => 3,
            BrowseCategory.Loved => 4,
            BrowseCategory.Pending => 0,
            BrowseCategory.Wip => -1,
            BrowseCategory.Graveyard => -2,
            _ => null,   // HasLeaderboard — the mirror's default listing
        };

        /// <summary>The mirror <c>sort=attr:dir</c> string for a sort choice + direction.</summary>
        public static string Sort(BrowseSort s, bool desc)
        {
            string attr = s switch
            {
                BrowseSort.Title => "title",
                BrowseSort.Artist => "artist",
                BrowseSort.Difficulty => "beatmaps.difficulty_rating",
                BrowseSort.Ranked => "ranked_date",
                BrowseSort.Plays => "play_count",
                BrowseSort.Favourites => "favourite_count",
                _ => "ranked_date",
            };
            return attr + (desc ? ":desc" : ":asc");
        }

        /// <summary>The natural default direction for a sort (text A→Z; everything else biggest/newest first).</summary>
        public static bool DefaultDesc(BrowseSort s) => s != BrowseSort.Title && s != BrowseSort.Artist;
    }

    /// <summary>
    /// The star / length / BPM ranges narrowing the mirror's results. Applied <b>client-side</b>: the mirrors'
    /// filter parameters are unverified (<c>docs/osu-api.md</c> §7 leaves that open), and every result already
    /// carries the metadata these need, so filtering locally costs one pass over ~50 rows and no extra request.
    /// A range is "active" only when narrowed from its full span.
    /// </summary>
    public sealed class BrowseFilters
    {
        public const float StarLo = 0f, StarHi = 10f, LenLo = 0f, LenHi = 600f, BpmLo = 0f, BpmHi = 400f;

        public float StarMin = StarLo, StarMax = StarHi;
        public float LenMin = LenLo, LenMax = LenHi;   // seconds
        public float BpmMin = BpmLo, BpmMax = BpmHi;

        // "Extra" row (osu! listing): set-level, applied client-side on every returned result — the mirror
        // ignores its own e= param but each result carries the video/storyboard flags (docs/osu-api.md §7).
        public bool VideoOnly, StoryboardOnly;

        public bool StarActive => StarMin > StarLo + 0.01f || StarMax < StarHi - 0.01f;
        public bool LenActive => LenMin > LenLo + 0.5f || LenMax < LenHi - 0.5f;
        public bool BpmActive => BpmMin > BpmLo + 0.5f || BpmMax < BpmHi - 0.5f;
        public bool Any => StarActive || LenActive || BpmActive || VideoOnly || StoryboardOnly;

        public void Reset()
        {
            StarMin = StarLo; StarMax = StarHi;
            LenMin = LenLo; LenMax = LenHi;
            BpmMin = BpmLo; BpmMax = BpmHi;
            VideoOnly = StoryboardOnly = false;
        }

        /// <summary>A set passes when it clears the set-level Extra gates and <b>any</b> of its difficulties
        /// clears the range gates (same rule as local song select).</summary>
        public bool Passes(BrowseSet set)
        {
            if (VideoOnly && !set.Video) return false;
            if (StoryboardOnly && !set.Storyboard) return false;
            foreach (var d in set.Diffs)
                if (Passes(d)) return true;
            return false;
        }

        public bool Passes(BrowseDiff d)
        {
            if (d.Stars < Mathf.Min(StarMin, StarMax) - 0.001f || d.Stars > Mathf.Max(StarMin, StarMax) + 0.001f) return false;
            if (LenActive && (d.LengthSec < Mathf.Min(LenMin, LenMax) || d.LengthSec > Mathf.Max(LenMin, LenMax))) return false;
            if (BpmActive && (d.Bpm < Mathf.Min(BpmMin, BpmMax) || d.Bpm > Mathf.Max(BpmMin, BpmMax))) return false;
            return true;
        }
    }

    /// <summary>
    /// Every player-facing string the browser draws, in one place so the layout files stay pure construction.
    /// Rich-text colours come from <see cref="UiTheme"/> so a live palette swap is picked up.
    /// </summary>
    public static class BrowseText
    {
        public static string SetTitle(BrowseSet s) => $"{s.Artist} - {s.Title}";

        public static string SetSubtitle(BrowseSet s)
        {
            int n = s.Diffs.Count;
            string stars = Mathf.Approximately((float)s.StarsLo, (float)s.StarsHi)
                ? $"{s.StarsLo:0.##}★"
                : $"{s.StarsLo:0.##}–{s.StarsHi:0.##}★";
            string owned = s.Status == BeatmapDownloadStatus.Downloaded ? "  ·  ✓ in library" : "";
            return $"{s.Creator}  ·  {n} diff{(n == 1 ? "" : "s")}  ·  {stars}{owned}";
        }

        public static string DetailTitle(BrowseSet s)
            => $"{s.Artist} - {s.Title}\n<size=70%>{Dim($"mapped by {s.Creator}")}</size>";

        public static string DiffRow(BrowseDiff d)
            => $"<b>{d.Stars:0.00}★</b>  [{d.Version}]   " +
               Dim($"{Length(d.LengthSec)} · {d.Bpm:0}BPM · CS{d.Cs:0.#} AR{d.Ar:0.#} OD{d.Od:0.#} HP{d.Hp:0.#}");

        public static string DiffMeta(BrowseDiff d)
            => $"<b>{d.Stars:0.00}★</b>   {Length(d.LengthSec)}   {d.Bpm:0} BPM\n" +
               $"CS {d.Cs:0.#}   AR {d.Ar:0.#}   OD {d.Od:0.#}   HP {d.Hp:0.#}";

        public static string Length(int sec) => sec <= 0 ? "--:--" : $"{sec / 60}:{sec % 60:00}";

        public static string PrimaryLabel(BrowseSet s)
        {
            switch (s?.Status)
            {
                case BeatmapDownloadStatus.Downloading: return "Downloading…";
                case BeatmapDownloadStatus.Downloaded: return "✓  In library — Enter to play";
                default: return "⬇  Download";
            }
        }

        public const string Hint = "Type to search  ·  ↑↓ map  ·  ←→ difficulty  ·  Enter download  ·  Esc back";

        private static string Dim(string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.TextSecondary)}>{s}</color>";
    }
}
