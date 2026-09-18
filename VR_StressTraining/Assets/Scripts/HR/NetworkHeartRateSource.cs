using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.HR
{
    /// <summary>
    /// UDP heart-rate source for the Python bridge and protocol-v1 phone relay.
    /// Phone-relay packets are parsed and validated on the Unity main thread.
    /// After a valid sample or heartbeat passes the ingest gate, Unity returns a
    /// protocol-v1 ACK to the packet's source IP and source port. Heartbeats keep
    /// transport diagnostics fresh but never become physiological samples.
    /// </summary>
    public sealed class NetworkHeartRateSource : IHeartRateSource
    {
        public const int ProtocolVersion = HrPacketParser.CurrentProtocolVersion;
        public HrSourceType SourceType => HrSourceType.NetworkBridge;
        public bool IsRunning { get; private set; }
        public string ListenerStatus { get; private set; } = "stopped";

        private readonly string _bindAddress;
        private readonly int _port;
        private readonly int _minBpm;
        private readonly int _maxBpm;

        public string BindAddress => _bindAddress;
        public int Port => _port;
        public string BindEndpoint => _bindAddress + ":" + _port;

        private UdpClient _udp;
        private UdpClient _ackUdp;
        private Thread _thread;
        private volatile bool _stop;
        private bool _bindWarningLogged;

        // Process-wide handle to the last socket bound on this port. A previous Editor
        // Play-mode run that did not shut down cleanly (a crash, a forced stop) leaves
        // its receive socket alive inside the SAME Unity process; the next run closes
        // it here before rebinding, so a leaked socket can never shadow the new one.
        private static readonly object s_socketLock = new object();
        private static UdpClient s_lastBoundUdp;

        private struct QueuedPayload
        {
            public string Json;
            public string ReceivedAtUtcIso;
            public double RealtimeAtReceive;
            public string RemoteAddress;
            public int RemotePort;
        }

        private readonly Queue<QueuedPayload> _queue = new Queue<QueuedPayload>(32);
        private readonly object _lock = new object();

        public int ParseFailureCount { get; private set; }
        public int ReconnectCount { get; private set; }
        public int AckSentCount { get; private set; }
        public int AckSendFailureCount { get; private set; }
        public int AcceptedSampleCount { get; private set; }
        public event Action<string> StatusChanged;

        private readonly HrRelayIngest _ingest = new HrRelayIngest();
        public string BoundSessionToken => _ingest.BoundToken;
        public int RejectedPacketCount => _ingest.RejectedCount;
        public int WrongTokenCount => _ingest.WrongTokenCount;
        public int DuplicateOrOutOfOrderCount => _ingest.DuplicateOrOutOfOrderCount;
        public int StaleWatchCount => _ingest.StaleWatchCount;
        public int HeartbeatCount => _ingest.HeartbeatCount;

        public double LastPacketRealtime { get; private set; } = -1;
        public double LastHeartbeatRealtime { get; private set; } = -1;
        public double LastAcceptedSampleRealtime { get; private set; } = -1;
        public string LastSenderIp { get; private set; } = "-";
        public int LastSenderPort { get; private set; } = -1;
        public int LastSequence { get; private set; } = -1;
        public int LastAckSequence { get; private set; } = -1;

        private double _realtimeBase;
        private System.Diagnostics.Stopwatch _stopwatch;

        public NetworkHeartRateSource(string bindAddress, int port, int minBpm = 30, int maxBpm = 220)
        {
            _bindAddress = string.IsNullOrEmpty(bindAddress) ? "0.0.0.0" : bindAddress;
            _port = port;
            _minBpm = minBpm;
            _maxBpm = maxBpm;
        }

        public void StartSource()
        {
            if (IsRunning) return;
            _stop = false;
            _ingest.Reset();
            ResetDiagnostics();
            _realtimeBase = Time.realtimeSinceStartupAsDouble;
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try { _ackUdp = new UdpClient(AddressFamily.InterNetwork); }
            catch { _ackUdp = null; }
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "HRUdpReceive" };
            _thread.Start();
            IsRunning = true;
        }

        public void StopSource()
        {
            _stop = true;
            try { _udp?.Close(); } catch { }
            try { _ackUdp?.Close(); } catch { }
            _thread?.Join(500);
            lock (s_socketLock) { if (s_lastBoundUdp == _udp) s_lastBoundUdp = null; }
            _udp = null;
            _ackUdp = null;
            IsRunning = false;
            SetStatus("stopped");
        }

        /// <summary>
        /// Binds the receive socket resiliently. Two guards make a leaked or duplicate
        /// socket unable to block reception, so it self-heals across Editor Play restarts
        /// instead of needing a manual process kill:
        ///   • closes any socket a previous run in THIS process left bound, and
        ///   • sets ReuseAddress so a socket in ANOTHER process (a zombie Editor) cannot
        ///     hold the port exclusively — on Windows the most-recent binder receives the
        ///     datagrams.
        /// </summary>
        private UdpClient CreateBoundSocket()
        {
            lock (s_socketLock)
            {
                try { s_lastBoundUdp?.Close(); } catch { }
                s_lastBoundUdp = null;

                var udp = new UdpClient();
                udp.ExclusiveAddressUse = false;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Parse(_bindAddress), _port));
                s_lastBoundUdp = udp;
                return udp;
            }
        }

        private void ResetDiagnostics()
        {
            ParseFailureCount = 0;
            ReconnectCount = 0;
            AckSentCount = 0;
            AckSendFailureCount = 0;
            AcceptedSampleCount = 0;
            LastPacketRealtime = -1;
            LastHeartbeatRealtime = -1;
            LastAcceptedSampleRealtime = -1;
            LastSenderIp = "-";
            LastSenderPort = -1;
            LastSequence = -1;
            LastAckSequence = -1;
            lock (_lock) _queue.Clear();
        }

        private void ReceiveLoop()
        {
            int backoffMs = 500;
            while (!_stop)
            {
                try
                {
                    _udp = CreateBoundSocket();
                    SetStatus("listening");
                    backoffMs = 500;
                    _bindWarningLogged = false;

                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    while (!_stop)
                    {
                        byte[] data = _udp.Receive(ref remote);
                        var payload = new QueuedPayload
                        {
                            Json = Encoding.UTF8.GetString(data),
                            ReceivedAtUtcIso = UtcTime.NowIso(),
                            RealtimeAtReceive = _realtimeBase + _stopwatch.Elapsed.TotalSeconds,
                            RemoteAddress = remote.Address.ToString(),
                            RemotePort = remote.Port
                        };
                        lock (_lock)
                        {
                            _queue.Enqueue(payload);
                            while (_queue.Count > 64) _queue.Dequeue();
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (_stop) break;
                    bool inUse = ex.SocketErrorCode == SocketError.AddressAlreadyInUse;
                    SetStatus(inUse ? "port_in_use" : "receive_error");
                    if (inUse && !_bindWarningLogged)
                    {
                        _bindWarningLogged = true;
                        Debug.LogWarning($"[HR] UDP port {_port} zauzet — vjerovatno zaostala Unity " +
                            "instanca drži soket. Prijemnik koristi ReuseAddress i pokušava ponovo; " +
                            "ako i dalje nema prijema, zatvori druge Unity procese.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (_stop) break;
                }
                catch (Exception)
                {
                    if (_stop) break;
                    SetStatus("receive_error");
                }

                if (_stop) break;
                try { _udp?.Close(); } catch { }
                Thread.Sleep(backoffMs);
                backoffMs = Math.Min(backoffMs * 2, 5000);
                ReconnectCount++;
                SetStatus("reconnected");
            }
        }

        private void SetStatus(string status)
        {
            ListenerStatus = status;
            StatusChanged?.Invoke(status);
        }

        public int DrainSamples(List<RawHrSample> buffer)
        {
            List<QueuedPayload> local;
            lock (_lock)
            {
                if (_queue.Count == 0) return 0;
                local = new List<QueuedPayload>(_queue);
                _queue.Clear();
            }

            int added = 0;
            foreach (var q in local)
            {
                LastPacketRealtime = q.RealtimeAtReceive;
                LastSenderIp = string.IsNullOrEmpty(q.RemoteAddress) ? "-" : q.RemoteAddress;
                LastSenderPort = q.RemotePort;

                var parsed = HrPacketParser.Parse(q.Json, _minBpm, _maxBpm);
                if (!parsed.Ok)
                {
                    ParseFailureCount++;
                    continue;
                }

                LastSequence = parsed.Sequence;
                var decision = _ingest.Evaluate(in parsed, out _);
                if (HrRelayAckProtocol.ShouldSendAcceptedAck(in parsed, decision))
                    SendAcceptedAck(q.RemoteAddress, q.RemotePort, parsed.SessionToken, parsed.Sequence);

                if (decision == HrRelayIngest.Decision.Heartbeat)
                {
                    LastHeartbeatRealtime = q.RealtimeAtReceive;
                    continue;
                }
                if (decision == HrRelayIngest.Decision.Reject)
                    continue;

                double ageMs = -1;
                if (!parsed.IsLegacy &&
                    UtcTime.TryParseIso(parsed.SourceTimestampUtcIso, out var srcUtc) &&
                    UtcTime.TryParseIso(q.ReceivedAtUtcIso, out var recvUtc))
                {
                    ageMs = (recvUtc - srcUtc).TotalMilliseconds;
                }

                buffer.Add(new RawHrSample
                {
                    Bpm = parsed.Bpm,
                    ReceivedAtUtcIso = q.ReceivedAtUtcIso,
                    RealtimeAtReceive = q.RealtimeAtReceive,
                    SourceTimestampUtcIso = parsed.SourceTimestampUtcIso,
                    Sequence = parsed.Sequence,
                    IsLegacyPacket = parsed.IsLegacy,
                    SignalAgeMs = ageMs,
                    ProtocolVersion = parsed.ProtocolVersion,
                    Source = parsed.Source,
                    HasSessionToken = !string.IsNullOrWhiteSpace(parsed.SessionToken)
                });
                LastAcceptedSampleRealtime = q.RealtimeAtReceive;
                AcceptedSampleCount++;
                added++;
            }
            return added;
        }

        private void SendAcceptedAck(string remoteAddress, int remotePort,
            string sessionToken, int sequence)
        {
            if (_ackUdp == null || remotePort <= 0 ||
                !IPAddress.TryParse(remoteAddress, out var address))
            {
                AckSendFailureCount++;
                return;
            }

            try
            {
                string json = HrRelayAckProtocol.BuildAccepted(sessionToken, sequence);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                _ackUdp.Send(bytes, bytes.Length, new IPEndPoint(address, remotePort));
                LastAckSequence = sequence;
                AckSentCount++;
            }
            catch
            {
                AckSendFailureCount++;
            }
        }

        /// <summary>Developer-only formatted transport diagnostics.</summary>
        public string BuildDeveloperDiagnostics(double nowRealtime)
        {
            string packetAge = FormatAge(nowRealtime, LastPacketRealtime);
            string sampleAge = FormatAge(nowRealtime, LastAcceptedSampleRealtime);
            string sender = LastSenderPort > 0 ? LastSenderIp + ":" + LastSenderPort : LastSenderIp;
            string token = string.IsNullOrEmpty(BoundSessionToken) ? "-" : BoundSessionToken;
            return "UDP " + ListenerStatus + " · bind " + BindEndpoint + "\n" +
                   "sender " + sender + " · packet " + packetAge + " · HR " + sampleAge + "\n" +
                   "seq " + LastSequence + " · token " + token + "\n" +
                   "hb " + HeartbeatCount + " · accepted " + AcceptedSampleCount +
                   " · parse " + ParseFailureCount + "\n" +
                   "wrong-token " + WrongTokenCount + " · dup/order " +
                   DuplicateOrOutOfOrderCount + " · stale-watch " + StaleWatchCount +
                   " · ACK " + AckSentCount;
        }

        private static string FormatAge(double nowRealtime, double thenRealtime)
        {
            if (thenRealtime < 0) return "-";
            return Math.Max(0, nowRealtime - thenRealtime).ToString("0.0") + "s";
        }
    }
}
