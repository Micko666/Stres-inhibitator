package me.djurovic.hrrelay;

/** Immutable protocol-v1 ACK received from Unity/Quest. */
public final class RelayAck {
    public final int protocolVersion;
    public final String sessionToken;
    public final int sequence;
    public final String status;

    public RelayAck(int protocolVersion, String sessionToken, int sequence, String status) {
        this.protocolVersion = protocolVersion;
        this.sessionToken = sessionToken == null ? "" : sessionToken;
        this.sequence = sequence;
        this.status = status == null ? "" : status;
    }
}
