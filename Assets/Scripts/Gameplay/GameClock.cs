using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Audio-synchronised clock based on <see cref="AudioSettings.dspTime"/>. The song is scheduled
    /// to start after a lead-in so the first objects can approach. Time is reported in milliseconds
    /// and may be negative before the audio actually begins.
    /// </summary>
    public sealed class GameClock
    {
        private AudioSource _source;
        private double _dspSongStart;   // dsp time at which song time == 0
        private bool _scheduled;
        private bool _audioStarted;
        private bool _paused;
        private double _pausedTime;     // song time captured at pause

        public bool IsPaused => _paused;

        public double LeadInMs { get; private set; }

        /// <summary>Current song time in milliseconds.</summary>
        public double TimeMs { get; private set; }

        public bool Finished { get; private set; }

        public void Prepare(AudioSource source, double leadInMs)
        {
            _source = source;
            LeadInMs = Mathf.Max(1000f, (float)leadInMs); // always give at least 1s of approach
            _scheduled = false;
            _audioStarted = false;
            _paused = false;
            Finished = false;
            TimeMs = -LeadInMs;
        }

        public void Start()
        {
            double now = AudioSettings.dspTime;
            _dspSongStart = now + LeadInMs / 1000.0;
            if (_source != null && _source.clip != null)
            {
                _source.PlayScheduled(_dspSongStart);
                _scheduled = true;
            }
        }

        /// <summary>Freeze the clock and the audio. Time stops advancing until <see cref="Resume"/>.</summary>
        public void Pause()
        {
            if (_paused) return;
            _paused = true;
            _pausedTime = TimeMs;
            if (_source != null && _source.isPlaying) _source.Pause();
        }

        /// <summary>Resume from the paused position, re-anchoring the dsp clock so time stays in sync.</summary>
        public void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _dspSongStart = AudioSettings.dspTime - _pausedTime / 1000.0;

            if (_audioStarted)
            {
                if (_source != null) _source.UnPause();
            }
            else if (_source != null && _source.clip != null)
            {
                // Paused during lead-in: audio never started, so reschedule its start.
                _source.Stop();
                _source.PlayScheduled(_dspSongStart);
                _scheduled = true;
            }
        }

        /// <summary>Call every frame.</summary>
        public void Update()
        {
            if (_paused) return;
            double now = AudioSettings.dspTime;
            TimeMs = (now - _dspSongStart) * 1000.0;

            if (_scheduled && !_audioStarted && now >= _dspSongStart)
                _audioStarted = true;

            if (_source != null && _source.clip != null && _audioStarted && !_source.isPlaying)
                Finished = true;
        }

        public double SongLengthMs => _source != null && _source.clip != null
            ? _source.clip.length * 1000.0
            : 0;
    }
}
