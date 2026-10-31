using System;

namespace RelicDrift
{
    public enum RunPhase { Title, Playing, Paused, Completed, GameOver }

    /// <summary>Small deterministic rules model. It contains no rendering or Unity input.</summary>
    public sealed class GameRules
    {
        public const float RunDuration = 60f;
        public const int StartingLives = 3;
        public RunPhase Phase { get; private set; } = RunPhase.Title;
        public int Score { get; private set; }
        public int Lives { get; private set; } = StartingLives;
        public int Crystals { get; private set; }
        public int Streak { get; private set; }
        public float SecondsRemaining { get; private set; } = RunDuration;
        public int Multiplier => Math.Min(5, 1 + Streak / 5);
        public float Progress => 1f - SecondsRemaining / RunDuration;

        public void Start()
        {
            Score = Crystals = Streak = 0;
            Lives = StartingLives;
            SecondsRemaining = RunDuration;
            Phase = RunPhase.Playing;
        }

        public void ShowTitle() { Phase = RunPhase.Title; }

        public void TogglePause()
        {
            if (Phase == RunPhase.Playing) Phase = RunPhase.Paused;
            else if (Phase == RunPhase.Paused) Phase = RunPhase.Playing;
        }

        public int Collect()
        {
            if (Phase != RunPhase.Playing) return 0;
            int points = 100 * Multiplier;
            Score += points;
            Crystals++;
            Streak++;
            return points;
        }

        public bool Hit()
        {
            if (Phase != RunPhase.Playing) return false;
            Streak = 0;
            Lives = Math.Max(0, Lives - 1);
            if (Lives == 0) Phase = RunPhase.GameOver;
            return true;
        }

        public void Advance(float seconds)
        {
            if (Phase != RunPhase.Playing || seconds <= 0f || float.IsNaN(seconds)) return;
            SecondsRemaining = Math.Max(0f, SecondsRemaining - seconds);
            if (SecondsRemaining == 0f) Phase = RunPhase.Completed;
        }
    }
}
