using OsuUnity.Beatmaps;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Plays short, procedurally-generated hit sounds so the project needs no audio assets.
    /// Different additions (whistle/finish/clap) get slightly different timbres.
    /// </summary>
    public sealed class HitSoundPlayer : MonoBehaviour
    {
        private AudioSource _source;
        private AudioClip _normal, _whistle, _finish, _clap, _tick;
        private int _lastPlayedMs = int.MinValue;

        public float Volume = 0.4f;

        public void Init()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            _normal = MakeClick(880f, 0.045f, 0.0f);
            _whistle = MakeClick(1500f, 0.08f, 0.0f);
            _finish = MakeClick(330f, 0.12f, 0.0f);
            _clap = MakeClick(1200f, 0.05f, 0.6f);
            _tick = MakeClick(2000f, 0.03f, 0.0f);
        }

        public void Play(HitSoundType sound, int timeMs)
        {
            // Avoid stacking many identical triggers on the exact same millisecond.
            if (timeMs == _lastPlayedMs) { }
            _lastPlayedMs = timeMs;

            _source.PlayOneShot(_normal, Volume);
            if ((sound & HitSoundType.Whistle) != 0) _source.PlayOneShot(_whistle, Volume * 0.7f);
            if ((sound & HitSoundType.Finish) != 0) _source.PlayOneShot(_finish, Volume * 0.9f);
            if ((sound & HitSoundType.Clap) != 0) _source.PlayOneShot(_clap, Volume * 0.7f);
        }

        public void PlayTick() => _source.PlayOneShot(_tick, Volume * 0.5f);

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
