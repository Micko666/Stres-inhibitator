package me.djurovic.hrrelay;

/**
 * One accepted heart-rate sample parsed from a Mi Fitness logcat line.
 * PURE JAVA (no Android imports) so it compiles and runs in a plain JVM for
 * unit testing (android_companion/tools/HrItemParserTest.java).
 *
 * watchUnixSeconds is the sensor timestamp reported by the watch inside
 * HrItem(time=...). It is the ONLY monotonic ordering key we trust — phone
 * wall-clock is not; see STANDALONE_HR_ARCHITECTURE.md.
 */
public final class HrSample {
    public final int bpm;
    public final long watchUnixSeconds;
    public final String rawSource; // e.g. "HrItem(time)"

    public HrSample(int bpm, long watchUnixSeconds, String rawSource) {
        this.bpm = bpm;
        this.watchUnixSeconds = watchUnixSeconds;
        this.rawSource = rawSource;
    }

    @Override
    public String toString() {
        return "HrSample{bpm=" + bpm + ", watchTs=" + watchUnixSeconds
                + ", src=" + rawSource + "}";
    }
}
