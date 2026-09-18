using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Attach to any GameObject in your VR scene.
/// Listens on UDP 127.0.0.1:5005 for JSON packets from the Python bridge:
///   {"hr": 82, "ts": "15:31:50"}
///
/// Read CurrentHR and LastTimestamp from any other script.
/// Subscribe to OnHRUpdated to react immediately when a new value arrives.
/// </summary>
public class HRReceiver : MonoBehaviour
{
    [Header("Network")]
    [SerializeField] private int listenPort = 5005;

    [Header("Timeout")]
    [Tooltip("Seconds without a packet before CurrentHR resets to 0")]
    [SerializeField] private float staleTimeout = 10f;

    // --- Public state (read from other scripts) ---
    public static int    CurrentHR        { get; private set; }
    public static string LastTimestamp    { get; private set; } = "--:--:--";
    public static bool   IsReceiving      { get; private set; }

    /// <summary>Fired on the main thread whenever a new HR value arrives.</summary>
    public static event Action<int> OnHRUpdated;

    // --- Internals ---
    private UdpClient   _udp;
    private Thread      _thread;
    private bool        _running;

    // Double-buffer: background thread writes _pending*, main thread picks them up in Update()
    private int    _pendingHR;
    private string _pendingTs;
    private bool   _hasPending;
    private readonly object _lock = new object();

    private float _lastPacketTime;

    void Start()
    {
        _udp     = new UdpClient(new IPEndPoint(IPAddress.Loopback, listenPort));
        _running = true;
        _thread  = new Thread(ReceiveLoop) { IsBackground = true };
        _thread.Start();
        Debug.Log($"[HRReceiver] Listening on UDP :{listenPort}");
    }

    void Update()
    {
        // Drain pending value from background thread
        int  hr;
        string ts;
        bool got;
        lock (_lock)
        {
            got = _hasPending;
            hr  = _pendingHR;
            ts  = _pendingTs;
            _hasPending = false;
        }

        if (got)
        {
            CurrentHR     = hr;
            LastTimestamp = ts;
            IsReceiving   = true;
            _lastPacketTime = Time.time;
            OnHRUpdated?.Invoke(hr);
        }

        // Stale check
        if (IsReceiving && Time.time - _lastPacketTime > staleTimeout)
        {
            IsReceiving = false;
            CurrentHR   = 0;
            Debug.LogWarning("[HRReceiver] No packet for " + staleTimeout + "s — signal lost.");
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref remote);
                string json = Encoding.UTF8.GetString(data);

                // Minimal JSON parse — no external dependency needed
                int hr = ParseIntField(json, "hr");
                string ts = ParseStringField(json, "ts");
                if (hr < 30 || hr > 220) continue;

                lock (_lock)
                {
                    _pendingHR  = hr;
                    _pendingTs  = ts ?? LastTimestamp;
                    _hasPending = true;
                }
            }
            catch (SocketException)
            {
                // Socket closed on quit — exit cleanly
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HRReceiver] Parse error: " + ex.Message);
            }
        }
    }

    void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _thread?.Join(500);
    }

    // --- Tiny JSON helpers (avoids Newtonsoft dependency) ---

    private static int ParseIntField(string json, string key)
    {
        // Matches: "hr": 82
        int ki = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (ki < 0) return -1;
        int colon = json.IndexOf(':', ki);
        if (colon < 0) return -1;
        int start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
        return end > start ? int.Parse(json.Substring(start, end - start)) : -1;
    }

    private static string ParseStringField(string json, string key)
    {
        // Matches: "ts": "15:31:50"
        int ki = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (ki < 0) return null;
        int colon = json.IndexOf(':', ki);
        if (colon < 0) return null;
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return null;
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }
}
