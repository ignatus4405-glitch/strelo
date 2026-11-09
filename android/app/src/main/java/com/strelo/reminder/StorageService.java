package com.strelo.reminder;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

public class StorageService {
    private static final String PREFS_NAME = "strelo_reminder_prefs";
    private static final String KEY_REMINDERS_JSON = "reminders_json";
    private static final String KEY_CLOUD_CONFIG_JSON = "cloud_config_json";
    private static final String KEY_DO_NOT_DISTURB = "do_not_disturb";

    public static synchronized List<ReminderItem> loadReminders(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        String json = prefs.getString(KEY_REMINDERS_JSON, "[]");
        return deserializeReminders(json);
    }

    public static synchronized void saveReminders(Context context, List<ReminderItem> reminders) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        String json = serializeReminders(reminders);
        prefs.edit().putString(KEY_REMINDERS_JSON, json).apply();
    }

    public static String serializeReminders(List<ReminderItem> reminders) {
        JSONArray arr = new JSONArray();
        if (reminders != null) {
            for (ReminderItem item : reminders) {
                if (item != null) {
                    try {
                        arr.put(item.toJson());
                    } catch (Exception ignored) {
                    }
                }
            }
        }
        return arr.toString();
    }

    public static List<ReminderItem> deserializeReminders(String json) {
        List<ReminderItem> list = new ArrayList<>();
        if (json == null || json.trim().isEmpty()) {
            return list;
        }
        try {
            JSONArray arr = new JSONArray(json.trim());
            for (int i = 0; i < arr.length(); i++) {
                JSONObject obj = arr.optJSONObject(i);
                if (obj != null) {
                    ReminderItem item = ReminderItem.fromJson(obj);
                    if (item != null) {
                        list.add(item);
                    }
                }
            }
        } catch (Exception ignored) {
        }
        return list;
    }

    public static CloudSyncConfig loadCloudConfig(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        String json = prefs.getString(KEY_CLOUD_CONFIG_JSON, "");
        if (json == null || json.isEmpty()) {
            return new CloudSyncConfig();
        }
        try {
            return CloudSyncConfig.fromJson(new JSONObject(json));
        } catch (Exception e) {
            return new CloudSyncConfig();
        }
    }

    public static void saveCloudConfig(Context context, CloudSyncConfig config) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        String json = config != null ? config.toJson().toString() : "";
        prefs.edit().putString(KEY_CLOUD_CONFIG_JSON, json).apply();
    }

    public static boolean isDoNotDisturb(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        return prefs.getBoolean(KEY_DO_NOT_DISTURB, false);
    }

    public static void setDoNotDisturb(Context context, boolean value) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        prefs.edit().putBoolean(KEY_DO_NOT_DISTURB, value).apply();
    }
}
