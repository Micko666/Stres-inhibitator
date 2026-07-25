package me.djurovic.hrrelay;

import java.time.Instant;

/**
 * Builds the exact protocol-v1 JSON envelope that the Unity side parses
 * (HrPacketParser.RelayPacket). PURE JAVA — testable in a plain JVM.
 *
 * Envelope (must stay byte-compatible with Unity's [Serializable] RelayPacket):
 *   {"protocolVersion":1,"sessionToken":"...","sequence":N,
 *    "timestampUtc":"2026-07-14T10:39:13Z","bpm":85,
 *    "source":"phone-relay","kind":"sample"}
 *
 * kind = "sample"    -> a real HR reading; enters baseline/aggregate on Quest.
 * kind = "heartbeat" -> transport-alive only; bpm is 0 and MUST NOT be treated
 *                       as a physiological sample. Keeps the Quest's "link alive"
 *                       indicator fresh without faking HR when the band is quiet.
 */
public final class RelayPacketBuilder {

    public static final int PROTOCOL_VERSION = 1;
    public static final String SOURCE = "phone-relay";
    public static final String KIND_SAMPLE = "sample";
    public static final String KIND_HEARTBEAT = "heartbeat";

    private RelayPacketBuilder() {}

    public static String sample(String sessionToken, int sequence, int bpm, long watchUnixSeconds) {
        String ts = watchUnixSeconds > 0 ? isoUtc(watchUnixSeconds) : nowIsoUtc();
        return build(sessionToken, sequence, bpm, ts, KIND_SAMPLE);
    }

    public static String heartbeat(String sessionToken, int sequence) {
        return build(sessionToken, sequence, 0, nowIsoUtc(), KIND_HEARTBEAT);
    }

    private static String build(String sessionToken, int sequence, int bpm,
                                String timestampUtc, String kind) {
        StringBuilder sb = new StringBuilder(160);
        sb.append('{');
        sb.append("\"protocolVersion\":").append(PROTOCOL_VERSION).append(',');
        sb.append("\"sessionToken\":\"").append(escape(sessionToken)).append("\",");
        sb.append("\"sequence\":").append(sequence).append(',');
        sb.append("\"timestampUtc\":\"").append(escape(timestampUtc)).append("\",");
        sb.append("\"bpm\":").append(bpm).append(',');
        sb.append("\"source\":\"").append(SOURCE).append("\",");
        sb.append("\"kind\":\"").append(kind).append("\"");
        sb.append('}');
        return sb.toString();
    }

    public static String isoUtc(long unixSeconds) {
        // "2026-07-14T10:39:13Z" — matches Unity UtcTime ISO parsing.
        return Instant.ofEpochSecond(unixSeconds).toString();
    }

    public static String nowIsoUtc() {
        return Instant.now().toString();
    }

    private static String escape(String s) {
        if (s == null) return "";
        StringBuilder out = new StringBuilder(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '"' || c == '\\') out.append('\\').append(c);
            else if (c >= 0x20) out.append(c);
        }
        return out.toString();
    }
}
