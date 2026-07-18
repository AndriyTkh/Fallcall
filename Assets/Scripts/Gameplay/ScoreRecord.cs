using System;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// One persisted play result for a single beatmap-difficulty. Plain, <see cref="SerializableAttribute"/>
    /// data only so Unity's <see cref="JsonUtility"/> round-trips it; grouped/queried by <see cref="MapKey"/>
    /// in <see cref="LocalScoreStore"/>. New fields must be appended with sane zero/empty defaults so old
    /// files keep loading (forward-compatible for the future local-ranking UI).
    /// </summary>
    [Serializable]
    public sealed class ScoreRecord
    {
        /// <summary>Beatmap-difficulty this play belongs to. See <see cref="LocalScoreStore.KeyFor"/>.</summary>
        public string MapKey;

        /// <summary>UTC time the play finished, ISO-8601 round-trip ("o"). Stored as a string because
        /// JsonUtility can't serialize <see cref="DateTime"/>; the "o" format sorts lexically = chronologically.</summary>
        public string TimestampUtc;

        public long Score;
        public double Accuracy;      // 0..1
        public int MaxCombo;

        public int Count300;
        public int Count100;
        public int Count50;
        public int CountMiss;

        /// <summary>osu!-style rank letter (SS/S/A/B/C/D) captured at finish.</summary>
        public string Rank;

        // ---- mods / flags (capture what we can read now; leave room for the rest) ----
        /// <summary>No-Fail (osu! NF) was active for this play.</summary>
        public bool NoFail;

        /// <summary>Autoplay drove the cursor — not a legit human score. Kept so the ranking UI can filter it.</summary>
        public bool Autoplay;

        /// <summary>Reserved for a future encoded mod set (DT/HR/etc.). Empty until mods exist.</summary>
        public string Mods = "";

        /// <summary>Convenience: parse <see cref="TimestampUtc"/> back to a DateTime (UTC). Falls back to
        /// <see cref="DateTime.MinValue"/> if the stored string is missing/garbage.</summary>
        public DateTime Timestamp
        {
            get => DateTime.TryParse(
                TimestampUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt : DateTime.MinValue;
        }
    }
}
