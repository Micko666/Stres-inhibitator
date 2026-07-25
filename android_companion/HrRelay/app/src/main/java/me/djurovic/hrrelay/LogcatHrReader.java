package me.djurovic.hrrelay;

import android.util.Log;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

/**
 * Reads the device logcat, extracts Xiaomi Mi Fitness HrItem samples via
 * {@link HrItemParser}, and pushes accepted samples to a listener. Runs on its
 * own thread; auto-restarts logcat if the process ends.
 *
 * REQUIRES android.permission.READ_LOGS. A normal sideloaded app CANNOT read
 * other apps' logs at install time; grant it once over ADB during setup:
 *     adb shell pm grant me.djurovic.hrrelay android.permission.READ_LOGS
 * After that one-time grant NO computer is needed at run time. See
 * STANDALONE_HR_SETUP.md. Without the grant, logcat shows only this app's own
 * lines and no HR is produced — reported honestly as "no HR / grant READ_LOGS".
 */
public final class LogcatHrReader {

    public interface Listener {
        /** A real, newer-than-last HR sample was parsed from Mi Fitness logs. */
        void onSample(HrSample sample);
        /** logcat is alive and being read (may be true even with no HR yet). */
        void onReaderState(boolean logcatAlive, String detail);
    }

    private static final String TAG = "HrRelay/Logcat";

    private final HrItemParser parser = new HrItemParser();
    private final Listener listener;

    private volatile boolean stop;
    private Thread thread;
    private Process process;

    public LogcatHrReader(Listener listener) {
        this.listener = listener;
    }

    public void start() {
        if (thread != null) return;
        stop = false;
        parser.reset();
        thread = new Thread(this::loop, "HrLogcatReader");
        thread.setDaemon(true);
        thread.start();
    }

    public void stop() {
        stop = true;
        killProcess();
        Thread t = thread;
        thread = null;
        if (t != null) t.interrupt();
    }

    private void loop() {
        int backoffMs = 1000;
        while (!stop) {
            try {
                // Drop the historical backlog so we start from live samples; the
                // parser's watch-ts dedup then prevents re-emitting old windows.
                try { Runtime.getRuntime().exec(new String[]{"logcat", "-c"}).waitFor(); }
                catch (Exception ignore) { /* -c may be unavailable; not fatal */ }

                process = Runtime.getRuntime().exec(new String[]{"logcat", "-v", "time"});
                notifyState(true, "logcat aktivan");
                backoffMs = 1000;

                BufferedReader reader = new BufferedReader(
                        new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8));
                String line;
                while (!stop && (line = reader.readLine()) != null) {
                    if (line.indexOf("HrItem") < 0 && line.indexOf("hritem") < 0) continue;
                    HrSample sample = parser.parseAndAccept(line);
                    if (sample != null && listener != null) listener.onSample(sample);
                }
            } catch (Exception e) {
                Log.w(TAG, "logcat read failed: " + e.getMessage());
                notifyState(false, "logcat greška: " + e.getMessage());
            } finally {
                killProcess();
            }

            if (stop) break;
            notifyState(false, "logcat se restartuje…");
            sleep(backoffMs);
            backoffMs = Math.min(backoffMs * 2, 15000);
        }
        notifyState(false, "logcat zaustavljen");
    }

    private void notifyState(boolean alive, String detail) {
        if (listener != null) listener.onReaderState(alive, detail);
    }

    private void killProcess() {
        Process p = process;
        process = null;
        if (p != null) { try { p.destroy(); } catch (Exception ignore) {} }
    }

    private static void sleep(long ms) {
        try { Thread.sleep(ms); } catch (InterruptedException ignore) { Thread.currentThread().interrupt(); }
    }
}
