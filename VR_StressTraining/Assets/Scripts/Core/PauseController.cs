using System;
using System.Collections.Generic;

namespace StressTraining.Core
{
    /// <summary>
    /// Single owner of the paused/resumed decision during an active session.
    /// Systems that consume time (tasks, pressure, tablet, HR tagging, animations)
    /// subscribe to <see cref="PauseChanged"/> or poll <see cref="IsPaused"/>.
    ///
    /// PAUSE IS ALWAYS MANUAL (left-controller Y). Nothing in the app may pause a
    /// session automatically — a brief HR/signal hiccup must never stop the timer,
    /// the task or the baseline/recovery measurement. Data quality on a gap is
    /// recorded as a metric (valid-sample ratio / gap length), not as a pause.
    ///
    /// Deliberately does NOT use Time.timeScale as the pause mechanism
    /// (architecture rule 12): every time-consuming system is pause-aware instead.
    /// </summary>
    public sealed class PauseController
    {
        public struct PauseRecord
        {
            public string OpenedAtUtcIso;
            public string ClosedAtUtcIso;
            public string Reason;
            public double ActiveSecondsAtPause;
        }

        public bool IsPaused { get; private set; }
        public string CurrentReason { get; private set; }

        public event Action<bool, string> PauseChanged;

        private readonly SessionClock _clock;
        private readonly List<PauseRecord> _records = new List<PauseRecord>(8);
        private PauseRecord _open;

        public IReadOnlyList<PauseRecord> Records => _records;

        public PauseController(SessionClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Opens the manual pause. Idempotent.</summary>
        public void Pause(string reason)
        {
            if (IsPaused) return;
            IsPaused = true;
            CurrentReason = reason;
            _open = new PauseRecord
            {
                OpenedAtUtcIso = UtcTime.NowIso(),
                Reason = reason,
                ActiveSecondsAtPause = _clock.ActiveElapsedSeconds
            };
            _clock.SetPaused(true);
            PauseChanged?.Invoke(true, reason);
        }

        /// <summary>Closes the manual pause. Idempotent.</summary>
        public void Resume(string reason = "user_continue")
        {
            if (!IsPaused) return;
            IsPaused = false;
            CurrentReason = null;
            _open.ClosedAtUtcIso = UtcTime.NowIso();
            _records.Add(_open);
            _open = default;
            _clock.SetPaused(false);
            PauseChanged?.Invoke(false, reason);
        }

        public void Reset()
        {
            if (IsPaused) _clock.SetPaused(false);
            IsPaused = false;
            CurrentReason = null;
            _open = default;
            _records.Clear();
        }
    }
}
