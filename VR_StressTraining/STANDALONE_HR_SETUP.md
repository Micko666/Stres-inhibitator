# STANDALONE_HR_SETUP.md — instalacija i povezivanje

Cilj u normalnom radu: samo **Band 9 + telefon + Quest**, ista Wi-Fi mreža ili
telefonski hotspot. Računar treba **samo jednom** (instalacija APK-a + READ_LOGS grant).

---

## A. Jednokratni setup (računar potreban samo ovdje)

### 1. Build HR Relej APK-a
```
cd android_companion/HrRelay
# koristi Android SDK (npr. Unity-jev) preko local.properties (FORWARD SLASHES!)
cp local.properties.example local.properties     # provjeri sdk.dir
gradlew.bat assembleDebug                          # → app/build/outputs/apk/debug/app-debug.apk
```

### 2. Instaliraj na telefon
```
platform-tools\adb.exe install -r app-debug.apk
```

### 3. Dodijeli READ_LOGS (KLJUČNI korak — jednom)
```
platform-tools\adb.exe shell pm grant me.djurovic.hrrelay android.permission.READ_LOGS
```
Bez ovoga aplikacija vidi samo svoje logove i neće biti pulsa. Poslije ovoga računar
više NIJE potreban (dozvola traje do reinstalacije aplikacije).

### 4. (opciono) Dozvoli notifikacije kad aplikacija to zatraži (Android 13+).

---

## B. Normalan rad (bez računara)

1. Na telefonu otvori **Mi Fitness** i uđi u Heart rate / pokreni kontinuirano
   mjerenje pulsa (Band 9 mora da šalje HrItem u logcat).
2. Telefon i Quest na istoj Wi-Fi mreži **ili** uključi hotspot na telefonu i spoji Quest.
3. Sazna Quest IP: na Questu Settings → Wi-Fi → (mreža) → Advanced → IP Address.
   (Kasnije: produkcijska sesija na Questu prikazuje svoj IP na HR panelu.)
4. Otvori **HR Relej** na telefonu:
   - unesi Quest IP, port ostavi **5005**,
   - pritisni **Poveži**.
5. Stavi Quest. Baseline u sesiji se otključava tek kad stigne stvaran, svjež uzorak.

---

## C. Provjera da radi

- HR Relej ekran: „logcat: čita", „PULS: NN bpm · star X s", „QUEST: povezan → IP:5005",
  „Poslato paketa (seq)" raste.
- Na Questu: sat na ruci pokazuje zonu (Stabilno/Povišeno/Visoko) samo u Network modu;
  baseline gate propušta dalje.
- Dev fallback (računar): `python hr_dashboard_v2.py` i dalje radi za dijagnostiku.

---

## D. Ako nema pulsa

| Simptom | Uzrok / rješenje |
|---|---|
| „PULS: nema stvarnog uzorka" a logcat čita | Mi Fitness ne emituje HR — otvori HR ekran/sinhronizuj u Mi Fitness-u |
| logcat „ne čita" | READ_LOGS nije dodijeljen → ponovi korak A.3 |
| QUEST „nije postavljen" | Unesi Quest IP i pritisni Poveži |
| Paketi rastu, Quest ništa | Provjeri da su na istoj mreži/hotspotu; firewall na Questu; tačan IP |
| Radi pa stane kad se ugasi ekran | Isključi bateriju-optimizaciju za HR Relej (Android Settings → Apps → HR Relej → Battery → Unrestricted) |

Detaljne granice i šta NIJE verifikovano: STANDALONE_HR_KNOWN_LIMITATIONS.md.
