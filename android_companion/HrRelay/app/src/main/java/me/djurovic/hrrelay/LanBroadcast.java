package me.djurovic.hrrelay;

import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Enumeration;
import java.util.List;

/**
 * Finds the IPv4 broadcast addresses of the phone's active networks, used to
 * probe for the Quest without knowing its address.
 *
 * Subnet-directed broadcast (e.g. 192.168.1.255) is preferred over the global
 * 255.255.255.255: Android and several consumer routers drop the global form,
 * and on a phone with multiple interfaces (Wi-Fi + hotspot) the directed form
 * reaches the right subnet. The global address is appended last as a fallback
 * so a network that reports no broadcast address is still probed.
 *
 * PURE JAVA (no Android imports) so it stays unit-testable in a plain JVM.
 */
public final class LanBroadcast {

    public static final String GLOBAL_BROADCAST = "255.255.255.255";

    private LanBroadcast() {}

    /**
     * Broadcast targets to probe, most specific first. Never empty: the global
     * broadcast address is always included as the last resort.
     */
    public static List<InetAddress> targets() {
        List<InetAddress> out = new ArrayList<InetAddress>();
        try {
            Enumeration<NetworkInterface> ifaces = NetworkInterface.getNetworkInterfaces();
            while (ifaces != null && ifaces.hasMoreElements()) {
                NetworkInterface nif = ifaces.nextElement();
                try {
                    if (!nif.isUp() || nif.isLoopback()) continue;
                } catch (Exception ignore) {
                    continue;
                }
                for (InterfaceAddress ia : nif.getInterfaceAddresses()) {
                    InetAddress b = ia.getBroadcast();   // null for IPv6
                    if (b != null && !out.contains(b)) out.add(b);
                }
            }
        } catch (Exception ignore) {
            // Fall through to the global address below.
        }
        try {
            InetAddress global = InetAddress.getByName(GLOBAL_BROADCAST);
            if (!out.contains(global)) out.add(global);
        } catch (Exception ignore) {
            // Nothing more we can do; caller handles an empty list.
        }
        return Collections.unmodifiableList(out);
    }
}
