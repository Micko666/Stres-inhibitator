import me.djurovic.hrrelay.HrItemParser;
import me.djurovic.hrrelay.HrSample;
import me.djurovic.hrrelay.RelayPacketBuilder;

import java.util.List;

/**
 * PURE-JVM verification of the band->phone parsing core. No Android, no Gradle,
 * no JUnit — compile with the pure-Java sources and run with `java`.
 *
 *   cd android_companion
 *   javac -d out HrRelay/app/src/main/java/me/djurovic/hrrelay/HrSample.java \
 *                HrRelay/app/src/main/java/me/djurovic/hrrelay/HrItemParser.java \
 *                HrRelay/app/src/main/java/me/djurovic/hrrelay/RelayPacketBuilder.java \
 *                tools/HrItemParserTest.java
 *   java -cp out HrItemParserTest
 *
 * The primary input is the REAL captured line from HR_REAL_DEBUG_SNAPSHOT.json.
 */
public final class HrItemParserTest {

    // Verbatim slice of the real Mi Fitness log line (Xiaomi Smart Band 9).
    private static final String REAL_LINE =
        "07-14 12:39:14.896 I/fitness_19864_18807(19864): [HomeDataRepository] "
        + "srcDataMap = {single_heart_rate=[HrItem(sid=870717940, time=1784025553, hr=85), "
        + "HrItem(sid=870717940, time=1784025551, hr=84), HrItem(sid=870717940, time=1784025550, hr=85), "
        + "HrItem(sid=870717940, time=1784025549, hr=84), HrItem(sid=870717940, time=1784025546, hr=83), "
        + "HrItem(sid=870717940, time=1784025544, hr=82), HrItem(sid=870717940, time=1784025542, hr=83)]}";

    private static int failures = 0;

    public static void main(String[] args) {
        // 1) Real line -> newest sample is bpm=85 at ts=1784025553.
        HrItemParser parser = new HrItemParser();
        HrSample s = parser.parseAndAccept(REAL_LINE);
        check("real line yields a sample", s != null);
        if (s != null) {
            check("real line newest bpm == 85 (max ts wins)", s.bpm == 85);
            check("real line newest watchTs == 1784025553", s.watchUnixSeconds == 1784025553L);
        }

        // 2) Same line again -> duplicate rejected (no newer watch ts).
        check("re-parsing identical line is rejected as duplicate",
                parser.parseAndAccept(REAL_LINE) == null);

        // 3) A strictly newer HrItem is accepted; an older one after it is not.
        check("newer HrItem accepted",
                parser.parseAndAccept("HrItem(sid=1, time=1784025560, hr=90)") != null);
        check("older HrItem rejected after newer",
                parser.parseAndAccept("HrItem(sid=1, time=1784025500, hr=70)") == null);

        // 4) Implausible BPM rejected entirely.
        HrItemParser p2 = new HrItemParser();
        check("bpm 500 rejected (out of range, no items)",
                p2.parseAndAccept("HrItem(sid=1, time=1784030000, hr=500)") == null);
        check("no-HR line rejected",
                p2.parseAndAccept("07-14 12:00:00 I/whatever: nothing here") == null);

        // 5) Plausible-range boundaries.
        check("30 is plausible", HrItemParser.isPlausible(30));
        check("220 is plausible", HrItemParser.isPlausible(220));
        check("29 is NOT plausible", !HrItemParser.isPlausible(29));
        // 221 has 3 digits so the regex could see it; ensure range guard drops it.
        HrItemParser p3 = new HrItemParser();
        check("221 rejected by range",
                p3.parseAndAccept("HrItem(sid=1, time=1784030001, hr=221)") == null);

        // 6) Millisecond timestamps are normalised to seconds.
        HrItemParser p4 = new HrItemParser();
        HrSample ms = p4.parseAndAccept("HrItem(sid=1, time=1784025553000, hr=88)");
        check("ms timestamp accepted", ms != null);
        if (ms != null) check("ms timestamp normalised to seconds", ms.watchUnixSeconds == 1784025553L);

        // 7) Relay envelope shape (must match Unity RelayPacket).
        String pkt = RelayPacketBuilder.sample("paired-xyz", 7, 85, 1784025553L);
        check("packet has protocolVersion:1", pkt.contains("\"protocolVersion\":1"));
        check("packet has source phone-relay", pkt.contains("\"source\":\"phone-relay\""));
        check("packet carries token", pkt.contains("\"sessionToken\":\"paired-xyz\""));
        check("packet carries sequence", pkt.contains("\"sequence\":7"));
        check("packet carries bpm", pkt.contains("\"bpm\":85"));
        check("packet kind sample", pkt.contains("\"kind\":\"sample\""));
        check("packet iso timestamp", pkt.contains("\"timestampUtc\":\"2026-"));

        // 8) Heartbeat envelope: bpm 0, kind heartbeat.
        String hb = RelayPacketBuilder.heartbeat("paired-xyz", 8);
        check("heartbeat bpm 0", hb.contains("\"bpm\":0"));
        check("heartbeat kind heartbeat", hb.contains("\"kind\":\"heartbeat\""));

        // 9) extractItems finds every HrItem in the real line.
        List<HrSample> all = HrItemParser.extractItems(REAL_LINE);
        check("extractItems found all 7 HrItems", all.size() == 7);

        System.out.println();
        if (failures == 0) {
            System.out.println("RESULT: ALL PASSED");
        } else {
            System.out.println("RESULT: " + failures + " FAILED");
            System.exit(1);
        }
    }

    private static void check(String name, boolean ok) {
        System.out.println((ok ? "  PASS  " : "  FAIL  ") + name);
        if (!ok) failures++;
    }
}
