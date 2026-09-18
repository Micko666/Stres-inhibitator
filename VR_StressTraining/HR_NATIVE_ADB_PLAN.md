# HR_NATIVE_ADB_PLAN.md — plan za standalone native ADB HR izvor

Status: **PLAN + arhitektonski placeholder** (`NativeAdbHeartRateSource`,
`INativeAdbBridge`, `NativeAdbConnectionState` postoje u kodu; native plugin NE postoji).
Placeholder nikad ne prijavljuje vezu — stanje je trajno `NotImplemented`.

## Cilj

```
Xiaomi Smart Band 9 → Mi Fitness (telefon) → adbd preko Wi-Fi (tcp/5555)
  → native C/C++ ADB klijent UNUTAR Quest APK-a (ARM64 .so)
  → C ABI → Unity C# P/Invoke → NativeAdbHeartRateSource → HeartRateService
```

KLJUČNO: desktop `adb.exe` se NE pakuje u APK i Android aplikacija NE pokreće
desktop adb executable. Native klijent implementira ADB protokol (CNXN/AUTH/OPEN/
WRTE/CLSE transport frames) direktno preko TCP socketa.

## C ABI (za IL2CPP P/Invoke)

```c
// libnativeadb.so — sve funkcije thread-safe, nikad ne blokiraju main thread
int32_t  nadb_initialize(const char* rsa_key_dir);
int32_t  nadb_connect(const char* host, int32_t port);       // async; vraća request id
void     nadb_disconnect(void);
int32_t  nadb_get_state(void);                               // NativeAdbConnectionState
int32_t  nadb_start_logcat(const char* filter_spec);         // "logcat -v time"
int32_t  nadb_poll_samples(NadbSample* buffer, int32_t max); // parsirani HR sample-i
void     nadb_set_state_callback(void (*cb)(int32_t state)); // opciono; poll je primaran
void     nadb_cancel_all(void);
void     nadb_shutdown(void);

typedef struct {
    int32_t bpm;
    int64_t source_unix_ms;    // watch timestamp iz HrItem(time=...)
    int32_t sequence;
} NadbSample;
```

## Obavezne stavke

| Stavka | Plan |
|---|---|
| Android ARM64 plugin | CMake/NDK, `Plugins/Android/libs/arm64-v8a/libnativeadb.so`; IL2CPP kompatibilno (C ABI, bez C++ izuzetaka preko granice) |
| Connect | TCP socket na phone IP:5555; ADB CNXN handshake; timeout+retry |
| Pair/auth | ADB AUTH (RSA): potpisivanje tokena privatnim ključem; ako telefon ne prizna ključ → AUTH RSAPUBLICKEY → korisnik potvrđuje dijalog na telefonu JEDNOM |
| RSA key storage | ključ se generiše na Questu, čuva u `Application.persistentDataPath/adbkeys/` (privatni ključ NIKAD u logove/izvještaje) |
| Logcat stream | `shell:logcat -v time` preko ADB OPEN/WRTE streama; ring buffer u native sloju |
| Parser | port postojećih regexa iz `hr_dashboard_v2.py` (HrItem/single_heart_rate/latestHrRecord/realTimeHeartRate; dedupe po watch ts) u C — REFERENTNA IMPLEMENTACIJA je Python |
| Packet/sample callback | poll model primaran (`nadb_poll_samples` iz `DrainSamples`); callback opcion |
| Cancellation/lifecycle | `nadb_cancel_all` prekida blokirajuće socket operacije; OnApplicationPause(true) → pauza streama; resume → reconnect |
| Reconnect | eksponencijalni backoff 1→30 s; stanje vidljivo kroz `NativeAdbConnectionState` |
| Phone IP/port config | postojeći `config.json` (`network` sekcija) + developer panel unos |
| Android permissions | `android.permission.INTERNET` (provjeriti merged manifest builda); cleartext TCP je lokalna mreža — dokumentovati u privacy notes |
| Quest IL2CPP build | `[DllImport("nativeadb")]`; testirati i Mono editor fallback (DLL stub koji vraća NotImplemented) |
| Sigurnosni rizici | ADB pristup telefonu = puna shell kontrola: koristiti ISKLJUČIVO namjenski studijski telefon; ključeve brisati poslije studije; ne logovati sadržaj logcat-a osim HR linija |
| Test plan | (1) unit: parser nad snimljenim logcat uzorcima; (2) desktop C test klijent protiv pravog telefona; (3) Quest standalone + telefon + Band 9 BEZ Python-a i BEZ računara; (4) reconnect test (telefon van dometa pa nazad); (5) auth-revoke test |

## Šta NE raditi

- Ne pokušavati `Runtime.exec("adb ...")` na Androidu (nema binarnog adb-a, nema privilegija).
- Ne tvrditi uspjeh bez standalone Quest+telefon+Band 9 testa bez računara.
- Ne blokirati Unity main thread socket operacijama.

## Fallback lanac (ostaje trajno)

1. `Simulated` — razvoj/demo (jasno označen SIMULIRANI PODACI).
2. `Network` — Python bridge (`hr_dashboard_v2.py`) za Play Mode / Quest Link — OSTAJE kao razvojni/dijagnostički alat i dokaz parsera.
3. `NativeAdb` — finalni standalone cilj (ovaj plan).
