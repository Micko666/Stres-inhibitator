package me.djurovic.hrrelay;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Parses Xiaomi Mi Fitness "HrItem(sid=..., time=..., hr=...)" logcat lines into
 * heart-rate samples. PURE JAVA (no Android imports) — this is the on-device
 * port of the proven Python reference (hr_dashboard_v2_AUTO_NETWORK.py,
 * extract_hr_with_ts + emit_hr dedup) and is directly unit-testable in a JVM.
 *
 * REFERENCE IMPLEMENTATION IS THE PYTHON BRIDGE. This class must accept the same
 * lines and reject the same lines. See STANDALONE_HR_TEST_MATRIX.md.
 *
 * Statefulness: instances remember the highest watch timestamp accepted so far
 * and only accept strictly-newer HrItems (deduplicates the overlapping windows
 * Mi Fitness prints on every refresh). Construct one instance per relay session
 * and call {@link #reset()} on reconnect.
 */
public final class HrItemParser {

    /** Plausible human range; values outside are discarded (matches Python valid_hr). */
    public static final int MIN_BPM = 30;
    public static final int MAX_BPM = 220;

    // time= ... hr= ...
    private static final Pattern HR_ITEM_TS =
            Pattern.compile("HrItem\\([^)]*?time\\s*=\\s*(\\d{10,13})[^)]*?hr\\s*=\\s*(\\d{2,3})[^)]*?\\)",
                    Pattern.CASE_INSENSITIVE);
    // hr= ... time= ...  (field order reversed in some builds)
    private static final Pattern HR_ITEM_TS_REVERSE =
            Pattern.compile("HrItem\\([^)]*?hr\\s*=\\s*(\\d{2,3})[^)]*?time\\s*=\\s*(\\d{10,13})[^)]*?\\)",
                    Pattern.CASE_INSENSITIVE);

    // 10^10: unix seconds are 10 digits (~1.7e9), millis are 13 (~1.7e12).
    private static final long MILLIS_THRESHOLD = 10_000_000_000L;

    private long highestWatchTs = 0L;

    /** Reset dedup state (use on relay reconnect / new pairing session). */
    public void reset() {
        highestWatchTs = 0L;
    }

    public long highestWatchTs() {
        return highestWatchTs;
    }

    /**
     * Returns EVERY valid (ts, hr) HrItem found in the line, newest first is NOT
     * guaranteed — order as found. Used by tests and by {@link #parseAndAccept}.
     * Static + stateless.
     */
    public static List<HrSample> extractItems(String line) {
        List<HrSample> items = new ArrayList<HrSample>();
        if (line == null || line.isEmpty()) return items;

        Matcher m = HR_ITEM_TS.matcher(line);
        while (m.find()) {
            long ts = normalizeTs(Long.parseLong(m.group(1)));
            int hr = Integer.parseInt(m.group(2));
            if (isPlausible(hr)) items.add(new HrSample(hr, ts, "HrItem(time)"));
        }
        Matcher r = HR_ITEM_TS_REVERSE.matcher(line);
        while (r.find()) {
            int hr = Integer.parseInt(r.group(1));
            long ts = normalizeTs(Long.parseLong(r.group(2)));
            if (isPlausible(hr)) items.add(new HrSample(hr, ts, "HrItem(time)"));
        }
        return items;
    }

    /**
     * Parses the line and, if it contains a valid HrItem strictly newer than the
     * highest one accepted so far, returns that newest sample and advances the
     * dedup watermark. Returns null when the line has no valid/newer HR data.
     * Mirrors Python emit_hr's "_highest_watch_ts" gate.
     */
    public HrSample parseAndAccept(String line) {
        List<HrSample> items = extractItems(line);
        if (items.isEmpty()) return null;

        HrSample newest = items.get(0);
        for (int i = 1; i < items.size(); i++) {
            if (items.get(i).watchUnixSeconds > newest.watchUnixSeconds) newest = items.get(i);
        }

        if (newest.watchUnixSeconds > 0 && newest.watchUnixSeconds <= highestWatchTs) {
            return null; // duplicate or older overlap window — reject
        }
        if (newest.watchUnixSeconds > 0) highestWatchTs = newest.watchUnixSeconds;
        return newest;
    }

    public static boolean isPlausible(int bpm) {
        return bpm >= MIN_BPM && bpm <= MAX_BPM;
    }

    private static long normalizeTs(long ts) {
        return ts > MILLIS_THRESHOLD ? ts / 1000L : ts;
    }
}
