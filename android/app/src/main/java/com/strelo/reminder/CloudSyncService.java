package com.strelo.reminder;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

public class CloudSyncService {

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

    /**
     * Создаёт новое «Облако в 1 клик» без регистрации и токенов.
     */
    public static CreateOneClickResult createOneClickCloudRoom(List<ReminderItem> initialReminders) {
        CreateOneClickResult res = new CreateOneClickResult();
        String remindersJson = StorageService.serializeReminders(initialReminders);
        if (remindersJson == null || remindersJson.trim().isEmpty()) {
            remindersJson = "[]";
        }

        HttpURLConnection conn = null;
        try {
            URL url = new URL("https://jsonblob.com/api/jsonBlob");
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("POST");
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(15000);
            conn.setRequestProperty("User-Agent", "StreloReminder-Android/1.4");
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            conn.setRequestProperty("Accept", "application/json");
            conn.setDoOutput(true);

            byte[] bytes = remindersJson.getBytes(StandardCharsets.UTF_8);
            try (OutputStream os = conn.getOutputStream()) {
                os.write(bytes);
                os.flush();
            }

            int code = conn.getResponseCode();
            if (code >= 200 && code < 300) {
                String location = conn.getHeaderField("Location");
                if (location == null || location.isEmpty()) {
                    location = conn.getHeaderField("location");
                }
                String blobHeader = conn.getHeaderField("X-jsonblob");
                if (blobHeader == null || blobHeader.isEmpty()) {
                    blobHeader = conn.getHeaderField("x-jsonblob");
                }

                if (blobHeader != null && !blobHeader.trim().isEmpty()) {
                    res.roomId = blobHeader.trim();
                } else if (location != null && !location.trim().isEmpty()) {
                    String cleanLoc = location.trim();
                    while (cleanLoc.endsWith("/")) {
                        cleanLoc = cleanLoc.substring(0, cleanLoc.length() - 1);
                    }
                    int lastSlash = cleanLoc.lastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash + 1 < cleanLoc.length()) {
                        res.roomId = cleanLoc.substring(lastSlash + 1).trim();
                    }
                }

                if (!res.roomId.isEmpty()) {
                    res.cloudUrl = "https://jsonblob.com/api/jsonBlob/" + res.roomId;
                    res.success = true;
                    return res;
                }
            }

            res.success = false;
            res.errorMessage = "Облачный сервис не вернул ID комнаты (HTTP " + code + ").";
            return res;
        } catch (Exception e) {
            res.success = false;
            res.errorMessage = "Не удалось создать облако в 1 клик: " + e.getMessage();
            return res;
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
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
                    payload.toString()
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
                String remoteJson = fetchRemoteRemindersJson(config);
                List<ReminderItem> remoteList = StorageService.deserializeReminders(remoteJson);

                boolean[] flags = new boolean[2];
                List<ReminderItem> merged = mergeReminders(workingList, remoteList, flags);

                if (flags[1] || forcePush || lanSuccess || remoteJson == null || remoteJson.trim().isEmpty()) {
                    String mergedJson = StorageService.serializeReminders(merged);
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

    private static String fetchRemoteRemindersJson(CloudSyncConfig config) throws Exception {
        if (config.providerTypeValue == CloudSyncConfig.PROVIDER_ONE_CLICK_CLOUD) {
            String url = config.getEffectiveOneClickUrl();
            if (url.isEmpty()) return "[]";
            String response = sendHttpRequest(url, "GET", null, null);
            if (response == null || response.trim().isEmpty() || "null".equalsIgnoreCase(response.trim())) {
                return "[]";
            }
            return response;
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_GITHUB_GIST) {
            String url = "https://api.github.com/gists/" + config.gistId.trim();
            String response = sendHttpRequest(url, "GET", config.gitHubToken.trim(), null);
            JSONObject root = new JSONObject(response);
            JSONObject files = root.optJSONObject("files");
            if (files != null) {
                JSONObject fileObj = files.optJSONObject("strelo_reminders.json");
                if (fileObj != null) {
                    return fileObj.optString("content", "[]");
                }
            }
            return "[]";
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_CUSTOM_URL) {
            String response = sendHttpRequest(config.customCloudUrl.trim(), "GET", null, null);
            if (response == null || response.trim().isEmpty() || "null".equalsIgnoreCase(response.trim())) {
                return "[]";
            }
            return response;
        }
        return "[]";
    }

    private static void pushRemoteRemindersJson(CloudSyncConfig config, String remindersJson) throws Exception {
        if (config.providerTypeValue == CloudSyncConfig.PROVIDER_ONE_CLICK_CLOUD) {
            String url = config.getEffectiveOneClickUrl();
            if (!url.isEmpty()) {
                sendHttpRequest(url, "PUT", null, (remindersJson == null || remindersJson.isEmpty()) ? "[]" : remindersJson);
            }
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_GITHUB_GIST) {
            String url = "https://api.github.com/gists/" + config.gistId.trim();
            JSONObject fileContent = new JSONObject();
            fileContent.put("content", (remindersJson == null || remindersJson.isEmpty()) ? "[]" : remindersJson);

            JSONObject files = new JSONObject();
            files.put("strelo_reminders.json", fileContent);

            JSONObject payload = new JSONObject();
            payload.put("description", "StreloReminder Cloud Sync (PC <-> Android)");
            payload.put("files", files);

            sendHttpRequest(url, "PATCH", config.gitHubToken.trim(), payload.toString());
        } else if (config.providerTypeValue == CloudSyncConfig.PROVIDER_CUSTOM_URL) {
            sendHttpRequest(config.customCloudUrl.trim(), "PUT", null, remindersJson);
        }
    }

    private static String sendHttpRequest(String urlStr, String method, String bearerToken, String jsonBody) throws Exception {
        URL url = new URL(urlStr);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        try {
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(15000);
            conn.setUseCaches(false);
            conn.setRequestProperty("User-Agent", "StreloReminder-Android/1.4");
            conn.setRequestProperty("Accept", "application/vnd.github+json, application/json");

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

            if (jsonBody != null && !jsonBody.isEmpty()) {
                conn.setDoOutput(true);
                conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                byte[] bytes = jsonBody.getBytes(StandardCharsets.UTF_8);
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
