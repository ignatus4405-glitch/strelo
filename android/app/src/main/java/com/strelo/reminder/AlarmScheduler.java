package com.strelo.reminder;

import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;

import java.util.List;

public class AlarmScheduler {
    public static final String ACTION_TRIGGER_REMINDER = "com.strelo.reminder.ACTION_TRIGGER_REMINDER";
    public static final String EXTRA_REMINDER_ID = "extra_reminder_id";
    public static final String CHANNEL_ID = "strelo_alerts_channel";

    public static void ensureNotificationChannel(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm == null) return;

            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Напоминания Strelo",
                    NotificationManager.IMPORTANCE_HIGH
            );
            channel.setDescription("Всплывающие уведомления и звуковые сигналы напоминаний Strelo");
            channel.enableVibration(true);
            nm.createNotificationChannel(channel);
        }
    }

    public static void rescheduleAll(Context context, List<ReminderItem> reminders) {
        AlarmManager am = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        if (am == null || reminders == null) return;

        long now = System.currentTimeMillis();
        for (ReminderItem item : reminders) {
            if (item == null || item.id == null) continue;

            Intent intent = new Intent(context, ReminderAlarmReceiver.class);
            intent.setAction(ACTION_TRIGGER_REMINDER);
            intent.putExtra(EXTRA_REMINDER_ID, item.id);

            int reqCode = item.id.hashCode();
            int flags = PendingIntent.FLAG_UPDATE_CURRENT;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                flags |= PendingIntent.FLAG_IMMUTABLE;
            }
            PendingIntent pi = PendingIntent.getBroadcast(context, reqCode, intent, flags);

            if (!item.isDeleted && item.isEnabled && !item.isCompleted) {
                long triggerAt = item.getTriggerTimeMillis();
                if (triggerAt > now - 60000L) {
                    long scheduleTime = Math.max(now + 1000L, triggerAt);
                    try {
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                            am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, scheduleTime, pi);
                        } else {
                            am.setExact(AlarmManager.RTC_WAKEUP, scheduleTime, pi);
                        }
                    } catch (SecurityException se) {
                        // Если на Android 12+ еще не выдано разрешение SCHEDULE_EXACT_ALARM, используем неточный будильник
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, scheduleTime, pi);
                        } else {
                            am.set(AlarmManager.RTC_WAKEUP, scheduleTime, pi);
                        }
                    }
                }
            } else {
                am.cancel(pi);
            }
        }
    }

    public static void triggerReminderNotificationAndPopup(Context context, ReminderItem item) {
        if (item == null || StorageService.isDoNotDisturb(context)) {
            return;
        }

        ensureNotificationChannel(context);

        Intent alertIntent = new Intent(context, ReminderAlertActivity.class);
        alertIntent.putExtra(EXTRA_REMINDER_ID, item.id);
        alertIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);

        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        PendingIntent fullScreenPi = PendingIntent.getActivity(context, item.id.hashCode(), alertIntent, flags);

        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(context, CHANNEL_ID);
        } else {
            builder = new Notification.Builder(context);
            builder.setPriority(Notification.PRIORITY_MAX);
            if (item.playSound) {
                Uri soundUri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
                builder.setSound(soundUri);
            }
        }

        String badge = item.priorityValue == ReminderItem.PRIORITY_HIGH
                ? "⚡ ВАЖНОЕ НАПОМИНАНИЕ"
                : "🔔 Напоминание Strelo";

        String body = (item.notes != null && !item.notes.trim().isEmpty())
                ? item.title + " — " + item.notes
                : item.title;

        builder.setSmallIcon(R.drawable.ic_bell)
                .setContentTitle(badge + ": " + item.title)
                .setContentText(body)
                .setAutoCancel(true)
                .setContentIntent(fullScreenPi)
                .setFullScreenIntent(fullScreenPi, true);

        NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) {
            nm.notify(item.id.hashCode(), builder.build());
        }

        try {
            context.startActivity(alertIntent);
        } catch (Exception ignored) {
        }
    }
}
