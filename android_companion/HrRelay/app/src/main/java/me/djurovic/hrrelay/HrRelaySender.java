package me.djurovic.hrrelay;

import android.util.Log;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.SocketException;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;

/**
 * Sends protocol-v1 samples/heartbeats to one configured endpoint and listens
 * for protocol-v1 ACK packets on the same UDP source port. A configured socket
 * means only "sending"; delivery is confirmed separately by a fresh ACK.
 */
public final class HrRelaySender {

    public interface AckListener { void onAck(RelayAck ack); }

    private static final String TAG = "HrRelay/Sender";
    private static final int ACK_BUFFER_BYTES = 512;
    private static final int ACK_RECEIVE_TIMEOUT_MS = 1000;

    private final String sessionToken;
    private final AckListener ackListener;
    private volatile InetAddress target;
    private volatile int port;
    private volatile DatagramSocket socket;
    private volatile boolean ackListening;
    private Thread ackThread;
    private int sequence;

    public HrRelaySender(String sessionToken) {
        this(sessionToken, null);
    }

    public HrRelaySender(String sessionToken, AckListener ackListener) {
        this.sessionToken = sessionToken == null ? "" : sessionToken;
        this.ackListener = ackListener;
    }

    /** (Re)point the sender at a Unity Editor/Quest endpoint. */
    public synchronized void setTarget(String host, int port) throws Exception {
        this.target = InetAddress.getByName(host);
        this.port = port;
        if (socket == null || socket.isClosed()) {
            socket = new DatagramSocket();
            socket.setSoTimeout(ACK_RECEIVE_TIMEOUT_MS);
            startAckListenerLocked();
        }
    }

    public synchronized boolean isConfigured() {
        return target != null && socket != null && !socket.isClosed();
    }

    /** Returns the sequence used, or -1 if not configured / send failed. */
    public synchronized int sendSample(int bpm, long watchUnixSeconds) {
        String json = RelayPacketBuilder.sample(sessionToken, sequence, bpm, watchUnixSeconds);
        return send(json);
    }

    public synchronized int sendHeartbeat() {
        String json = RelayPacketBuilder.heartbeat(sessionToken, sequence);
        return send(json);
    }

    private int send(String json) {
        DatagramSocket activeSocket = socket;
        InetAddress activeTarget = target;
        if (activeTarget == null || activeSocket == null || activeSocket.isClosed()) return -1;
        try {
            byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
            activeSocket.send(new DatagramPacket(bytes, bytes.length, activeTarget, port));
            int used = sequence;
            sequence++;
            return used;
        } catch (Exception e) {
            Log.w(TAG, "UDP send failed: " + e.getMessage());
            return -1;
        }
    }

    private void startAckListenerLocked() {
        if (ackListening) return;
        ackListening = true;
        ackThread = new Thread(this::ackLoop, "HrRelayAck");
        ackThread.setDaemon(true);
        ackThread.start();
    }

    private void ackLoop() {
        byte[] buffer = new byte[ACK_BUFFER_BYTES];
        while (ackListening) {
            DatagramSocket activeSocket = socket;
            if (activeSocket == null || activeSocket.isClosed()) break;
            try {
                DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                activeSocket.receive(packet);
                InetAddress expected = target;
                if (expected != null && !expected.equals(packet.getAddress())) continue;
                RelayAck ack = RelayAckParser.parse(packet.getData(), packet.getLength());
                if (ack != null && ackListener != null) ackListener.onAck(ack);
            } catch (SocketTimeoutException ignored) {
                // Periodic wake-up so close() can stop the thread promptly.
            } catch (SocketException e) {
                if (ackListening) Log.w(TAG, "ACK socket error: " + e.getMessage());
                break;
            } catch (Exception e) {
                Log.w(TAG, "ACK receive failed: " + e.getMessage());
            }
        }
        ackListening = false;
    }

    public synchronized int localPort() {
        return socket != null && !socket.isClosed() ? socket.getLocalPort() : -1;
    }

    public synchronized void close() {
        ackListening = false;
        DatagramSocket activeSocket = socket;
        socket = null;
        target = null;
        if (activeSocket != null) {
            try { activeSocket.close(); } catch (Exception ignore) {}
        }
        if (ackThread != null) {
            ackThread.interrupt();
            ackThread = null;
        }
    }
}
