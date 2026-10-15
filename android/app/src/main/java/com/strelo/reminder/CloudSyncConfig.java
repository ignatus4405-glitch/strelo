package com.strelo.reminder;

import android.util.Base64;

import org.json.JSONObject;

import java.nio.charset.StandardCharsets;

public class CloudSyncConfig {
    public static final int PROVIDER_DISABLED = 0;
    public static final int PROVIDER_GITHUB_GIST = 1;
    public static final int PROVIDER_CUSTOM_URL = 2;

    private static final String CODE_PREFIX = "STRELO1:";

    public int providerTypeValue = PROVIDER_DISABLED;
    public String gitHubToken = "";
    public String gistId = "";
    public String customCloudUrl = "";
    public int autoSyncIntervalSeconds = 20;

    public boolean isConfigured() {
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

    public JSONObject toJson() {
        JSONObject obj = new JSONObject();
        try {
            obj.put("ProviderTypeValue", providerTypeValue);
            obj.put("GitHubToken", gitHubToken != null ? gitHubToken : "");
            obj.put("GistId", gistId != null ? gistId : "");
            obj.put("CustomCloudUrl", customCloudUrl != null ? customCloudUrl : "");
            obj.put("AutoSyncIntervalSeconds", autoSyncIntervalSeconds);
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
        cfg.autoSyncIntervalSeconds = Math.max(10, obj.optInt("AutoSyncIntervalSeconds", 20));
        return cfg;
    }

    public String toConnectionCode() {
        try {
            String json = toJson().toString();
            String b64 = Base64.encodeToString(json.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
            return CODE_PREFIX + b64;
        } catch (Exception e) {
            return "";
        }
    }

    public static CloudSyncConfig fromConnectionCode(String code) {
        if (code == null || code.trim().isEmpty()) return null;
        String trimmed = code.trim();
        if (trimmed.regionMatches(true, 0, CODE_PREFIX, 0, CODE_PREFIX.length())) {
            trimmed = trimmed.substring(CODE_PREFIX.length()).trim();
        }
        try {
            byte[] decoded = Base64.decode(trimmed, Base64.DEFAULT);
            String json = new String(decoded, StandardCharsets.UTF_8);
            JSONObject obj = new JSONObject(json);
            return fromJson(obj);
        } catch (Exception e) {
            return null;
        }
    }
}
