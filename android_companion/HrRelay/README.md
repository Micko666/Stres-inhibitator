# HR Relej (VR) — Android companion

Namjenska aplikacija koja prosljeđuje stvarni puls sa Xiaomi Smart Band 9 (preko
Mi Fitness logcat-a) na standalone Meta Quest 3, lokalnom UDP vezom. Dio diplomskog
VR stress-training sistema. Puna arhitektura: `../../VR_StressTraining/STANDALONE_HR_ARCHITECTURE.md`.

## Kako radi

```
Band 9 → Mi Fitness → HrItem u logcat-u → [HR Relej: čita logcat, parsira, šalje UDP] → Quest
```

- Čita logcat lokalno na telefonu (traži `READ_LOGS`, grant jednom preko ADB-a).
- Parsira `HrItem(time=...,hr=...)` (`HrItemParser`, port dokazanog Python parsera).
- Šalje protokol-v1 UDP pakete (`RelayPacketBuilder`) unicast na Quest IP:5005.
- Foreground service + wakelock: radi sa ugašenim ekranom; heartbeat svakih 5 s.
- Bez cloud-a, bez naloga, bez čuvanja HR istorije.

## Build

```
cp local.properties.example local.properties     # koristi FORWARD slashes za sdk.dir!
gradlew.bat assembleDebug                          # ili: gradlew assembleDebug
# APK: app/build/outputs/apk/debug/app-debug.apk
```

Testovi (bez uređaja): `gradlew.bat test` (JUnit) ili pure-JVM harness:
`../tools` → `javac`+`java` protiv stvarnog snimljenog reda.

## Instalacija i READ_LOGS grant (jednom)

```
adb install -r app-debug.apk
adb shell pm grant me.djurovic.hrrelay android.permission.READ_LOGS
```

Detaljno: `../../VR_StressTraining/STANDALONE_HR_SETUP.md`.

## Struktura

| Fajl | Uloga | Čist JVM? |
|---|---|---|
| `HrItemParser.java` | parsira HrItem + dedup po watch-ts | da |
| `HrSample.java` | jedan uzorak | da |
| `RelayPacketBuilder.java` | protokol-v1 JSON envelope | da |
| `HrRelaySender.java` | UDP unicast + sequence | da (java.net) |
| `LogcatHrReader.java` | spawn `logcat`, feed parser | Android |
| `HrRelayService.java` | foreground service, wakelock, heartbeat | Android |
| `MainActivity.java` | minimalni UI | Android |
| `HrRelayState.java` | dijeljeno stanje UI↔servis | Android |

Poznata ograničenja i šta NIJE verifikovano na uređaju:
`../../VR_StressTraining/STANDALONE_HR_KNOWN_LIMITATIONS.md`.
