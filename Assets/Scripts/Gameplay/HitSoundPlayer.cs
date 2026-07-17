using System.Collections;
using System.Collections.Generic;
using System.IO;
using OsuUnity.Beatmaps;
using OsuUnity.Skinning;
using OsuUnity.Util;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Plays hit sounds and the combo-break sound. Samples resolve the way osu! does: the bank
    /// (normal/soft/drum), custom index and volume come from the object's hitSample, then the active
    /// timing point, then the beatmap default. The file itself is looked up beatmap folder → skin
    /// folder → the bundled osu! default skin (Assets/Resources/DefaultSkin), and a custom index that
    /// has no file falls back to index 1 before dropping to the default skin — same order as osu!.
    ///
    /// Because the default skin ships with the game, every sample name always resolves to a real
    /// sample; there is no synthesised fallback.
    ///
    /// An empty sample file (0 bytes or a header-only WAV) is treated as osu! treats it: intentional
    /// silence, not a missing sample — so skins can mute individual sounds.
    /// See https://osu.ppy.sh/wiki/en/Skinning/osu! — "Hitsounds".
    /// </summary>
    public sealed class HitSoundPlayer : MonoBehaviour
    {
        public float Volume = 0.5f;

        private AudioSource _source;
        private Beatmap _map;

        /// <summary>Bundled osu! default-skin samples, keyed "bank-type" (never a custom index).</summary>
        private readonly Dictionary<string, AudioClip> _defaults = new Dictionary<string, AudioClip>();

        // Skin/beatmap samples keyed by "bank-type[index]" (e.g. "soft-hitnormal", "normal-hitclap3").
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _silent = new HashSet<string>(); // present-but-empty = mute

        private static readonly string[] Banks = { "normal", "soft", "drum" };
        private static readonly string[] Types = { "hitnormal", "hitwhistle", "hitfinish", "hitclap", "slidertick" };
        private static readonly string[] Exts = { ".wav", ".ogg", ".mp3" };

        private const string ComboBreak = "combobreak";
        private const string DefaultSkinPath = "DefaultSkin/";

        public void Init(Beatmap map)
        {
            _map = map;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            LoadDefaultSkin();
            StartCoroutine(Preload());
        }

        /// <summary>Play an object/edge: the normal sound plus any whistle/finish/clap additions.</summary>
        public void Play(HitSoundType additions, int timeMs,
                         SampleBank normalBank = SampleBank.Auto,
                         SampleBank additionBank = SampleBank.Auto,
                         int customIndex = 0, int volumeOverride = 0)
        {
            var tp = _map?.GetTimingPointAt(timeMs);
            SampleBank normal = ResolveBank(normalBank, tp);
            SampleBank addition = additionBank != SampleBank.Auto ? additionBank : normal;
            string suffix = Suffix(customIndex, tp);
            float vol = ResolveVolume(volumeOverride, tp);

            PlayComponent(normal, "hitnormal", suffix, vol);
            if ((additions & HitSoundType.Whistle) != 0) PlayComponent(addition, "hitwhistle", suffix, vol * 0.85f);
            if ((additions & HitSoundType.Finish) != 0) PlayComponent(addition, "hitfinish", suffix, vol * 0.95f);
            if ((additions & HitSoundType.Clap) != 0) PlayComponent(addition, "hitclap", suffix, vol * 0.85f);
        }

        /// <summary>Play a slider tick from the slider's normal bank.</summary>
        public void PlayTick(int timeMs, SampleBank normalBank = SampleBank.Auto,
                             int customIndex = 0, int volumeOverride = 0)
        {
            var tp = _map?.GetTimingPointAt(timeMs);
            SampleBank bank = ResolveBank(normalBank, tp);
            float vol = ResolveVolume(volumeOverride, tp) * 0.6f; // ticks sit under the hits
            PlayComponent(bank, "slidertick", Suffix(customIndex, tp), vol);
        }

        /// <summary>
        /// Play the combo-break sound. Unlike hit sounds this is skin-only (a beatmap can't ship one)
        /// and ignores timing-point volume — osu! plays it at the effect volume, not the map's.
        /// </summary>
        public void PlayComboBreak()
        {
            if (_clips.TryGetValue(ComboBreak, out var clip)) { _source.PlayOneShot(clip, Volume); return; }
            if (_silent.Contains(ComboBreak)) return;
            if (_defaults.TryGetValue(ComboBreak, out var def)) _source.PlayOneShot(def, Volume);
        }

        // ----------------------------------------------------------------- resolution

        /// <summary>
        /// Resolve one sample component and play it. Order matches osu!: the exact custom index from the
        /// beatmap/skin, then index 1 of the same bank+type, then the bundled default skin.
        /// </summary>
        private void PlayComponent(SampleBank bank, string type, string suffix, float vol)
        {
            string name = BankName(bank) + "-" + type;

            if (TryPlay(name + suffix, vol)) return;               // exact custom index (or index 1)
            if (suffix.Length > 0 && TryPlay(name, vol)) return;   // custom index absent -> index 1
            if (_defaults.TryGetValue(name, out var def)) _source.PlayOneShot(def, vol);
        }

        /// <summary>True if the key resolved — either to a loaded clip (played) or to deliberate silence.</summary>
        private bool TryPlay(string key, float vol)
        {
            if (_clips.TryGetValue(key, out var clip)) { _source.PlayOneShot(clip, vol); return true; }
            return _silent.Contains(key); // skin muted this sample on purpose: resolved, play nothing
        }

        private SampleBank ResolveBank(SampleBank obj, TimingPoint tp)
        {
            if (obj != SampleBank.Auto) return obj;
            if (tp != null && tp.SampleSet >= 1 && tp.SampleSet <= 3) return (SampleBank)tp.SampleSet;
            return BankFromName(_map?.General.SampleSet);
        }

        private float ResolveVolume(int overrideVol, TimingPoint tp)
        {
            int raw = overrideVol > 0 ? overrideVol : (tp != null ? tp.Volume : 100);
            return Volume * Mathf.Clamp01(raw / 100f);
        }

        /// <summary>Filename suffix for a custom sample index (index 0/1 = default, no suffix; 2+ appends).</summary>
        private static string Suffix(int objIndex, TimingPoint tp)
        {
            int idx = objIndex > 0 ? objIndex : (tp != null ? tp.SampleIndex : 0);
            return idx >= 2 ? idx.ToString() : "";
        }

        private static string BankName(SampleBank b)
        {
            switch (b)
            {
                case SampleBank.Soft: return "soft";
                case SampleBank.Drum: return "drum";
                default: return "normal";
            }
        }

        private static SampleBank BankFromName(string s)
        {
            if (string.IsNullOrEmpty(s)) return SampleBank.Normal;
            switch (s.Trim().ToLowerInvariant())
            {
                case "soft": case "2": return SampleBank.Soft;
                case "drum": case "3": return SampleBank.Drum;
                default: return SampleBank.Normal;
            }
        }

        // ----------------------------------------------------------------- loading

        /// <summary>
        /// Load the bundled osu! default skin from Resources. These are the last-resort samples every
        /// lookup falls back to, so they load synchronously before the first hit object can fire.
        /// </summary>
        private void LoadDefaultSkin()
        {
            foreach (string bank in Banks)
                foreach (string type in Types)
                {
                    string key = bank + "-" + type;
                    var clip = Resources.Load<AudioClip>(DefaultSkinPath + key);
                    if (clip != null) _defaults[key] = clip;
                }

            var cb = Resources.Load<AudioClip>(DefaultSkinPath + ComboBreak);
            if (cb != null) _defaults[ComboBreak] = cb;
        }

        /// <summary>
        /// Load every sample we might reference from the beatmap folder (priority) then the skin folder.
        /// Runs once at startup; the first few hits use the default skin if a file is still loading.
        /// </summary>
        private IEnumerator Preload()
        {
            string[] dirs = { _map?.Directory, Skin.Current?.Directory };

            foreach (string suffix in CollectSuffixes())
                foreach (string bank in Banks)
                    foreach (string type in Types)
                        yield return TryLoad(bank + "-" + type + suffix, dirs);

            // combobreak is a skin sample only — a beatmap folder never provides one.
            yield return TryLoad(ComboBreak, new[] { Skin.Current?.Directory });
        }

        /// <summary>The set of custom-index suffixes referenced anywhere in the map (plus the default "").</summary>
        private HashSet<string> CollectSuffixes()
        {
            var set = new HashSet<string> { "" };
            if (_map != null)
            {
                foreach (var tp in _map.TimingPoints)
                    if (tp.SampleIndex >= 2) set.Add(tp.SampleIndex.ToString());
                foreach (var ho in _map.HitObjects)
                    if (ho.CustomSampleIndex >= 2) set.Add(ho.CustomSampleIndex.ToString());
            }
            return set;
        }

        private IEnumerator TryLoad(string key, string[] dirs)
        {
            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                foreach (string ext in Exts)
                {
                    string path = Path.Combine(dir, key + ext);
                    if (!File.Exists(path)) continue;

                    // osu! skins mute a sound by shipping an empty or header-only file. A WAV header is
                    // 44 bytes with no PCM; anything that small carries no audio and makes FMOD throw
                    // "Unsupported file or audio format" if handed to the decoder, so treat it as silence.
                    if (new FileInfo(path).Length <= 44) { _silent.Add(key); yield break; }

                    AudioClip loaded;
                    if (ext == ".wav")
                    {
                        // Decode WAV ourselves: Unity's FMOD-backed loader logs an "Unsupported file or
                        // audio format" error for some perfectly valid skin WAVs (extra chunks, etc.), and
                        // that error can't be caught from GetContent. A hand-rolled PCM reader avoids it.
                        loaded = WavDecoder.Decode(File.ReadAllBytes(path), key);
                    }
                    else
                    {
                        AudioClip c = null;
                        yield return AssetLoader.LoadAudio(path, x => c = x);
                        loaded = c;
                    }

                    if (loaded != null && loaded.samples > 0) _clips[key] = loaded;
                    else _silent.Add(key); // header-only / unreadable: treat as intentional silence
                    yield break;           // first matching file (beatmap beats skin) wins
                }
            }
        }
    }
}
