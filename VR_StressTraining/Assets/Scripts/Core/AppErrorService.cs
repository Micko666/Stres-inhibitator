using System;
using System.Collections.Generic;
using UnityEngine;

namespace StressTraining.Core
{
    public enum ErrorSessionImpact
    {
        None,           // cosmetic, session unaffected
        Degraded,       // session continues with reduced functionality (e.g. HR lost)
        InvalidatesData,// session data must be flagged
        Fatal           // session must end
    }

    [Serializable]
    public sealed class AppError
    {
        public string code;
        public string userMessage;
        public string technicalMessage;
        public string timestampUtcIso;
        public bool recoverable;
        public ErrorSessionImpact sessionImpact;
        [NonSerialized] public Exception exception;
    }

    /// <summary>
    /// Central error funnel. Every important failure is reported here so it can be
    /// logged to the session event stream, surfaced to the user in a controlled way,
    /// and never silently swallowed. One invalid HR packet or a missing audio clip
    /// must never crash a session — callers catch, report, and continue.
    /// </summary>
    public sealed class AppErrorService
    {
        public event Action<AppError> ErrorReported;

        private readonly List<AppError> _recent = new List<AppError>(32);
        public IReadOnlyList<AppError> Recent => _recent;

        public AppError Report(string code, string userMessage, string technicalMessage,
            bool recoverable, ErrorSessionImpact impact, Exception ex = null)
        {
            var err = new AppError
            {
                code = code,
                userMessage = userMessage,
                technicalMessage = technicalMessage,
                timestampUtcIso = UtcTime.NowIso(),
                recoverable = recoverable,
                sessionImpact = impact,
                exception = ex
            };
            _recent.Add(err);
            if (_recent.Count > 100) _recent.RemoveAt(0);

            string log = $"[AppError:{code}] {technicalMessage}" +
                         (ex != null ? $" | ex: {ex.GetType().Name}: {ex.Message}" : "");
            if (impact >= ErrorSessionImpact.InvalidatesData) Debug.LogError(log);
            else Debug.LogWarning(log);

            ErrorReported?.Invoke(err);
            return err;
        }
    }
}
