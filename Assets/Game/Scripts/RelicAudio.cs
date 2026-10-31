using System;
using UnityEngine;

namespace RelicDrift
{
    /// <summary>Original music and sound effects synthesized from oscillators. No audio downloads.</summary>
    public sealed class RelicAudio : MonoBehaviour
    {
        const int SampleRate = 22050;
        static readonly float[] FinishNotes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        static readonly float[] MusicRoots = { 146.832f, 116.541f, 174.614f, 130.813f };
        static readonly float[] MusicIntervals = { 1f, 1.189207f, 1.498307f, 2f };
        static readonly int[] MusicPhrase = { 0, 2, 1, 2, 0, 3, 2, 1 };
        AudioSource music, effects;
        AudioClip pickup, impact, launch, click, finish, soundtrack;
        bool muted;
        float musicLevel = .12f;

        public bool Muted => muted;

        public void Initialize()
        {
            music = gameObject.AddComponent<AudioSource>();
            effects = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = effects.playOnAwake = false;
            music.spatialBlend = effects.spatialBlend = 0f;
            music.loop = true;
            muted = PlayerPrefs.GetInt("RelicDrift.Muted", 0) == 1;
            pickup = MakeClip("Sun crystal", .24f, (t, n) =>
                (float)(Math.Sin(t * Math.PI * 2 * (t < .09f ? 880 : 1320)) * Math.Exp(-t * 16)) * .35f);
            impact = MakeClip("Shield impact", .42f, (t, n) =>
                ((HashNoise(n) * .55f + (float)Math.Sin((120 * t - 80 * t * t) * Math.PI * 2) * .45f)
                 * (float)Math.Exp(-t * 12)) * .6f);
            launch = MakeClip("Courier launch", .6f, (t, n) =>
                (float)(Math.Sin((220 * t + 500 * t * t) * Math.PI * 2) * Math.Sin(Math.PI * t / .6f)) * .18f);
            click = MakeClip("Panel tap", .08f, (t, n) =>
                (float)(Math.Sin(t * Math.PI * 2 * 660) * Math.Exp(-t * 75)) * .16f);
            finish = MakeClip("Delivery signal", 1.1f, (t, n) =>
            {
                int note = Math.Min(3, (int)(t / .18f));
                double age = t - note * .18f;
                return (float)(Math.Sin(t * Math.PI * 2 * FinishNotes[note]) * Math.Exp(-age * 7)) * .2f;
            });
            soundtrack = MakeClip("Relic Drift - orbital lullaby", 16f, MusicSample);
            music.clip = soundtrack;
            music.volume = muted ? 0f : musicLevel;
            effects.mute = muted;
            music.Play();
        }

        public void SetPaused(bool paused) { musicLevel = paused ? .055f : .12f; }
        public void ToggleMute()
        {
            muted = !muted;
            effects.mute = muted;
            PlayerPrefs.SetInt("RelicDrift.Muted", muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        void Update()
        {
            if (music) music.volume = Mathf.MoveTowards(music.volume, muted ? 0f : musicLevel, Time.unscaledDeltaTime * .5f);
        }

        public void Pickup() { Play(pickup); }
        public void Hit() { Play(impact); }
        public void Launch() { Play(launch); }
        public void Click() { Play(click); }
        public void Finish() { Play(finish); }
        void Play(AudioClip clip) { if (effects && clip) effects.PlayOneShot(clip); }

        static AudioClip MakeClip(string title, float duration, Func<float, int, float> sample)
        {
            var data = new float[Mathf.CeilToInt(duration * SampleRate)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float edge = Mathf.Min(1f, Mathf.Min(t * 200f, (duration - t) * 200f));
                data[i] = Mathf.Clamp(sample(t, i) * edge, -.9f, .9f);
            }
            var clip = AudioClip.Create(title, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float MusicSample(float t, int n)
        {
            // D minor, B-flat, F, C. Sixteenth-note sine plucks over a soft bass pad.
            int chord = Mathf.Min(3, (int)(t / 4f));
            float root = MusicRoots[chord];
            // Major thirds on F and C, minor thirds on D and B-flat for a gentle suspended color.
            int step = (int)(t / .25f);
            int degree = MusicPhrase[step % MusicPhrase.Length];
            float interval = degree == 1 && chord >= 2 ? 1.259921f : MusicIntervals[degree];
            float age = t % .25f;
            float pluck = (float)(Math.Sin(2 * Math.PI * root * 2 * interval * t) * Math.Exp(-age * 18));
            float pad = (float)(Math.Sin(2 * Math.PI * root * .5f * t) * .55
                        + Math.Sin(2 * Math.PI * root * .75f * t) * .2);
            float chordEdge = Mathf.Min(1f, Mathf.Min((t % 4f) * 5f, (4f - t % 4f) * 5f));
            float beat = (float)(Math.Sin(2 * Math.PI * 52 * t) * Math.Exp(-(t % .5f) * 25));
            return (pluck * .27f + pad * .24f * chordEdge + beat * .07f) * .55f;
        }

        static float HashNoise(int n)
        {
            unchecked
            {
                uint x = (uint)n * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
                return ((x >> 22) ^ x) / (float)uint.MaxValue * 2f - 1f;
            }
        }

        void OnDestroy()
        {
            foreach (var clip in new[] { pickup, impact, launch, click, finish, soundtrack })
                if (clip) Destroy(clip);
        }
    }
}
