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
    /// Plays hit sounds. When a skin (or the beatmap folder) ships sample files we use those, resolving
    /// the bank (normal/soft/drum), custom index and volume the way osu! does — from the object's
    /// hitSample, the active timing point, then the beatmap default. Anything not provided by a sample
    /// falls back to a short procedurally-generated click, so the game still sounds right with no assets.
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

        // Procedural fallbacks, keyed by sample type.
        private AudioClip _normal, _whistle, _finish, _clap, _tick;

        // Skin/beatmap samples keyed by "bank-type[index]" (e.g. "soft-hitnormal", "normal-hitclap3").
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _silent = new HashSet<string>(); // present-but-empty = mute

        private static readonly string[] Banks = { "normal", "soft", "drum" };
        private static readonly string[] Types = { "hitnormal", "hitwhistle", "hitfinish", "hitclap", "slidertick" };
        private static readonly string[] Exts = { ".wav", ".ogg", ".mp3" };

        public void Init(Beatmap map)
        {
            _map = map;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            _normal = MakeClick(880f, 0.045f, 0.0f);
            _whistle = MakeClick(1500f, 0.08f, 0.0f);
            _finish = MakeClick(330f, 0.12f, 0.0f);
            _clap = MakeClick(1200f, 0.05f, 0.6f);
            _tick = MakeClick(2000f, 0.03f, 0.0f);

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

            PlayComponent(normal, "hitnormal", suffix, vol, _normal);
            if ((additions & HitSoundType.Whistle) != 0) PlayComponent(addition, "hitwhistle", suffix, vol * 0.85f, _whistle);
            if ((additions & HitSoundType.Finish) != 0) PlayComponent(addition, "hitfinish", suffix, vol * 0.95f, _finish);
            if ((additions & HitSoundType.Clap) != 0) PlayComponent(addition, "hitclap", suffix, vol * 0.85f, _clap);
        }

        /// <summary>Play a slider tick from the slider's normal bank.</summary>
        public void PlayTick(int timeMs, SampleBank normalBank = SampleBank.Auto,
                             int customIndex = 0, int volumeOverride = 0)
        {
            var tp = _map?.GetTimingPointAt(timeMs);
            SampleBank bank = ResolveBank(normalBank, tp);
            float vol = ResolveVolume(volumeOverride, tp) * 0.6f; // ticks sit under the hits
            PlayComponent(bank, "slidertick", Suffix(customIndex, tp), vol, _tick);
        }

        // ----------------------------------------------------------------- resolution

        private void PlayComponent(SampleBank bank, string type, string suffix, float vol, AudioClip fallback)
        {
            string key = BankName(bank) + "-" + type + suffix;
            if (_clips.TryGetValue(key, out var clip)) { _source.PlayOneShot(clip, vol); return; }
            if (_silent.Contains(key)) return;                  // skin muted this sample on purpose
            _source.PlayOneShot(fallback, vol);                 // no sample present -> synth
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
        /// Load every sample we might reference from the beatmap folder (priority) then the skin folder.
        /// Runs once at startup; the first few hits use the synth fallback if a file is still loading.
        /// </summary>
        private IEnumerator Preload()
        {
            string[] dirs = { _map?.Directory, Skin.Current?.Directory };

            foreach (string suffix in CollectSuffixes())
                foreach (string bank in Banks)
                    foreach (string type in Types)
                        yield return TryLoad(bank + "-" + type + suffix, dirs);
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

        /// <summary>Generate a percussive click: a decaying sine mixed with optional noise.</summary>
        private static AudioClip MakeClick(float freq, float duration, float noise)
        {
            int rate = 44100;
            int samples = Mathf.Max(1, (int)(rate * duration));
            var data = new float[samples];
            var rng = new System.Random(unchecked((int)(freq * 1000)));
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 35f);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                float n = noise > 0 ? (float)(rng.NextDouble() * 2 - 1) * noise : 0f;
                data[i] = (tone * (1f - noise) + n) * env;
            }
            var clip = AudioClip.Create("click", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
