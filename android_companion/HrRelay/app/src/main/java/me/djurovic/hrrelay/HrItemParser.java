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
 * <h3>Value scale (2026-08-23)</h3>
 * Mi Fitness does NOT always report hr= in plain bpm. Captured evidence:
 * <ul>
 *   <li>2026-07-14 — {@code single_heart_rate=[HrItem(..., hr=85), ...]} — plain bpm.</li>
 *   <li>2026-08-23 — the SAME field emits {@code hr=425} while the band displays
 *       83 bpm: values 390..580, 100% divisible by 5, i.e. bpm x5.</li>
 * </ul>
 * A fixed 30..220 filter therefore silently discarded ~100% of live samples and
 * the relay looked "connected but silent". Rather than hard-coding a divisor
 * (which would break the older format and any future change), the scale is
 * DETECTED per line: we pick the interpretation that makes the most samples
 * plausible. A stray value cannot flip the decision, and the choice is exposed
 * via {@link #scale()} so the UI can show it instead of failing silently.
 *
 * Statefulness: instances remember the highest watch timestamp accepted so far
 * and only accept strictly-newer HrItems (deduplicates the overlapping windows
 * Mi Fitness prints on every refresh). Construct one instance per relay session
 * and call {@link #reset()} on reconnect.
 */
public final class HrItemParser {

    /** Plausible human range, applied to the DECODED bpm (matches Python valid_hr). */
    public static final int MIN_BPM = 30;
    public static final int MAX_BPM = 220;

    /** Divisor of the scaled Mi Fitness format (hr = bpm * 5). */
    public static final int SCALE_DIVISOR = 5;

    /** How the raw hr= field maps to bpm. */
    public enum Scale {
        /** hr= is already bpm (format proven 2026-07-14). */
        PLAIN,
        /** hr= is bpm * 5 (format observed 2026-08-23). */
        SCALED_X5
    }

    /**
     * Minimum raw values in one line before its scale verdict is allowed to LOCK
     * the parser. Mi Fitness prints large batches, so this is reached immediately;
     * it only guards against locking on a single stray value.
     */
    private static final int MIN_SAMPLES_TO_LOCK = 3;

    // time= ... hr= ...   (hr is 2..4 digits: 220 plain, up to 1100 scaled)
    private static final Pattern HR_ITEM_TS =
            Pattern.compile("HrItem\\([^)]*?time\\s*=\\s*(\\d{10,13})[^)]*?hr\\s*=\\s*(\\d{2,4})(?!\\d)[^)]*?\\)",
                    Pattern.CASE_INSENSITIVE);
    // hr= ... time= ...  (field order reversed in some builds)
    private static final Pattern HR_ITEM_TS_REVERSE =
            Pattern.compile("HrItem\\([^)]*?hr\\s*=\\s*(\\d{2,4})(?!\\d)[^)]*?time\\s*=\\s*(\\d{10,13})[^)]*?\\)",
                    Pattern.CASE_INSENSITIVE);

    // 10^10: unix seconds are 10 digits (~1.7e9), millis are 13 (~1.7e12).
    private static final long MILLIS_THRESHOLD = 10_000_000_000L;

    private long highestWatchTs = 0L;
    private Scale lockedScale;              // null until a confident line locks it
    private int rejectedImplausible;

    /** Reset dedup + scale state (use on relay reconnect / new pairing session). */
    public void reset() {
        highestWatchTs = 0L;
        lockedScale = null;
        rejectedImplausible = 0;
    }

    public long highestWatchTs() {
        return highestWatchTs;
    }

    /** Scale locked for this session, or null while still undetermined. */
    public Scale scale() {
        return lockedScale;
    }

    /** Raw values seen that were implausible under the chosen scale. */
    public int rejectedImplausible() {
        return rejectedImplausible;
    }

    /** Short human label for the relay UI. */
    public String scaleLabel() {
        if (lockedScale == null) return "nepoznata";
        return lockedScale == Scale.SCALED_X5 ? "bpm x5 (dijeli se sa 5)" : "bpm";
    }

    /** One raw hr= occurrence, before scale is applied. */
    private static final class Raw {
        final long ts;
        final int value;
        Raw(long ts, int value) { this.ts = ts; this.value = value; }
    }

    private static List<Raw> extractRaw(String line) {
        List<Raw> out = new ArrayList<Raw>();
        if (line == null || line.isEmpty()) return out;

        Matcher m = HR_ITEM_TS.matcher(line);
        while (m.find()) {
            out.add(new Raw(normalizeTs(Long.parseLong(m.group(1))), Integer.parseInt(m.group(2))));
        }
        Matcher r = HR_ITEM_TS_REVERSE.matcher(line);
        while (r.find()) {
            out.add(new Raw(normalizeTs(Long.parseLong(r.group(2))), Integer.parseInt(r.group(1))));
        }
        return out;
    }

    /**
     * Picks the scale that yields the most plausible bpm values. Ties go to PLAIN
     * so the historically proven format stays the default. Returns null when the
     * evidence supports neither (nothing plausible either way).
     */
    static Scale detectScale(List<Integer> rawValues) {
        int plain = 0, scaled = 0;
        for (int v : rawValues) {
            if (isPlausible(v)) plain++;
            if (v % SCALE_DIVISOR == 0 && isPlausible(v / SCALE_DIVISOR)) scaled++;
        }
        if (plain == 0 && scaled == 0) return null;
        return scaled > plain ? Scale.SCALED_X5 : Scale.PLAIN;
    }

    private static int decode(int raw, Scale scale) {
        return scale == Scale.SCALED_X5 ? raw / SCALE_DIVISOR : raw;
    }

    /**
     * Returns EVERY valid (ts, bpm) HrItem found in the line, order as found.
     * Scale is detected from this line alone — static and self-contained, so a
     * single captured line can still be parsed correctly in tests.
     */
    public static List<HrSample> extractItems(String line) {
        List<Raw> raws = extractRaw(line);
        List<Integer> values = new ArrayList<Integer>(raws.size());
        for (Raw raw : raws) values.add(raw.value);
        Scale scale = detectScale(values);
        return decodeAll(raws, scale == null ? Scale.PLAIN : scale, null);
    }

    private static List<HrSample> decodeAll(List<Raw> raws, Scale scale, int[] rejectedOut) {
        List<HrSample> items = new ArrayList<HrSample>();
        for (Raw raw : raws) {
            int bpm = decode(raw.value, scale);
            if (isPlausible(bpm)) {
                items.add(new HrSample(bpm, raw.ts, "HrItem(time)"));
            } else if (rejectedOut != null) {
                rejectedOut[0]++;
            }
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
        List<Raw> raws = extractRaw(line);
        if (raws.isEmpty()) return null;

        List<Integer> values = new ArrayList<Integer>(raws.size());
        for (Raw raw : raws) values.add(raw.value);

        Scale lineScale = detectScale(values);
        if (lineScale != null && raws.size() >= MIN_SAMPLES_TO_LOCK) {
            lockedScale = lineScale;            // batch large enough to trust
        }
        Scale scale = lockedScale != null ? lockedScale
                : (lineScale != null ? lineScale : Scale.PLAIN);

        int[] rejected = new int[1];
        List<HrSample> items = decodeAll(raws, scale, rejected);
        rejectedImplausible += rejected[0];
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
