package me.djurovic.hrrelay;

/** Pure-Java status wording shared by the Android UI and unit tests. */
public final class RelayStatusText {
    public static final String NO_CONFIRMATION = "Šalje, nema potvrde prijema";

    private RelayStatusText() {}

    public static String confirmation(boolean configured, boolean ackFresh,
                                      String destinationName) {
        if (!configured) return "nema potvrde";
        if (!ackFresh) return NO_CONFIRMATION;
        String name = destinationName == null || destinationName.trim().isEmpty()
                ? "Unity/Quest" : destinationName.trim();
        return name + " potvrđen";
    }
}
