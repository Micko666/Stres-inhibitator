# STANDALONE_HR_KNOWN_LIMITATIONS.md

Iskreno razdvajanje: šta radi, šta je verifikovano, a šta NIJE.

## Verifikovano (u ovom okruženju, bez fizičkih uređaja)

- Parser band→telefon: `HrItemParser` (Java) verifikovan na STVARNOM snimljenom
  Mi Fitness redu (`HR_REAL_DEBUG_SNAPSHOT.json`) — `javac`+`java`, 24/24 PASS,
  i JUnit u Gradle projektu.
- Unity ingest gate (`HrRelayIngest`): identitet sesije, dedup, redosljed sekvence,
  monotonost watch-timestamp-a, heartbeat/uzorak — Unity Test Runner (HrRelayIngestTests).
- Unity compile: čisto. Unity EditMode: 148/148.
- Android APK build: vidi STANDALONE_HR_TEST_MATRIX.md (status upisan poslije build-a).

## NIJE verifikovano (traži fizičke uređaje — NE tvrditi da radi)

- **real band acquisition** — da HR Relej na stvarnom Samsung telefonu zaista pročita
  Band 9 HrItem preko logcat-a poslije READ_LOGS granta.
- **phone→Quest na uređaju** — da Quest primi i otključa baseline sa stvarnim pulsom.
- **screen-lock** — da relej radi sa ugašenim ekranom (foreground service + wakelock
  su tu, ali OEM battery killer na Samsung-u može ubiti servis; testirati + isključiti
  battery optimization).
- **hotspot** — rad preko telefonskog hotspota (UDP unicast; provjeriti da hotspot ne
  izoluje klijente / AP isolation).
- **60-min stabilnost** — dug rad bez curenja/gubitka veze.

## Trajna ograničenja / rizici

- **READ_LOGS** — jedina koncesija „računar jednom": grant preko ADB-a pri instalaciji.
  Traje do reinstalacije. Android 12+ on-device „log access" dijalog je OEM-zavisan i
  NIJE potvrđen na ciljanom telefonu.
- **Zavisnost od Mi Fitness-a** — HR dolazi samo dok Mi Fitness aktivno mjeri i loguje
  HrItem. Ako Mi Fitness stane, nema uzoraka (Quest freshness gate to pošteno prikaže).
- **Sigurnost logcat-a** — logcat sadrži i druge sistemske logove; HR Relej čita samo
  linije sa „HrItem" i ne čuva istoriju niti ih šalje ikuda osim UDP-om na Quest. Koristiti
  namjenski studijski telefon; ne logovati sadržaj.
- **Bez enkripcije transporta** — UDP na lokalnoj mreži je čist tekst. Prihvatljivo za
  lokalnu studijsku mrežu/hotspot; ne slati preko javne mreže.
- **Simulated HR** — dozvoljen samo u izolovanim testovima; nikad ne otključava produkciju
  (gate u `HeartRateService.GetProductionReadiness`).

## Ako se pokaže da band→telefon ne radi na uređaju

Redosljed rezervi (bez lažnog pipeline-a, bez simuliranog HR-a kao zamjene):
1. Provjeri READ_LOGS grant i Mi Fitness kontinuirano mjerenje.
2. Dev fallback: `hr_dashboard_v2.py` (računar) — dokazan, ostaje netaknut.
3. Tek kao krajnja opcija razmotriti direktni BLE (Band 9 auth ključ) — veliki napor,
   vidi STANDALONE_HR_ARCHITECTURE.md §2 (alternativa B).
