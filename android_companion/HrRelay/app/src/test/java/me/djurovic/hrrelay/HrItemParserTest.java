package me.djurovic.hrrelay;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.List;

/**
 * JUnit local unit test (runs via `gradlew test`, no device). Mirrors the pure
 * JVM harness in android_companion/tools/HrItemParserTest.java against the real
 * captured Mi Fitness line.
 */
public class HrItemParserTest {

    private static final String REAL_LINE =
        "07-14 12:39:14.896 I/fitness_19864_18807(19864): [HomeDataRepository] "
        + "srcDataMap = {single_heart_rate=[HrItem(sid=870717940, time=1784025553, hr=85), "
        + "HrItem(sid=870717940, time=1784025551, hr=84), HrItem(sid=870717940, time=1784025550, hr=85), "
        + "HrItem(sid=870717940, time=1784025546, hr=83)]}";

    @Test public void acceptsRealNewestSample() {
        HrItemParser p = new HrItemParser();
        HrSample s = p.parseAndAccept(REAL_LINE);
        assertNotNull(s);
        assertEquals(85, s.bpm);
        assertEquals(1784025553L, s.watchUnixSeconds);
    }

    @Test public void rejectsDuplicateSecondTime() {
        HrItemParser p = new HrItemParser();
        assertNotNull(p.parseAndAccept(REAL_LINE));
        assertNull(p.parseAndAccept(REAL_LINE));
    }

    @Test public void rejectsOlderAfterNewer() {
        HrItemParser p = new HrItemParser();
        assertNotNull(p.parseAndAccept("HrItem(sid=1, time=1784025560, hr=90)"));
        assertNull(p.parseAndAccept("HrItem(sid=1, time=1784025500, hr=70)"));
    }

    @Test public void rejectsImplausibleBpm() {
        HrItemParser p = new HrItemParser();
        assertNull(p.parseAndAccept("HrItem(sid=1, time=1784030000, hr=500)"));
        assertFalse(HrItemParser.isPlausible(29));
        assertTrue(HrItemParser.isPlausible(30));
        assertTrue(HrItemParser.isPlausible(220));
    }

    @Test public void normalisesMillisTimestamp() {
        HrItemParser p = new HrItemParser();
        HrSample s = p.parseAndAccept("HrItem(sid=1, time=1784025553000, hr=88)");
        assertNotNull(s);
        assertEquals(1784025553L, s.watchUnixSeconds);
    }

    @Test public void relayEnvelopeMatchesUnitySchema() {
        String pkt = RelayPacketBuilder.sample("paired-xyz", 7, 85, 1784025553L);
        assertTrue(pkt.contains("\"protocolVersion\":1"));
        assertTrue(pkt.contains("\"source\":\"phone-relay\""));
        assertTrue(pkt.contains("\"sessionToken\":\"paired-xyz\""));
        assertTrue(pkt.contains("\"kind\":\"sample\""));
        String hb = RelayPacketBuilder.heartbeat("paired-xyz", 8);
        assertTrue(hb.contains("\"kind\":\"heartbeat\""));
        assertTrue(hb.contains("\"bpm\":0"));
    }

    @Test public void extractItemsFindsAll() {
        List<HrSample> all = HrItemParser.extractItems(REAL_LINE);
        assertEquals(4, all.size());
    }
}
