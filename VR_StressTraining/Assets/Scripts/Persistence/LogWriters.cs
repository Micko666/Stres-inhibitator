using System;
using System.IO;
using System.Text;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Persistence
{
    /// <summary>
    /// Append-only JSONL stream writer. One JSON object per line.
    /// - Buffered; Flush() is called after important events and block ends,
    ///   never per-frame (Quest performance rule).
    /// - Never throws into gameplay: write failures are counted and reported once.
    /// </summary>
    public class JsonlWriter : IDisposable
    {
        private StreamWriter _writer;
        private readonly object _lock = new object();
        public string Path { get; }
        public int FailedWrites { get; private set; }

        public JsonlWriter(string path)
        {
            Path = path;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _writer = new StreamWriter(new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonlWriter] Cannot open {path}: {ex.Message}");
                _writer = null;
                FailedWrites++;
            }
        }

        public void Append<T>(T record)
        {
            if (_writer == null) { FailedWrites++; return; }
            try
            {
                string json = JsonUtility.ToJson(record);
                lock (_lock) _writer.WriteLine(json);
            }
            catch (Exception)
            {
                FailedWrites++;
            }
        }

        public void Flush()
        {
            if (_writer == null) return;
            try { lock (_lock) _writer.Flush(); }
            catch (Exception) { FailedWrites++; }
        }

        public void Dispose()
        {
            try
            {
                lock (_lock)
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                    _writer = null;
                }
            }
            catch (Exception) { /* disposing must never throw */ }
        }
    }

    /// <summary>events.jsonl — every important decision is reconstructable from this stream.</summary>
    public sealed class EventLogWriter : JsonlWriter
    {
        private long _nextEventId;
        public EventLogWriter(string path) : base(path) { }

        public void Log(string eventType, string sessionId, string appState,
            double monotonicSeconds, string payloadJson = "")
        {
            Append(new EventRecord
            {
                eventId = _nextEventId++,
                eventType = eventType,
                timestampUtcIso = Core.UtcTime.NowIso(),
                monotonicSeconds = monotonicSeconds,
                sessionId = sessionId ?? "",
                appState = appState ?? "",
                payloadJson = payloadJson ?? ""
            });
        }
    }

    /// <summary>trials.jsonl</summary>
    public sealed class TrialLogWriter : JsonlWriter
    {
        public TrialLogWriter(string path) : base(path) { }
        public void Log(TrialRecord trial) => Append(trial);
    }

    /// <summary>hr.jsonl</summary>
    public sealed class HeartRateLogWriter : JsonlWriter
    {
        private long _nextSampleId;
        public HeartRateLogWriter(string path) : base(path) { }

        public void Log(HeartRateSampleRecord sample)
        {
            sample.sampleId = _nextSampleId++;
            Append(sample);
        }
    }
}
