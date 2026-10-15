package com.strelo.reminder;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

public class UpdateService {
    public static final String CURRENT_VERSION = "1.3.0";
    public static final String GITHUB_REPO = "ignatus4405-glitch/strelo";
    public static final String RELEASES_PAGE_URL = "https://github.com/" + GITHUB_REPO + "/releases";

    public static class UpdateCheckResult {
        public boolean success;
        public boolean updateAvailable;
        public String currentVersion = CURRENT_VERSION;
        public String latestVersion = CURRENT_VERSION;
        public String releaseTitle = "";
        public String releaseNotes = "";
        public String releaseUrl = RELEASES_PAGE_URL;
        public String apkDownloadUrl = "";
        public String errorMessage = "";
    }

    public static UpdateCheckResult checkForUpdates() {
        UpdateCheckResult res = new UpdateCheckResult();
        HttpURLConnection conn = null;
        try {
            URL url = new URL("https://api.github.com/repos/" + GITHUB_REPO + "/releases/latest");
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setConnectTimeout(12000);
            conn.setReadTimeout(12000);
            conn.setRequestProperty("User-Agent", "StreloReminder-Android-Updater");
            conn.setRequestProperty("Accept", "application/vnd.github+json");

            int code = conn.getResponseCode();
            if (code == 404) {
                res.success = true;
                res.updateAvailable = false;
                return res;
            }

            InputStream stream = (code >= 200 && code < 300) ? conn.getInputStream() : conn.getErrorStream();
            String body = readStream(stream);

            if (code < 200 || code >= 300) {
                res.success = false;
                res.errorMessage = "Ошибка GitHub API (HTTP " + code + ")";
                return res;
            }

            JSONObject json = new JSONObject(body);
            String tagName = json.optString("tag_name", "");
            if (tagName == null || tagName.trim().isEmpty()) {
                res.success = true;
                res.updateAvailable = false;
                return res;
            }

            String latestClean = normalizeVersion(tagName);
            res.latestVersion = latestClean;
            res.releaseTitle = json.optString("name", tagName);
            res.releaseNotes = json.optString("body", "");
            res.releaseUrl = json.optString("html_url", RELEASES_PAGE_URL);

            JSONArray assets = json.optJSONArray("assets");
            if (assets != null) {
                for (int i = 0; i < assets.length(); i++) {
                    JSONObject asset = assets.optJSONObject(i);
                    if (asset != null) {
                        String dl = asset.optString("browser_download_url", "");
                        if (dl.toLowerCase().endsWith(".apk")) {
                            res.apkDownloadUrl = dl;
                            break;
                        }
                    }
                }
            }

            res.updateAvailable = isNewerVersion(CURRENT_VERSION, latestClean);
            res.success = true;
            return res;
        } catch (Exception ex) {
            res.success = false;
            res.errorMessage = "Не удалось проверить обновления: " + ex.getMessage();
            return res;
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
    }

    public static String normalizeVersion(String raw) {
        if (raw == null) return "0.0.0";
        String s = raw.trim();
        if (s.startsWith("v") || s.startsWith("V")) {
            s = s.substring(1).trim();
        }
        int dashIdx = s.indexOf('-');
        if (dashIdx > 0) {
            s = s.substring(0, dashIdx);
        }
        return s.isEmpty() ? "0.0.0" : s;
    }

    public static boolean isNewerVersion(String currentVer, String latestVer) {
        int[] cur = parseVersionParts(normalizeVersion(currentVer));
        int[] lat = parseVersionParts(normalizeVersion(latestVer));
        int maxLen = Math.max(cur.length, lat.length);
        for (int i = 0; i < maxLen; i++) {
            int c = i < cur.length ? cur[i] : 0;
            int l = i < lat.length ? lat[i] : 0;
            if (l > c) return true;
            if (l < c) return false;
        }
        return false;
    }

    private static int[] parseVersionParts(String v) {
        String[] parts = v.split("\\.");
        int[] nums = new int[parts.length];
        for (int i = 0; i < parts.length; i++) {
            try {
                nums[i] = Integer.parseInt(parts[i].trim());
            } catch (NumberFormatException ignored) {
                nums[i] = 0;
            }
        }
        return nums;
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
