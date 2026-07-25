# STANDALONE_HR_TEST_MATRIX.md

Statusi razdvojeni po nivou dokaza. „VERIFIKOVANO" = stvarno izvršeno u ovoj sesiji.
„NIJE" = traži fizičke uređaje i NE smije se tvrditi bez device testa.

## Automatski / build (VERIFIKOVANO 2026-07-14)

| Stavka | Status | Dokaz |
|---|---|---|
| feasibility band→telefon | **VERIFIKOVANO** | realni HrItem u `HR_REAL_DEBUG_SNAPSHOT.json` + parser na njemu |
| parser (pure JVM, realni podatak) | **PASS 24/24** | `javac`+`java` `android_companion/tools/HrItemParserTest.java` |
| Android companion JUnit | **PASS 7/7** | `gradlew test` → `TEST-me.djurovic.hrrelay.HrItemParserTest.xml` |
| **Android APK build** | **PASS** | `assembleDebug` → `app/build/outputs/apk/debug/app-debug.apk` (~3.1 MB) |
| Unity compile | **PASS** | Coplay `check_compile_errors`: No compile errors |
| Unity EditMode testovi | **PASS 148/148** | Unity Test Runner (uklj. `HrRelayIngestTests`, `HeartRateServiceTests`) |
| ingest: Simulated ne otključava produkciju | **PASS** | `ProductionGate_RejectsDisconnectedSimulatedAndNativePlaceholder` |
| ingest: Disconnected/placeholder ne otključava | **PASS** | isti + wrist watch testovi |
| ingest: heartbeat nije uzorak | **PASS** | `HeartbeatParsesWithoutBpmAndIsNeverASample` |
| ingest: duplikat/van-redosljeda odbačen | **PASS** | `DuplicateSequenceIsRejected`, `OutOfOrderSequenceIsRejected` |
| ingest: star watch-timestamp odbačen | **PASS** | `StaleWatchTimestampIsRejectedEvenWhenSequenceAdvances` |
| ingest: pogrešan token odbačen | **PASS** | `FirstTokenBindsAndDifferentTokenIsRejected` |
| ingest: restart pošiljaoca (seq 0) | **PASS** | `SenderRestartAtSequenceZeroRecovers` |
| ingest: python bridge bez tokena prolazi | **PASS** | `PythonBridgePacketWithoutTokenStillFlowsThroughIngest` |
| freshness gate zatvori baseline kad uzorak zastari | **PASS** | `ProductionGate_ClosesWhenLastGoodNetworkSampleBecomesLocallyStale` |
| shutdown oslobađa socket/thread | **PASS** | `Shutdown_StopsEverySource_AndIsIdempotent` |
| ponovljeni bootstrap ne pravi drugi watch | **PASS** | `RepeatedBootstrap_ReusesExistingWatch` |

## Na uređaju (2026-07-14, Samsung SM-S936B + Band 9)

| Stavka | Status | Dokaz / kako testirati |
|---|---|---|
| **real band acquisition** | **VERIFIKOVANO** | HR Relej na telefonu: „logcat: čita", „PULS: 85 bpm · star 0.5 s" (svjež real-time uzorak); HrItem linije žive u logcat-u (77–115). READ_LOGS radi i za work-profil (user 150) Mi Fitness. |
| **phone→Quest na uređaju** | **VERIFIKOVANO** | „QUEST: povezan → 192.168.1.217:5005", „Poslato paketa (seq): 218" (raste); virtuelni sat na Questu = „Povezan · Stabilno" (prima svježe uzorke). |
| **APK install + READ_LOGS grant na telefonu** | **VERIFIKOVANO** | `adb install` Success; `pm grant ... READ_LOGS` granted=true (user 0). |
| screen-lock | **NIJE** | Ugasi ekran telefona 10+ min; provjeri „seq" i dalje raste (isključi battery optimization za HR Relej). |
| hotspot | **NIJE** | Quest na telefonskom hotspotu; provjeri da nema AP-isolation. |
| telefon promijeni IP / Wi-Fi kratko padne | **NIJE** | Prebaci mrežu; UDP/logcat reconnect. |
| 60-min stabilnost | **NIJE** | Cijela sesija bez gubitka veze / curenja. |
| pogrešan Quest ne prima | delimično logički | unicast na izabrani IP; token gate na Questu. |

> Napomena: baseline zona „Stabilno" je očekivana dok baseline faza ne postavi referencu;
> tokom sesije zone (Povišeno/Visoko) postaju relativne u odnosu na izmjereni baseline.

## Procedura device testa (kad budu uređaji)

1. `adb install -r app-debug.apk` → `adb shell pm grant me.djurovic.hrrelay android.permission.READ_LOGS`
2. Mi Fitness → Heart rate (kontinuirano).
3. HR Relej → Quest IP → Poveži. Provjeri „PULS" i „Poslato paketa (seq)" raste.
4. Na Questu: produkcijska sesija → baseline gate se otvara samo sa stvarnim, svježim pulsom.
5. Ponovi uz: ugašen ekran; hotspot; 60 min. Upiši rezultate ovdje.
