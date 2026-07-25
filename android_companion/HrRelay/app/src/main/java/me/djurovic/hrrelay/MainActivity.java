package me.djurovic.hrrelay;

import android.Manifest;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.Spinner;
import android.widget.TextView;

import java.util.Locale;
import java.util.UUID;

import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

/**
 * Minimal, purpose-built control surface (spec §7). Shows band/source status,
 * current REAL bpm, age of the last real sample, Quest link status, a connect
 * and a disconnect button, and any error in plain language. No charts, no
 * history, no accounts.
 */
public final class MainActivity extends AppCompatActivity implements HrRelayState.Listener {

    private static final String PREFS = "hr_relay_prefs";
    private static final String KEY_HOST = "quest_host";
    private static final String KEY_PORT = "quest_port";
    private static final String KEY_TOKEN = "session_token";
    private static final String KEY_TARGET_MODE = "target_mode";
    private static final int DEFAULT_PORT = 5005;

    private final HrRelayState state = HrRelayState.get();
    private final Handler ui = new Handler(Looper.getMainLooper());

    private EditText hostInput;
    private EditText portInput;
    private TextView statusView;
    private TextView tokenView;
    private Spinner targetModeInput;
    private Button connectButton;
    private Button disconnectButton;

    private String sessionToken;

    private final Runnable poll = new Runnable() {
        @Override public void run() {
            render();
            ui.postDelayed(this, 500);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        hostInput = findViewById(R.id.host_input);
        portInput = findViewById(R.id.port_input);
        statusView = findViewById(R.id.status_view);
        tokenView = findViewById(R.id.token_view);
        targetModeInput = findViewById(R.id.target_mode_input);
        connectButton = findViewById(R.id.connect_button);
        disconnectButton = findViewById(R.id.disconnect_button);

        SharedPreferences prefs = getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        hostInput.setText(prefs.getString(KEY_HOST, ""));
        portInput.setText(String.valueOf(prefs.getInt(KEY_PORT, DEFAULT_PORT)));
        ArrayAdapter<CharSequence> targetAdapter = ArrayAdapter.createFromResource(this,
                R.array.target_modes, android.R.layout.simple_spinner_item);
        targetAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        targetModeInput.setAdapter(targetAdapter);
        targetModeInput.setSelection(prefs.getInt(KEY_TARGET_MODE, 0));
        sessionToken = prefs.getString(KEY_TOKEN, "");
        if (TextUtils.isEmpty(sessionToken)) {
            sessionToken = "quest-" + UUID.randomUUID().toString().substring(0, 8);
            prefs.edit().putString(KEY_TOKEN, sessionToken).apply();
        }
        state.sessionToken = sessionToken;

        connectButton.setOnClickListener(v -> startRelay());
        disconnectButton.setOnClickListener(v -> stopRelay());

        requestNotificationPermissionIfNeeded();
    }

    @Override
    protected void onResume() {
        super.onResume();
        state.setListener(this);
        ui.post(poll);
    }

    @Override
    protected void onPause() {
        super.onPause();
        state.setListener(null);
        ui.removeCallbacks(poll);
    }

    @Override
    public void onStateChanged() {
        ui.post(this::render);
    }

    private void startRelay() {
        String host = hostInput.getText().toString().trim();
        int port = parsePort(portInput.getText().toString());
        int targetMode = targetModeInput.getSelectedItemPosition();
        getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putString(KEY_HOST, host).putInt(KEY_PORT, port)
                .putInt(KEY_TARGET_MODE, targetMode).apply();

        Intent i = new Intent(this, HrRelayService.class);
        i.setAction(HrRelayService.ACTION_START);
        i.putExtra(HrRelayService.EXTRA_HOST, host);
        i.putExtra(HrRelayService.EXTRA_PORT, port);
        i.putExtra(HrRelayService.EXTRA_TOKEN, sessionToken);
        i.putExtra(HrRelayService.EXTRA_TARGET_KIND, targetMode == 1 ? "Quest" : "Unity");
        ContextCompat.startForegroundService(this, i);
    }

    private void stopRelay() {
        Intent i = new Intent(this, HrRelayService.class);
        i.setAction(HrRelayService.ACTION_STOP);
        startService(i);
    }

    private void render() {
        tokenView.setText("Pairing token: " + sessionToken);

        StringBuilder sb = new StringBuilder();
        sb.append("SERVIS: ").append(state.serviceRunning ? "aktivan" : "zaustavljen").append('\n');
        sb.append("LOGCAT: ").append(state.logcatAlive ? "čita" : "ne čita")
          .append(" (").append(state.readerDetail).append(")\n");

        double sampleAge = state.lastSampleAgeSeconds();
        if (state.lastBpm > 0 && sampleAge >= 0) {
            sb.append("HR UZORAK: ")
              .append(state.isHrSampleFresh() ? "svjež" : "zastario")
              .append(" · ").append(state.lastBpm).append(" bpm · ")
              .append(String.format(Locale.US, "%.1f", sampleAge)).append(" s\n");
        } else {
            sb.append("HR UZORAK: nema stvarnog uzorka još\n");
        }

        if (state.questConfigured) {
            sb.append("SLANJE: Šalje na: ").append(state.questTarget)
              .append(" · seq ").append(state.lastSequenceSent).append('\n');
            if (state.isAckFresh()) {
                sb.append("PRIJEM: ").append(RelayStatusText.confirmation(true, true,
                        state.destinationName)).append(" · ACK seq ")
                  .append(state.lastAckSequence).append(" · ")
                  .append(String.format(Locale.US, "%.1f", state.lastAckAgeSeconds()))
                  .append(" s\n");
            } else {
                sb.append("PRIJEM: ").append(RelayStatusText.confirmation(true, false,
                        state.destinationName)).append('\n');
            }
        } else {
            sb.append("SLANJE: odredište nije postavljeno\n");
            sb.append("PRIJEM: ").append(RelayStatusText.confirmation(false, false,
                    state.destinationName)).append('\n');
        }

        if (!TextUtils.isEmpty(state.errorText)) {
            sb.append('\n').append("GREŠKA: ").append(state.errorText);
        }
        if (!hasReadLogsHint()) {
            sb.append('\n').append("NAPOMENA: ako nema pulsa, dozvoli čitanje logova jednom preko ADB-a "
                    + "(vidi STANDALONE_HR_SETUP.md).");
        }
        statusView.setText(sb.toString());
    }

    private boolean hasReadLogsHint() {
        // READ_LOGS is not a runtime-checkable normal permission; we only surface
        // the hint. Presence of any accepted sample implies the grant worked.
        return state.lastBpm > 0;
    }

    private int parsePort(String raw) {
        try {
            int p = Integer.parseInt(raw.trim());
            return (p > 0 && p < 65536) ? p : DEFAULT_PORT;
        } catch (Exception e) {
            return DEFAULT_PORT;
        }
    }

    private void requestNotificationPermissionIfNeeded() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                && ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED) {
            ActivityCompat.requestPermissions(this,
                    new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions,
                                           @NonNull int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
    }
}
