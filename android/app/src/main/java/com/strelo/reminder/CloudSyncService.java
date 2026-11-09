package com.strelo.reminder;

import android.util.Base64;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.zip.GZIPInputStream;
import java.util.zip.GZIPOutputStream;

public class CloudSyncService {

    private static final String BROWSER_USER_AGENT = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";
    private static final String SYNC_MARKER_RAW = "STRELO_SYNC_V1:";
    private static final String SYNC_MARKER_GZIP = "STRELO_SYNC_GZ:";
    private static final String SYNC_MARKER_END = "|END_STRELO";

    private static final String[] ONE_CLICK_RELAY_BASES = new String[]{
            "https://ntfy.sh",
            "https://ntfy.envs.net",
            "https://ntfy.tedomum.net"
    };

    private static final Map<String, String> LAST_PUSHED_BY_ROOM = new HashMap<>();

    public static class SyncResult {
        public boolean success;
        public String message;
        public List<ReminderItem> mergedReminders;
        public boolean localListUpdated;
    }

    public static class CreateGistResult {
        public boolean success;
        public String gistId;
        public String errorMessage;
    }

    public static class CreateOneClickResult {
        public boolean success;
        public String roomId = "";
        public String cloudUrl = "";
        public String errorMessage = "";
    }

    public static String generateNewRoomId() {
        return "strelo-" + UUID.randomUUID().toString().replace("-", "").substring(0, 10).toLowerCase(Locale.US);
    }

    /**
     * Создаёт новое «Облако в 1 клик» без регистрации и токенов.
     * Генерирует уникальный ID комнаты локально (без риска ошибки 403) и отправляет начальное состояние в пул релеев.
     */
    public static CreateOneClickResult createOneClickCloudRoom(List<ReminderItem> initialReminders) {
        CreateOneClickResult res = new CreateOneClickResult();
        res.roomId = generateNewRoomId();
        res.cloudUrl = "https://ntfy.sh/strelo_room_" + res.roomId;
        res.success = true;

        String remindersJson = StorageService.serializeReminders(initialReminders);
        if (remindersJson == null || remindersJson.trim().isEmpty()) {
            remindersJson = "[]";
        }

        try {
            pushOneClickCloudRoom(res.roomId, remindersJson);
        } catch (Exception ignored) {
            // Даже если внешний интернет временно недоступен, комната и QR-код успешно созданы
        }

        return res;
    }

    public static CreateGistResult createNewGist(String githubToken, List<ReminderItem> initialReminders) {
        CreateGistResult res = new CreateGistResult();
        if (githubToken == null || githubToken.trim().isEmpty()) {
            res.success = false;
            res.errorMessage = "Укажите GitHub Personal Access Token (с правом gist).";
            return res;
        }

        try {
            String remindersJson = StorageService.serializeReminders(initialReminders);
            JSONObject fileContent = new JSONObject();
            fileContent.put("content", remindersJson.isEmpty() ? "[]" : remindersJson);

            JSONObject files = new JSONObject();
            files.put("strelo_reminders.json", fileContent);

            JSONObject payload = new JSONObject();
            payload.put("description", "StreloReminder Cloud Sync (PC <-> Android)");
            payload.put("public", false);
            payload.put("files", files);

            String response = sendHttpRequest(
                    "https://api.github.com/gists",
                    "POST",
                    githubToken.trim(),
                    payload.toString(),
                    "application/json; charset=utf-8",
                    15000
            );

            JSONObject respObj = new JSONObject(response);
            String id = respObj.optString("id", "").trim();
            if (id.isEmpty()) {
                res.success = false;
                res.errorMessage = "Сервер GitHub не вернул ID созданного Gist.";
                return res;
            }

            res.success = true;
            res.gistId = id;
            return res;
        } catch (Exception e) {
            res.success = false;
            res.errorMessage = "Ошибка создания Gist: " + e.getMessage();
            return res;
        }
    }

    /**
     * Выполняет гибридную двустороннюю синхронизацию:
     * 1) Если включена локальная синхронизация по Wi-Fi — мгновенно синхронизируется с ПК в домашней сети.
     * 2) Если настроено Облако (Облако в 1 клик / GitHub Gist / Cloud URL) — также синхронизируется с облаком.
     */
    public static SyncResult synchronize(CloudSyncConfig config, List<ReminderItem> localSnapshot, boolean forcePush) {
        SyncResult result = new SyncResult();
        result.success = false;
        List<ReminderItem> workingList = localSnapshot != null ? localSnapshot : new ArrayList<ReminderItem>();
        result.mergedReminders = workingList;
        result.localListUpdated = false;

        if (config == null || !config.isConfigured()) {
            result.message = "Синхронизация не настроена";
            return result;
        }

        boolean lanSuccess = false;
        String lanMsg = "";

        // Шаг 1: Локальная синхронизация по Wi-Fi (LAN)
        if (config.lanWifiSyncEnabled) {
            LanSyncService.LanSyncResult lanRes = LanSyncService.synchronizeOverLan(config, workingList);
            if (lanRes.success && lanRes.mergedReminders != null) {
                boolean[] flags = new boolean[2];
                workingList = mergeReminders(workingList, lanRes.mergedReminders, flags);
                if (flags[0]) {
                    result.localListUpdated = true;
                }
                result.mergedReminders = workingList;
                lanSuccess = true;
                lanMsg = lanRes.message;
            } else {
                lanMsg = lanRes.message;
            }
        }

        // Шаг 2: Облачная синхронизация через интернет (если настроена)
        if (config.isCloudConfigured()) {
            try {
                boolean[] hadExisting = new boolean[]{false};
                String remoteJson = fetchRemoteRemindersJson(config, hadExisting);
                List<ReminderItem> remoteList = StorageService.deserializeReminders(remoteJson);

                boolean[] flags = new boolean[2];
                List<ReminderItem> merged = mergeReminders(workingList, remoteList, flags);

                String mergedJson = StorageService.serializeReminders(merged);
                boolean shouldPush = flags[1];
                if (!shouldPush && (forcePush || lanSuccess)) {
                    String normMerged = mergedJson != null ? mergedJson.trim() : "[]";
                    String normRemote = remoteJson != null ? remoteJson.trim() : "[]";
                    if (!hadExisting[0] && !merged.isEmpty()) {
                        shouldPush = true;
                    } else if (!normMerged.equals(normRemote)) {
                        shouldPush = true;
                    }
                }

                if (shouldPush) {
                    pushRemoteRemindersJson(config, mergedJson);
                }

                SimpleDateFormat sdf = new SimpleDateFormat("HH:mm:ss", Locale.getDefault());
                result.success = true;
                result.mergedReminders = merged;
                if (flags[0]) {
                    result.localListUpdated = true;
                }
                result.message = lanSuccess
                        ? ("Wi-Fi + Облако (" + sdf.format(new Date()) + ")")
                        : ("Облако синхр. (" + sdf.format(new Date()) + ")");
                return result;
            } catch (Exception e) {
                if (lanSuccess) {
                    result.success = true;
                    result.message = lanMsg;
                    return result;
                }
                result.success = false;
                result.message = "Ошибка облака: " + e.getMessage();
                return result;
            }
        }

        if (lanSuccess) {
            result.success = true;
            result.message = lanMsg;
            return result;
        }

        result.success = false;
        result.message = !lanMsg.isEmpty() ? lanMsg : "Синхронизация не выполнена";
        return result;
    }

    public static List<ReminderItem> mergeReminders(
            List<ReminderItem> localList,
            List<ReminderItem> remoteList,
            boolean[] outFlags) {

        boolean hasChangesFromRemote = false;
        boolean hasChangesForRemote = false;

        Map<String, ReminderItem> localMap = new LinkedHashMap<>();
        if (localList != null) {
            for (ReminderItem item : localList) {
                if (item != null && item.id != null && !item.id.isEmpty()) {
                    localMap.put(item.id, item);
                }
            }
        }

        Map<String, ReminderItem> remoteMap = new LinkedHashMap<>();
        if (remoteList != null) {
            for (ReminderItem item : remoteList) {
                if (item != null && item.id != null && !item.id.isEmpty()) {
                    remoteMap.put(item.id, item);
                }
            }
        }

        List<ReminderItem> merged = new ArrayList<>();
        Set<String> processed = new HashSet<>();

        for (Map.Entry<String, ReminderItem> entry : localMap.entrySet()) {
            String id = entry.getKey();
            ReminderItem localItem = entry.getValue();
            processed.add(id);

            ReminderItem remoteItem = remoteMap.get(id);
            if (remoteItem == null) {
                merged.add(localItem.copy());
                hasChangesForRemote = true;
            } else {
                if (remoteItem.updatedAtUnixMs > localItem.updatedAtUnixMs) {
                    merged.add(remoteItem.copy());
                    hasChangesFromRemote = true;
                } else if (localItem.updatedAtUnixMs > remoteItem.updatedAtUnixMs) {
                    merged.add(localItem.copy());
                    hasChangesForRemote = true;
                } else {
                    merged.add(localItem.copy());
                }
            }
        }

        for (Map.Entry<String, ReminderItem> entry : remoteMap.entrySet()) {
            if (!processed.contains(entry.getKey())) {
                merged.add(entry.getValue().copy());
                hasChangesFromRemote = true;
            }
        }

        if (outFlags != null && outFlags.length >= 2) {
            outFlags[0] = hasChangesFromRemote;
            outFlags[1] = hasChangesForRemote;
        }
        return merged;
    }

    private static String fetchRemoteRemindersJson(CloudSyncConfig config, boolean[] outHadExisting) throws Exception {
        if (outHadExisting != null && outHadExisting.length > 0) {
            outHadExisting[0] = false;
        }
        if (config.providerTypeValue == CloudSyncConfig.PROVIDER_ONE_CLICK_CLOUD) {
            String roomId = CloudSyncConfig.sanitizeRoomId(config.oneClickRoomId);
            if (roomId.isEmpty()) return "[]";
            return fetchOneClickCloudRoom(roomId, outHadExisting);
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_GITHUB_GIST) {
            String url = "https://api.github.com/gists/" + config.gistId.trim();
            String response = sendHttpRequest(url, "GET", config.gitHubToken.trim(), null, null, 15000);
            JSONObject root = new JSONObject(response);
            JSONObject files = root.optJSONObject("files");
            if (files != null) {
                JSONObject fileObj = files.optJSONObject("strelo_reminders.json");
                if (fileObj != null) {
                    if (outHadExisting != null && outHadExisting.length > 0) outHadExisting[0] = true;
                    return fileObj.optString("content", "[]");
                }
            }
            return "[]";
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_CUSTOM_URL) {
            String response = sendHttpRequest(config.customCloudUrl.trim(), "GET", null, null, null, 15000);
            if (response == null || response.trim().isEmpty() || "null".equalsIgnoreCase(response.trim())) {
                return "[]";
            }
            if (outHadExisting != null && outHadExisting.length > 0) outHadExisting[0] = true;
            return response;
        }
        return "[]";
    }

    private static void pushRemoteRemindersJson(CloudSyncConfig config, String remindersJson) throws Exception {
        String normalized = (remindersJson == null || remindersJson.trim().isEmpty()) ? "[]" : remindersJson.trim();

        if (config.providerTypeValue == CloudSyncConfig.PROVIDER_ONE_CLICK_CLOUD) {
            String roomId = CloudSyncConfig.sanitizeRoomId(config.oneClickRoomId);
            if (!roomId.isEmpty()) {
                pushOneClickCloudRoom(roomId, normalized);
            }
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_GITHUB_GIST) {
            String url = "https://api.github.com/gists/" + config.gistId.trim();
            JSONObject fileContent = new JSONObject();
            fileContent.put("content", normalized);

            JSONObject files = new JSONObject();
            files.put("strelo_reminders.json", fileContent);

            JSONObject payload = new JSONObject();
            payload.put("description", "StreloReminder Cloud Sync (PC <-> Android)");
            payload.put("files", files);

            sendHttpRequest(url, "PATCH", config.gitHubToken.trim(), payload.toString(), "application/json; charset=utf-8", 15000);
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_CUSTOM_URL) {
            sendHttpRequest(config.customCloudUrl.trim(), "PUT", null, normalized, "application/json; charset=utf-8", 15000);
        }
    }

    private static String fetchOneClickCloudRoom(String roomId, boolean[] outHadExisting) throws Exception {
        String cleanRoom = CloudSyncConfig.sanitizeRoomId(roomId);
        String topic = "strelo_room_" + cleanRoom;
        Exception lastEx = null;
        boolean anyRelayReachable = false;

        for (String baseUrl : ONE_CLICK_RELAY_BASES) {
            String url = baseUrl + "/" + topic + "/raw?poll=1&since=all";
            try {
                String rawResp = sendHttpRequest(url, "GET", null, null, null, 8000);
                anyRelayReachable = true;
                String extracted = tryExtractLatestOneClickJson(rawResp);
                if (extracted != null) {
                    if (outHadExisting != null && outHadExisting.length > 0) {
                        outHadExisting[0] = true;
                    }
                    synchronized (LAST_PUSHED_BY_ROOM) {
                        LAST_PUSHED_BY_ROOM.put(cleanRoom, extracted);
                    }
                    return extracted;
                }
            } catch (Exception ex) {
                lastEx = ex;
            }
        }

        if (anyRelayReachable) {
            return "[]";
        }
        if (lastEx != null) {
            throw lastEx;
        }
        return "[]";
    }

    private static void pushOneClickCloudRoom(String roomId, String remindersJson) throws Exception {
        String cleanRoom = CloudSyncConfig.sanitizeRoomId(roomId);
        if (cleanRoom.isEmpty()) return;

        synchronized (LAST_PUSHED_BY_ROOM) {
            String last = LAST_PUSHED_BY_ROOM.get(cleanRoom);
            if (last != null && last.equals(remindersJson)) {
                return;
            }
        }

        String topic = "strelo_room_" + cleanRoom;
        String encodedBody = encodeOneClickPayload(remindersJson);
        Exception lastEx = null;
        boolean pushed = false;

        for (String baseUrl : ONE_CLICK_RELAY_BASES) {
            String url = baseUrl + "/" + topic;
            try {
                sendHttpRequest(url, "POST", null, encodedBody, "text/plain; charset=utf-8", 8000);
                pushed = true;
                break;
            } catch (Exception ex) {
                lastEx = ex;
            }
        }

        if (pushed) {
            synchronized (LAST_PUSHED_BY_ROOM) {
                LAST_PUSHED_BY_ROOM.put(cleanRoom, remindersJson);
            }
            return;
        }
        if (lastEx != null) {
            throw lastEx;
        }
    }

    public static String encodeOneClickPayload(String json) {
        String safeJson = (json == null || json.trim().isEmpty()) ? "[]" : json.trim();
        byte[] utf8 = safeJson.getBytes(StandardCharsets.UTF_8);

        if (utf8.length <= 2200) {
            return SYNC_MARKER_RAW + Base64.encodeToString(utf8, Base64.NO_WRAP) + SYNC_MARKER_END;
        }

        try {
            ByteArrayOutputStream baos = new ByteArrayOutputStream();
            try (GZIPOutputStream gzip = new GZIPOutputStream(baos)) {
                gzip.write(utf8);
            }
            return SYNC_MARKER_GZIP + Base64.encodeToString(baos.toByteArray(), Base64.NO_WRAP) + SYNC_MARKER_END;
        } catch (Exception e) {
            return SYNC_MARKER_RAW + Base64.encodeToString(utf8, Base64.NO_WRAP) + SYNC_MARKER_END;
        }
    }

    public static String tryExtractLatestOneClickJson(String responseBody) {
        if (responseBody == null || responseBody.trim().isEmpty()) {
            return null;
        }

        int lastRaw = responseBody.lastIndexOf(SYNC_MARKER_RAW);
        int lastGz = responseBody.lastIndexOf(SYNC_MARKER_GZIP);

        if (lastRaw < 0 && lastGz < 0) {
            String trimmed = responseBody.trim();
            if (trimmed.startsWith("[") && trimmed.endsWith("]")) {
                return trimmed;
            }
            return null;
        }

        boolean isGzip = lastGz > lastRaw;
        int markerStart = isGzip ? lastGz : lastRaw;
        String marker = isGzip ? SYNC_MARKER_GZIP : SYNC_MARKER_RAW;
        int payloadStart = markerStart + marker.length();

        int endIdx = responseBody.indexOf(SYNC_MARKER_END, payloadStart);
        String b64;
        if (endIdx >= payloadStart) {
            b64 = responseBody.substring(payloadStart, endIdx).trim();
        } else {
            int lineEnd = responseBody.length();
            for (int i = payloadStart; i < responseBody.length(); i++) {
                char c = responseBody.charAt(i);
                if (c == '\r' || c == '\n' || c == '"' || c == ' ') {
                    lineEnd = i;
                    break;
                }
            }
            b64 = responseBody.substring(payloadStart, lineEnd).trim();
        }

        if (b64.isEmpty()) return null;

        try {
            byte[] decoded = Base64.decode(b64, Base64.DEFAULT);
            if (!isGzip) {
                return new String(decoded, StandardCharsets.UTF_8);
            }

            try (ByteArrayInputStream bais = new ByteArrayInputStream(decoded);
                 GZIPInputStream gzis = new GZIPInputStream(bais);
                 ByteArrayOutputStream out = new ByteArrayOutputStream()) {
                byte[] buf = new byte[4096];
                int r;
                while ((r = gzis.read(buf)) > 0) {
                    out.write(buf, 0, r);
                }
                return new String(out.toByteArray(), StandardCharsets.UTF_8);
            }
        } catch (Exception e) {
            return null;
        }
    }

    private static String sendHttpRequest(
            String urlStr,
            String method,
            String bearerToken,
            String body,
            String contentType,
            int timeoutMs) throws Exception {

        URL url = new URL(urlStr);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        try {
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);
            conn.setUseCaches(false);
            conn.setRequestProperty("User-Agent", BROWSER_USER_AGENT);
            conn.setRequestProperty("Accept", "application/json, text/plain, */*");

            if ("PATCH".equalsIgnoreCase(method)) {
                try {
                    conn.setRequestMethod("PATCH");
                } catch (Exception e) {
                    conn.setRequestMethod("POST");
                    conn.setRequestProperty("X-HTTP-Method-Override", "PATCH");
                }
            } else {
                conn.setRequestMethod(method);
            }

            if (bearerToken != null && !bearerToken.trim().isEmpty()) {
                conn.setRequestProperty("Authorization", "Bearer " + bearerToken.trim());
            }

            if (body != null && !body.isEmpty()) {
                conn.setDoOutput(true);
                conn.setRequestProperty(
                        "Content-Type",
                        (contentType != null && !contentType.isEmpty()) ? contentType : "application/json; charset=utf-8"
                );
                byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
                try (OutputStream os = conn.getOutputStream()) {
                    os.write(bytes);
                    os.flush();
                }
            }

            int code = conn.getResponseCode();
            InputStream is = (code >= 200 && code < 300) ? conn.getInputStream() : conn.getErrorStream();
            StringBuilder sb = new StringBuilder();
            if (is != null) {
                try (BufferedReader reader = new BufferedReader(new InputStreamReader(is, StandardCharsets.UTF_8))) {
                    String line;
                    while ((line = reader.readLine()) != null) {
                        if (sb.length() > 0) sb.append('\n');
                        sb.append(line);
                    }
                }
            }

            if (code < 200 || code >= 300) {
                throw new Exception("HTTP " + code);
            }
            return sb.toString();
        } finally {
            conn.disconnect();
        }
    }
}
