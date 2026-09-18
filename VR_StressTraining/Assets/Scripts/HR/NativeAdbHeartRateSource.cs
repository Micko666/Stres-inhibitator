using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.HR
{
    /// <summary>Connection lifecycle of the future in-APK native ADB client (spec §35).</summary>
    public enum NativeAdbConnectionState
    {
        NotImplemented = 0,     // placeholder build — no native plugin present
        Disconnected = 1,
        Connecting = 2,
        Authenticating = 3,     // ADB RSA pair/auth handshake with the phone
        Connected = 4,
        StreamingLogcat = 5,
        Error = 6
    }

    /// <summary>
    /// C-ABI boundary that the future Android ARM64 native plugin must satisfy
    /// (P/Invoke from Unity IL2CPP). Full plan: HR_NATIVE_ADB_PLAN.md.
    /// Target chain: Xiaomi Smart Band 9 → Mi Fitness (telefon) → ADB/logcat
    /// preko Wi-Fi-ja → native ADB klijent U APK-u → HeartRateService.
    /// A desktop adb.exe can NEVER be bundled into or launched from a Quest APK.
    /// </summary>
    public interface INativeAdbBridge
    {
        NativeAdbConnectionState State { get; }
        string LastError { get; }
        /// <summary>Begin async connect to phoneHost:port (adbd over Wi-Fi, default 5555).</summary>
        bool Connect(string phoneHost, int port);
        void Disconnect();
        /// <summary>Non-blocking: drains parsed HR packets queued by the native logcat reader.</summary>
        int PollSamples(List<RawHrSample> buffer);
        event Action<NativeAdbConnectionState> StateChanged;
    }

    /// <summary>
    /// ARCHITECTURAL PLACEHOLDER (spec §35.1): occupies the NativeAdb slot in the
    /// source architecture so the app, developer panel and persistence already
    /// speak the final vocabulary. It NEVER fabricates a connection: the state is
    /// permanently NotImplemented, no samples are produced, and the wrist watch /
    /// dev panel display exactly that. The real implementation replaces the
    /// bridge instance without touching HeartRateService or callers.
    /// </summary>
    public sealed class NativeAdbHeartRateSource : IHeartRateSource, INativeAdbBridge
    {
        public HrSourceType SourceType => HrSourceType.NativeAdbPlaceholder;
        public bool IsRunning { get; private set; }

        public NativeAdbConnectionState State { get; private set; } =
            NativeAdbConnectionState.NotImplemented;
        public string LastError { get; private set; } =
            "Native ADB plugin nije implementiran (placeholder).";
        public string Status =>
            "NativeAdb: " + State + " — vidi HR_NATIVE_ADB_PLAN.md";

        public event Action<NativeAdbConnectionState> StateChanged;

        public void StartSource() => IsRunning = true;   // running ≠ connected
        public void StopSource() => IsRunning = false;

        public bool Connect(string phoneHost, int port)
        {
            // Honest failure: the placeholder must never claim progress.
            LastError = $"Connect({phoneHost}:{port}) odbijen — native plugin ne postoji u ovom buildu.";
            State = NativeAdbConnectionState.NotImplemented;
            StateChanged?.Invoke(State);
            return false;
        }

        public void Disconnect()
        {
            State = NativeAdbConnectionState.NotImplemented;
            StateChanged?.Invoke(State);
        }

        public int PollSamples(List<RawHrSample> buffer) => 0;

        public int DrainSamples(List<RawHrSample> buffer) => 0;   // never produces data
    }
}
