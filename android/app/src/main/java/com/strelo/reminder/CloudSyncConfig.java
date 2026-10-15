package com.strelo.reminder;

import android.net.Uri;
import android.util.Base64;

import org.json.JSONObject;

import java.nio.charset.StandardCharsets;

public class CloudSyncConfig {
    public static final int PROVIDER_DISABLED = 0;
    public static final int PROVIDER_GITHUB_GIST = 1;
    public static final int PROVIDER_CUSTOM_URL = 2;
    public static final int PROVIDER_ONE_CLICK_CLOUD = 3;

    private static final String CODE_PREFIX = "STRELO1:";
    private static final String DEEP_LINK_PREFIX = "strelo://sync?code=";

    public int providerTypeValue = PROVIDER_DISABLED;
    public String gitHubToken = "";
    public String gistId = "";
    public String customCloudUrl = "";
    public int autoSyncIntervalSeconds = 15;
    public String oneClickRoomId = "";
    public boolean lanWifiSyncEnabled = true;
    public String lanPcIp = "";
    public int lanPort = 45892;

    public boolean isCloudConfigured() {
        if (providerTypeValue == PROVIDER_ONE_CLICK_CLOUD) {
            return oneClickRoomId != null && !sanitizeRoomId(oneClickRoomId).isEmpty();
        }
        if (providerTypeValue == PROVIDER_GITHUB_GIST) {
            return gitHubToken != null && !gitHubToken.trim().isEmpty()
                    && gistId != null && !gistId.trim().isEmpty();
        }
        if (providerTypeValue == PROVIDER_CUSTOM_URL) {
            if (customCloudUrl == null) return false;
            String u = customCloudUrl.trim().toLowerCase();
            return u.startsWith("http://") || u.startsWith("https://");
        }
        return false;
    }

    public boolean isConfigured() {
        return isCloudConfigured() || lanWifiSyncEnabled;
    }

    public String getEffectiveOneClickUrl() {
        String clean = sanitizeRoomId(oneClickRoomId);
        if (!clean.isEmpty()) {
            return "https://ntfy.sh/strelo_room_" + clean;
        }
        return "";
    }

    public static String sanitizeRoomId(String rawRoomId) {
        if (rawRoomId == null || rawRoomId.trim().isEmpty()) {
            return "";
        }
        String trimmed = rawRoomId.trim();
        while (trimmed.endsWith("/")) {
            trimmed = trimmed.substring(0, trimmed.length() - 1);
        }
        int lastSlash = trimmed.lastIndexOf('/');
        if (lastSlash >= 0 && lastSlash + 1 < trimmed.length()) {
            trimmed = trimmed.substring(lastSlash + 1);
        }
        StringBuilder sb = new StringBuilder(trimmed.length());
        for (int i = 0; i < trimmed.length(); i++) {
            char c = trimmed.charAt(i);
            if ((c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-' || c == '_') {
                sb.append(Character.toLowerCase(c));
            }
        }
        return sb.toString();
    }

    public JSONObject toJson() {
        JSONObject obj = new JSONObject();
        try {
            obj.put("ProviderTypeValue", providerTypeValue);
            obj.put("GitHubToken", gitHubToken != null ? gitHubToken : "");
            obj.put("GistId", gistId != null ? gistId : "");
            obj.put("CustomCloudUrl", customCloudUrl != null ? customCloudUrl : "");
            obj.put("AutoSyncIntervalSeconds", Math.max(3, autoSyncIntervalSeconds));
            obj.put("OneClickRoomId", oneClickRoomId != null ? sanitizeRoomId(oneClickRoomId) : "");
            obj.put("LanWifiSyncEnabled", lanWifiSyncEnabled);
            obj.put("LanPcIp", lanPcIp != null ? lanPcIp : "");
            obj.put("LanPort", lanPort > 0 ? lanPort : 45892);
        } catch (Exception ignored) {
        }
        return obj;
    }

    public static CloudSyncConfig fromJson(JSONObject obj) {
        CloudSyncConfig cfg = new CloudSyncConfig();
        if (obj == null) return cfg;
        cfg.providerTypeValue = obj.optInt("ProviderTypeValue", PROVIDER_DISABLED);
        cfg.gitHubToken = obj.optString("GitHubToken", "");
        cfg.gistId = obj.optString("GistId", "");
        cfg.customCloudUrl = obj.optString("CustomCloudUrl", "");
        cfg.autoSyncIntervalSeconds = Math.max(3, obj.optInt("AutoSyncIntervalSeconds", 15));
        cfg.oneClickRoomId = sanitizeRoomId(obj.optString("OneClickRoomId", ""));
        cfg.lanWifiSyncEnabled = obj.optBoolean("LanWifiSyncEnabled", true);
        cfg.lanPcIp = obj.optString("LanPcIp", "");
        cfg.lanPort = obj.optInt("LanPort", 45892);
        if (cfg.lanPort <= 0) cfg.lanPort = 45892;
        return cfg;
    }

    public String toConnectionCode() {
        try {
            byte[] jsonBytes = toJson().toString().getBytes(StandardCharsets.UTF_8);
            String b64 = Base64.encodeToString(jsonBytes, Base64.NO_WRAP);
            return CODE_PREFIX + b64;
        } catch (Exception e) {
            return "";
        }
    }

    public String toQrDeepLink() {
        if (providerTypeValue == PROVIDER_ONE_CLICK_CLOUD || providerTypeValue == PROVIDER_DISABLED) {
            StringBuilder sb = new StringBuilder("strelo://sync?v=1");
            sb.append("&p=").append(providerTypeValue);
            if (oneClickRoomId != null && !oneClickRoomId.trim().isEmpty()) {
                sb.append("&r=").append(Uri.encode(sanitizeRoomId(oneClickRoomId)));
            }
            sb.append("&w=").append(lanWifiSyncEnabled ? "1" : "0");
            if (lanPcIp != null && !lanPcIp.trim().isEmpty()) {
                sb.append("&ip=").append(Uri.encode(lanPcIp.trim()));
            }
            if (lanPort > 0 && lanPort != 45892) {
                sb.append("&pt=").append(lanPort);
            }
            sb.append("&i=").append(Math.max(3, autoSyncIntervalSeconds));
            return sb.toString();
        }

        String code = toConnectionCode();
        if (code.isEmpty()) return "";
        return DEEP_LINK_PREFIX + Uri.encode(code);
    }

    public static CloudSyncConfig fromConnectionCode(String code) {
        if (code == null || code.trim().isEmpty()) return null;
        String trimmed = code.trim();

        // Компактный формат QR-кода strelo://sync?v=1&p=...
        if (trimmed.toLowerCase().startsWith("strelo://sync?") && trimmed.toLowerCase().contains("v=1")) {
            try {
                Uri uri = Uri.parse(trimmed);
                CloudSyncConfig cfg = new CloudSyncConfig();
                String p = uri.getQueryParameter("p");
                if (p != null) cfg.providerTypeValue = Integer.parseInt(p.trim());
                String r = uri.getQueryParameter("r");
                if (r != null) {
                    cfg.oneClickRoomId = sanitizeRoomId(r);
                }
                String w = uri.getQueryParameter("w");
                if (w != null) cfg.lanWifiSyncEnabled = "1".equals(w) || "true".equalsIgnoreCase(w);
                String ip = uri.getQueryParameter("ip");
                if (ip != null) cfg.lanPcIp = ip.trim();
                String pt = uri.getQueryParameter("pt");
                if (pt != null) cfg.lanPort = Integer.parseInt(pt.trim());
                String i = uri.getQueryParameter("i");
                if (i != null) cfg.autoSyncIntervalSeconds = Math.max(3, Integer.parseInt(i.trim()));
                return cfg;
            } catch (Exception ignored) {
            }
        }

        int paramIdx = trimmed.toLowerCase().indexOf("code=");
        if (paramIdx >= 0) {
            trimmed = Uri.decode(trimmed.substring(paramIdx + 5).trim());
        }

        if (trimmed.regionMatches(true, 0, CODE_PREFIX, 0, CODE_PREFIX.length())) {
            trimmed = trimmed.substring(CODE_PREFIX.length()).trim();
        }

        try {
            byte[] decoded = Base64.decode(trimmed, Base64.DEFAULT);
            String json = new String(decoded, StandardCharsets.UTF_8);
            JSONObject obj = new JSONObject(json);
            return fromJson(obj);
        } catch (Exception ignored) {
        }

        // Если пользователь ввёл короткий ID комнаты 1-клик облака
        if (trimmed.length() >= 6 && trimmed.length() <= 64 && !trimmed.contains(" ")) {
            CloudSyncConfig cfg = new CloudSyncConfig();
            cfg.providerTypeValue = PROVIDER_ONE_CLICK_CLOUD;
            cfg.oneClickRoomId = sanitizeRoomId(trimmed);
            cfg.lanWifiSyncEnabled = true;
            return cfg;
        }

        return null;
    }
}
