# STANDALONE_HR_ARCHITECTURE.md — stvarni puls do standalone Quest-a

Cilj: **stvarni BPM sa Xiaomi Smart Band 9 → Android telefon → standalone Quest 3 APK**,
bez računara i bez interneta u normalnom radu. Ovaj dokument je odluka o arhitekturi
i njeno tehničko opravdanje. Statusi (šta je verifikovano, a šta ne) su na kraju.

---

## 1. Izabrana arhitektura

```
Xiaomi Smart Band 9
  → Mi Fitness (Android telefon)                     [nepromijenjeno, dokazano]
  → HrItem(...) linije u Android logcat-u             [dokazano: HR_REAL_DEBUG_SNAPSHOT.json]
  → HR Relej (custom Android app, U TELEFONU)         [novo: android_companion/HrRelay]
        · čita logcat lokalno (READ_LOGS, jednokratni ADB grant)
        · parsira HrItem (port dokazanog Python parsera)
        · šalje protokol-v1 UDP pakete (unicast na Quest)
  → Quest NetworkHeartRateSource (UDP :5005)          [postojeće + očvršćeno]
  → HeartRateService → produkcijska sesija
```

Jedina dodatna aplikacija pored Mi Fitness-a je **HR Relej**. U normalnom radu trebaju
samo Band 9 + telefon + Quest, na istoj Wi-Fi mreži ili preko telefonskog hotspota.

**Jedini izuzetak od „nikad računar":** READ_LOGS dozvola se dodjeljuje **jednom**
preko ADB-a pri instalaciji (`adb shell pm grant me.djurovic.hrrelay
android.permission.READ_LOGS`). Poslije toga računar nije potreban ni za jedno
pokretanje. Ovo je jedina praktična koncesija i jasno je označena (vidi §5 i
STANDALONE_HR_KNOWN_LIMITATIONS.md).

---

## 2. Zašto baš ovako — provjerene alternative

| Put | Band→telefon | Ocjena | Zašto (ne)izabran |
|---|---|---|---|
| **A. Mi Fitness + čitanje logcat-a na telefonu** | Mi Fitness (dokazan) | **IZABRANO** | Podatak već postoji i parsiran je dokazanim Python kodom; telefon čita **svoj** logcat lokalno — nema ADB-a preko mreže u radu, nema desktop adb-a. Cijena: jednokratni READ_LOGS grant. |
| B. Direktni BLE/GATT na Band 9 | custom BLE central | odbačeno za MVP | Traži Xiaomi auth ključ + reimplementaciju vlasničkog auth/HR protokola (ono što Gadgetbridge radi). Korisnik potvrdio da Gadgetbridge nije dao pouzdan tok za ovaj uređaj. Uz to, BLE periferija obično dozvoljava jednu vezu → sukob sa Mi Fitness-om. Visok rizik, veliki reverse-engineering. |
| C. Health Connect (Mi Fitness → HC) | Health Connect | neprikladno | Mi Fitness u Health Connect upisuje agregatne/istorijske podatke periodično, ne real-time 1 Hz. Neupotrebljivo za biofeedback u realnom vremenu. |
| D. Notification listener | — | neprikladno | Mi Fitness ne emituje kontinuirane HR notifikacije. |
| E. Desktop ADB + Python + UDP (postojeće) | Mi Fitness | ostaje kao **dev fallback** | Dokazano radi (~1 h), ali traži računar → nije finalno rješenje. Ne briše se: `hr_dashboard_v2.py` / `hr_dashboard_v2_AUTO_NETWORK.py`. |
| F. Native ADB klijent U Quest APK-u | — | odbačeno | HR_NATIVE_ADB_PLAN.md; traži reimplementaciju ADB transporta/AUTH na Questu. Složeno, neprovjereno, bez jasne prednosti nad A. |

**Zaključak izvodljivosti (band→telefon):** stvarni BPM POSTOJI u Mi Fitness logcat-u
(`HrItem(sid=..., time=..., hr=...)`, dokazano realnim snimkom, cadence ~1–2 s). Jedini
legitiman način da ga obična aplikacija bez root-a pročita jeste dozvola
**android.permission.READ_LOGS**, koja je `signature|privileged|development` i ne dobija
se pri instalaciji sideload-om, ali se dodjeljuje jednokratno preko ADB-a. Bez tog granta
aplikacija vidi samo svoje logove i pošteno prijavljuje „nema HR / dodijeli READ_LOGS".

---

## 3. Transport telefon → Quest

- **UDP unicast** na izabranu Quest IP adresu i port (default 5005). Nije broadcast →
  pogrešan headset ne prima tok.
- **Protokol v1 envelope** (identičan Unity `HrPacketParser.RelayPacket`):
  ```json
  {"protocolVersion":1,"sessionToken":"quest-ab12cd34","sequence":42,
   "timestampUtc":"2026-07-14T10:39:13Z","bpm":85,"source":"phone-relay","kind":"sample"}
  ```
- **kind = "heartbeat"** (bpm 0) svakih 5 s: održava „veza živa" indikator na Questu
  a da se NE lažira HR kad je narukvica na trenutak tiha. Heartbeat nikad ne ulazi u
  baseline/agregat.
- **sessionToken**: identitet uparivanja. Telefon generiše token jednom i čuva ga; Quest
  se veže za PRVI viđeni token i odbija svaki drugi (drugi telefon/uljez ne može da ubaci
  podatke).
- **sequence**: raste monotono kroz i uzorke i heartbeat-e → jedinstveni ključ za
  odbacivanje duplikata i paketa van redosljeda. `sequence == 0` znači legitiman restart
  pošiljaoca (ne zaključava se trajno).
- **timestampUtc**: timestamp sa sata (jedini pouzdan monotoni ključ; telefonski zidni sat
  se ne koristi za redosljed). Star watch-timestamp se odbacuje.

---

## 4. Očvršćavanje na Unity strani (`HrRelayIngest`)

Svaki parsirani paket prolazi kroz čist, deterministički filter prije nego postane uzorak:

1. **Identitet sesije** — veže se za prvi phone-relay token; drugi token = odbačeno.
2. **Redosljed sekvence** — duplikat/van-redosljeda = odbačeno; `seq 0` = restart, re-baseline.
3. **Heartbeat/uzorak** — heartbeat održava „transport živ", nikad nije fiziološki uzorak.
4. **Monotonost watch-timestamp-a** — uzorak sa ne-novijim watch vremenom = odbačeno.

Produkcijska sesija se otključava **samo** kada `HeartRateService.GetProductionReadiness()`
vrati `IsReady` — a to traži: stvaran `NetworkBridge` izvor koji radi + svjež, plauzibilan,
kvalitetan uzorak. Simulated/Disconnected/NativeAdbPlaceholder se odbijaju (spec §8).
Sat na ruci participantu NIKAD ne pokazuje sirovi BPM niti zonu za simulirane podatke —
samo „Sat nije povezan" van stvarnog Network moda.

---

## 5. READ_LOGS — jedina koncesija, iskreno

- READ_LOGS je zaštićena dozvola; sideload-ana aplikacija je ne dobija pri instalaciji.
- **Jednokratni setup:** `adb shell pm grant me.djurovic.hrrelay android.permission.READ_LOGS`.
- Poslije toga aplikacija čita sistemski logcat lokalno na telefonu **bez računara** dok
  se ne reinstalira. Nema ADB-a preko mreže i nema desktop adb-a u radu.
- Android 12+ ima i on-device „log access" dijalog za neke ROM-ove, ali je OEM-zavisan i
  NIJE verifikovan na ciljanom Samsung telefonu — zato je ADB grant referentni put.
- Vidi STANDALONE_HR_SETUP.md za korak-po-korak.

---

## 6. Pouzdanost — obrađeni slučajevi

Telefon i Quest na istoj mreži / Quest na hotspotu telefona; ugašen ekran (foreground
service + PARTIAL_WAKE_LOCK); app u pozadini; telefon promijeni IP (Quest sluša 0.0.0.0,
telefon cilja Quest IP); Wi-Fi kratko nestane (logcat reader + UDP reconnect/backoff);
narukvica prestane davati uzorke (watch-ts dedup + Quest freshness gate zatvori baseline);
companion restart (`seq 0` re-baseline); Quest restart (`NetworkHeartRateSource.StartSource`
resetuje ingest); duplikat/star/van-redosljeda/nevalidan BPM/pogrešan token → odbačeno.
Razdvojeno: „transport živ" (heartbeat/last-receive) vs „svjež stvarni uzorak" (freshness gate).

---

## 7. Statusi (iskreno razdvojeno)

| Stavka | Status |
|---|---|
| feasibility verified (band→telefon put) | **DA** — realni HrItem dokazan + parser verifikovan na stvarnom podatku (24/24) |
| architecture implemented | **DA** — Android companion + Unity ingest + gate |
| Unity compile | **DA** — Coplay `check_compile_errors`: No compile errors |
| Unity EditMode testovi | **DA** — 148/148 (uklj. HrRelayIngestTests) |
| parser verifikovan (JVM, realni podatak) | **DA** — `javac`+`java`, 24/24 PASS |
| Android build (APK) | vidi STANDALONE_HR_TEST_MATRIX.md — status upisan poslije build pokušaja |
| real band acquisition (na uređaju) | **NE** — traži telefon + Band 9 |
| phone→Quest na uređaju | **NE** — traži oba uređaja |
| screen-lock / hotspot / 60-min stabilnost | **NE** — traži uređaje |

> Nijedan „na uređaju" status se NE smije proglasiti dok se ne izvede stvarni test.
> Vidi STANDALONE_HR_TEST_MATRIX.md i STANDALONE_HR_KNOWN_LIMITATIONS.md.
