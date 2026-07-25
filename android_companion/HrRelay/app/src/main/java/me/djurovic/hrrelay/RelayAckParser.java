package me.djurovic.hrrelay;

import java.nio.charset.StandardCharsets;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Pure-Java parser for Unity's small protocol-v1 UDP ACK envelope. */
public final class RelayAckParser {
    public static final String STATUS_ACCEPTED = "accepted";

    private static final Pattern VERSION = Pattern.compile("\\\"protocolVersion\\\"\\s*:\\s*(-?\\d+)");
    private static final Pattern TOKEN = Pattern.compile("\\\"sessionToken\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
    private static final Pattern SEQUENCE = Pattern.compile("\\\"sequence\\\"\\s*:\\s*(-?\\d+)");
    private static final Pattern STATUS = Pattern.compile("\\\"status\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");

    private RelayAckParser() {}

    public static RelayAck parse(byte[] bytes, int length) {
        if (bytes == null || length <= 0 || length > bytes.length) return null;
        return parse(new String(bytes, 0, length, StandardCharsets.UTF_8));
    }

    public static RelayAck parse(String json) {
        if (json == null || json.trim().isEmpty()) return null;
        Integer version = integer(VERSION, json);
        Integer sequence = integer(SEQUENCE, json);
        String token = string(TOKEN, json);
        String status = string(STATUS, json);
        if (version == null || sequence == null || token == null || status == null) return null;
        if (version != RelayPacketBuilder.PROTOCOL_VERSION || sequence < 0 || token.isEmpty()) return null;
        return new RelayAck(version, token, sequence, status);
    }

    private static Integer integer(Pattern pattern, String text) {
        Matcher m = pattern.matcher(text);
        if (!m.find()) return null;
        try { return Integer.parseInt(m.group(1)); }
        catch (NumberFormatException e) { return null; }
    }

    private static String string(Pattern pattern, String text) {
        Matcher m = pattern.matcher(text);
        if (!m.find()) return null;
        return unescape(m.group(1));
    }

    private static String unescape(String value) {
        StringBuilder out = new StringBuilder(value.length());
        boolean escaped = false;
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (escaped) { out.append(c); escaped = false; }
            else if (c == '\\') escaped = true;
            else out.append(c);
        }
        if (escaped) out.append('\\');
        return out.toString();
    }
}
