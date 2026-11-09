package com.strelo.reminder;

import android.app.NotificationManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

import java.util.List;

public class ReminderAlarmReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(final Context context, Intent intent) {
        if (context == null || intent == null) return;

        String action = intent.getAction();
        List<ReminderItem> list = StorageService.loadReminders(context);

        if (Intent.ACTION_BOOT_COMPLETED.equals(action)
                || Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)
                || "android.intent.action.QUICKBOOT_POWERON".equals(action)) {
            AlarmScheduler.rescheduleAll(context, list);
            return;
        }

        String id = intent.getStringExtra(AlarmScheduler.EXTRA_REMINDER_ID);
        if (id == null) return;

        if (AlarmScheduler.ACTION_COMPLETE_REMINDER.equals(action)) {
            OverlayAlertManager.dismissOverlay();
            cancelNotification(context, id);
            for (ReminderItem item : list) {
                if (item != null && id.equals(item.id)) {
                    item.advanceAfterComplete(System.currentTimeMillis());
                    break;
                }
            }
            StorageService.saveReminders(context, list);
            AlarmScheduler.rescheduleAll(context, list);
            syncInBackground(context, list);
            return;
        }

        if (AlarmScheduler.ACTION_SNOOZE_10_REMINDER.equals(action)) {
            OverlayAlertManager.dismissOverlay();
            cancelNotification(context, id);
            for (ReminderItem item : list) {
                if (item != null && id.equals(item.id)) {
                    item.snoozeByOffset(0, 0, 0, 0, 10, 0);
                    break;
                }
            }
            StorageService.saveReminders(context, list);
            AlarmScheduler.rescheduleAll(context, list);
            syncInBackground(context, list);
            return;
        }

        if (AlarmScheduler.ACTION_TRIGGER_REMINDER.equals(action)) {
            for (ReminderItem item : list) {
                if (item != null && !item.isDeleted && item.isEnabled && !item.isCompleted && id.equals(item.id)) {
                    AlarmScheduler.triggerReminderNotificationAndPopup(context, item);
                    break;
                }
            }
        }
    }

    private void cancelNotification(Context context, String id) {
        try {
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null && id != null) {
                nm.cancel(id.hashCode());
            }
        } catch (Exception ignored) {
        }
    }

    private void syncInBackground(final Context context, final List<ReminderItem> list) {
        final CloudSyncConfig cfg = StorageService.loadCloudConfig(context);
        if (cfg == null || !cfg.isConfigured()) return;
        new Thread(new Runnable() {
            @Override
            public void run() {
                CloudSyncService.SyncResult res = CloudSyncService.synchronize(cfg, list, true);
                if (res.success && res.mergedReminders != null) {
                    StorageService.saveReminders(context, res.mergedReminders);
                    AlarmScheduler.rescheduleAll(context, res.mergedReminders);
                }
            }
        }).start();
    }
}
