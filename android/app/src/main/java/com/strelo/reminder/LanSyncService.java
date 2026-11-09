package com.strelo.reminder;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.NetworkInterface;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.Enumeration;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Сервис локальной синхронизации Android ↔ ПК по домашней/офисной сети Wi-Fi (LAN)
 * с автоматическим поиском компьютера (UDP Broadcast 45891 + параллельное сканирование подсети 45892).
 */
public class LanSyncService {

    public static final int DISCOVERY_UDP_PORT = 45891;
    public static final int DEFAULT_HTTP_PORT = 45892;
    private static final String DISCOVERY_MAGIC = "STRELO_DISCOVER_V1";
    private static final String DISCOVERY_REPLY_PREFIX = "STRELO_PC_V1|";

    public static class DiscoveredPc {
        public boolean found;
        public String pcName = "";
        public String ipAddress = "";
        public int port = DEFAULT_HTTP_PORT;
        public String errorMessage = "";
    }

    public static class LanSyncResult {
        public boolean success;
        public String message = "";
        public String discoveredIp = "";
        public List<ReminderItem> mergedReminders;
    }

    /**
     * Автоматически ищет запущенную программу StreloReminder на ПК в локальной сети Wi-Fi.
     */
    public static DiscoveredPc discoverPcInLan(int timeoutMs) {
        DiscoveredPc result = new DiscoveredPc();
        DatagramSocket socket = null;
        try {
            socket = new DatagramSocket();
            socket.setBroadcast(true);
            socket.setSoTimeout(Math.max(600, timeoutMs));

            byte[] sendData = DISCOVERY_MAGIC.getBytes(StandardCharsets.UTF_8);

            // 1. Отправляем широковещательный UDP-запрос на 255.255.255.255 и широковещательный адрес подсети
            List<String> broadcastAddresses = new ArrayList<>();
            broadcastAddresses.add("255.255.255.255");
            String localIp = getLocalWifiIpv4();
            if (localIp != null && localIp.contains(".")) {
                int lastDot = localIp.lastIndexOf('.');
                if (lastDot > 0) {
                    broadcastAddresses.add(localIp.substring(0, lastDot + 1) + "255");
                }
            }

            for (String bcast : broadcastAddresses) {
                try {
                    DatagramPacket packet = new DatagramPacket(
                            sendData,
                            sendData.length,
                            InetAddress.getByName(bcast),
                            DISCOVERY_UDP_PORT
                    );
                    socket.send(packet);
                } catch (Exception ignored) {
                }
            }

            byte[] recvBuf = new byte[512];
            DatagramPacket recvPacket = new DatagramPacket(recvBuf, recvBuf.length);
            socket.receive(recvPacket);

            String reply = new String(recvPacket.getData(), 0, recvPacket.getLength(), StandardCharsets.UTF_8).trim();
            if (reply.startsWith(DISCOVERY_REPLY_PREFIX)) {
                String[] parts = reply.split("\\|");
                result.found = true;
                result.pcName = parts.length > 1 ? parts[1] : "Windows-PC";
                if (parts.length > 2) {
                    try {
                        result.port = Integer.parseInt(parts[2].trim());
                    } catch (Exception ignored) {
                        result.port = DEFAULT_HTTP_PORT;
                    }
                }
                String senderIp = recvPacket.getAddress() != null ? recvPacket.getAddress().getHostAddress() : "";
                result.ipAddress = (senderIp != null && !senderIp.isEmpty())
                        ? senderIp
                        : (parts.length > 3 ? parts[3].trim() : "");
                return result;
            }
        } catch (Exception ignored) {
        } finally {
            if (socket != null) {
                socket.close();
            }
        }

        // 2. Если роутер блокирует UDP Broadcast — быстро проверяем локальную подсеть /24 по TCP порту 45892
        String myIp = getLocalWifiIpv4();
        if (myIp != null && myIp.contains(".")) {
            int lastDot = myIp.lastIndexOf('.');
            if (lastDot > 0) {
                final String prefix = myIp.substring(0, lastDot + 1);
                final AtomicReference<DiscoveredPc> foundRef = new AtomicReference<>(null);
                final CountDownLatch latch = new CountDownLatch(254);
                ExecutorService pool = Executors.newFixedThreadPool(32);

                for (int host = 1; host <= 254; host++) {
                    final String candidateIp = prefix + host;
                    if (candidateIp.equals(myIp)) {
                        latch.countDown();
                        continue;
                    }
                    pool.execute(new Runnable() {
                        @Override
                        public void run() {
                            try {
                                if (foundRef.get() == null) {
                                    DiscoveredPc pinged = pingCandidatePc(candidateIp, DEFAULT_HTTP_PORT, 320);
                                    if (pinged != null && pinged.found) {
                                        foundRef.compareAndSet(null, pinged);
                                    }
                                }
                            } finally {
                                latch.countDown();
                            }
                        }
                    });
                }

                try {
                    latch.await(2500, TimeUnit.MILLISECONDS);
                } catch (InterruptedException ignored) {
                }
                pool.shutdownNow();

                DiscoveredPc tcpFound = foundRef.get();
                if (tcpFound != null && tcpFound.found) {
                    return tcpFound;
                }
            }
        }

        result.found = false;
        result.errorMessage = "Компьютер со StreloReminder не найден в текущей сети Wi-Fi. Убедитесь, что телефон и ПК подключены к одному роутеру.";
        return result;
    }

    public static DiscoveredPc pingCandidatePc(String ip, int port, int timeoutMs) {
        HttpURLConnection conn = null;
        try {
            URL url = new URL("http://" + ip + ":" + port + "/strelo/ping");
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            int code = conn.getResponseCode();
            if (code == 200) {
                String body = readStream(conn.getInputStream());
                JSONObject json = new JSONObject(body);
                if ("ok".equalsIgnoreCase(json.optString("status", ""))) {
                    DiscoveredPc pc = new DiscoveredPc();
                    pc.found = true;
                    pc.ipAddress = ip;
                    pc.port = port;
                    pc.pcName = json.optString("pcName", "Windows-PC");
                    return pc;
                }
            }
        } catch (Exception ignored) {
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
        return null;
    }

    /**
     * Выполняет двустороннюю синхронизацию напрямую с ПК по Wi-Fi (с автопоиском ПК при необходимости).
     */
    public static LanSyncResult synchronizeOverLan(CloudSyncConfig config, List<ReminderItem> localReminders) {
        LanSyncResult res = new LanSyncResult();
        res.mergedReminders = localReminders;

        int port = (config != null && config.lanPort > 0) ? config.lanPort : DEFAULT_HTTP_PORT;
        String targetIp = (config != null && config.lanPcIp != null) ? config.lanPcIp.trim() : "";

        // Если IP ещё не известен — автоматически ищем ПК в сети Wi-Fi
        if (targetIp.isEmpty()) {
            DiscoveredPc discovered = discoverPcInLan(1200);
            if (!discovered.found) {
                res.success = false;
                res.message = discovered.errorMessage;
                return res;
            }
            targetIp = discovered.ipAddress;
            port = discovered.port;
            if (config != null) {
                config.lanPcIp = targetIp;
                config.lanPort = port;
            }
        }

        // Пробуем синхронизироваться по targetIp; если IP изменился — переоткрываем через автопоиск
        LanSyncResult direct = sendSyncRequestToPc(targetIp, port, localReminders);
        if (direct.success) {
            direct.discoveredIp = targetIp;
            return direct;
        }

        DiscoveredPc rediscovered = discoverPcInLan(1200);
        if (rediscovered.found && !rediscovered.ipAddress.equals(targetIp)) {
            if (config != null) {
                config.lanPcIp = rediscovered.ipAddress;
                config.lanPort = rediscovered.port;
            }
            LanSyncResult retry = sendSyncRequestToPc(rediscovered.ipAddress, rediscovered.port, localReminders);
            if (retry.success) {
                retry.discoveredIp = rediscovered.ipAddress;
                return retry;
            }
        }

        return direct;
    }

    private static LanSyncResult sendSyncRequestToPc(String ip, int port, List<ReminderItem> localReminders) {
        LanSyncResult res = new LanSyncResult();
        res.mergedReminders = localReminders;
        HttpURLConnection conn = null;
        try {
            URL url = new URL("http://" + ip + ":" + port + "/strelo/sync");
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("POST");
            conn.setConnectTimeout(3500);
            conn.setReadTimeout(5000);
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            conn.setDoOutput(true);

            String payloadJson = StorageService.serializeReminders(localReminders);
            byte[] bytes = payloadJson.getBytes(StandardCharsets.UTF_8);
            conn.setFixedLengthStreamingMode(bytes.length);
            try (OutputStream os = conn.getOutputStream()) {
                os.write(bytes);
            }

            int code = conn.getResponseCode();
            if (code >= 200 && code < 300) {
                String respJson = readStream(conn.getInputStream());
                List<ReminderItem> merged = StorageService.deserializeReminders(respJson);
                SimpleDateFormat sdf = new SimpleDateFormat("HH:mm:ss", Locale.getDefault());
                res.success = true;
                res.mergedReminders = merged;
                res.discoveredIp = ip;
                res.message = "Wi-Fi синхр. с ПК " + ip + " (" + sdf.format(new Date()) + ")";
                return res;
            } else {
                res.success = false;
                res.message = "Ошибка Wi-Fi сервера ПК: HTTP " + code;
                return res;
            }
        } catch (Exception ex) {
            res.success = false;
            res.message = "ПК по Wi-Fi (" + ip + ") недоступен";
            return res;
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
    }

    public static String getLocalWifiIpv4() {
        try {
            Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
            while (interfaces.hasMoreElements()) {
                NetworkInterface ni = interfaces.nextElement();
                if (!ni.isUp() || ni.isLoopback()) continue;
                Enumeration<InetAddress> addrs = ni.getInetAddresses();
                while (addrs.hasMoreElements()) {
                    InetAddress addr = addrs.nextElement();
                    if (addr instanceof Inet4Address && !addr.isLoopbackAddress()) {
                        String ip = addr.getHostAddress();
                        if (ip != null && (ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172."))) {
                            return ip;
                        }
                    }
                }
            }
        } catch (Exception ignored) {
        }
        return null;
    }

    private static String readStream(InputStream stream) throws Exception {
        if (stream == null) return "";
        StringBuilder sb = new StringBuilder();
        BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8));
        String line;
        while ((line = reader.readLine()) != null) {
            sb.append(line).append('\n');
        }
        return sb.toString();
    }
}
