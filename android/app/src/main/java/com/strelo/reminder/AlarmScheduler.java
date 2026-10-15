package com.strelo.reminder;

import android.app.AlarmManager;
import android.app.KeyguardManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.media.AudioAttributes;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;
import android.os.PowerManager;

import java.util.List;

public class AlarmScheduler {
    public static final String ACTION_TRIGGER_REMINDER = "com.strelo.reminder.ACTION_TRIGGER_REMINDER";
    public static final String ACTION_COMPLETE_REMINDER = "com.strelo.reminder.ACTION_COMPLETE_REMINDER";
    public static final String ACTION_SNOOZE_10_REMINDER = "com.strelo.reminder.ACTION_SNOOZE_10_REMINDER";
    public static final String EXTRA_REMINDER_ID = "extra_reminder_id";
    public static final String CHANNEL_ID = "strelo_alerts_popup_v2";

    public static void ensureNotificationChannel(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm == null) return;

            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Всплывающие напоминания Strelo (поверх окон)",
                    NotificationManager.IMPORTANCE_HIGH
            );
            channel.setDescription("Экстренные всплывающие уведомления и звуковые сигналы напоминаний Strelo");
            channel.enableVibration(true);
            channel.setVibrationPattern(new long[]{0, 400, 200, 400});
            channel.setLockscreenVisibility(Notification.VISIBILITY_PUBLIC);
            channel.setBypassDnd(true);

            try {
                Uri soundUri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
                if (soundUri == null) {
                    soundUri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM);
                }
                AudioAttributes attrs = new AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_ALARM)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                        .build();
                channel.setSound(soundUri, attrs);
            } catch (Exception ignored) {
            }

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

        // Будим экран при срабатывании напоминания
        try {
            PowerManager pm = (PowerManager) context.getSystemService(Context.POWER_SERVICE);
            if (pm != null && !pm.isInteractive()) {
                PowerManager.WakeLock wl = pm.newWakeLock(
                        PowerManager.PARTIAL_WAKE_LOCK | PowerManager.ACQUIRE_CAUSES_WAKEUP,
                        "strelo:reminder_wakeup"
                );
                wl.acquire(5000L);
            }
        } catch (Exception ignored) {
        }

        Intent alertIntent = new Intent(context, ReminderAlertActivity.class);
        alertIntent.putExtra(EXTRA_REMINDER_ID, item.id);
        alertIntent.addFlags(
                Intent.FLAG_ACTIVITY_NEW_TASK
                        | Intent.FLAG_ACTIVITY_CLEAR_TOP
                        | Intent.FLAG_ACTIVITY_SINGLE_TOP
        );

        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        PendingIntent fullScreenPi = PendingIntent.getActivity(context, item.id.hashCode(), alertIntent, flags);

        Intent completeIntent = new Intent(context, ReminderAlarmReceiver.class);
        completeIntent.setAction(ACTION_COMPLETE_REMINDER);
        completeIntent.putExtra(EXTRA_REMINDER_ID, item.id);
        PendingIntent completePi = PendingIntent.getBroadcast(context, item.id.hashCode() + 1, completeIntent, flags);

        Intent snooze10Intent = new Intent(context, ReminderAlarmReceiver.class);
        snooze10Intent.setAction(ACTION_SNOOZE_10_REMINDER);
        snooze10Intent.putExtra(EXTRA_REMINDER_ID, item.id);
        PendingIntent snooze10Pi = PendingIntent.getBroadcast(context, item.id.hashCode() + 2, snooze10Intent, flags);

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
                .setStyle(new Notification.BigTextStyle().bigText(body))
                .setCategory(Notification.CATEGORY_ALARM)
                .setVisibility(Notification.VISIBILITY_PUBLIC)
                .setOngoing(true)
                .setAutoCancel(true)
                .setContentIntent(fullScreenPi)
                .setFullScreenIntent(fullScreenPi, true)
                .addAction(android.R.drawable.checkbox_on_background, "✓ Выполнено", completePi)
                .addAction(android.R.drawable.ic_popup_reminder, "+10 мин", snooze10Pi);

        NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) {
            nm.notify(item.id.hashCode(), builder.build());
        }

        // 1. Если выдано разрешение «Поверх других окон» — выводим плавающее окно прямо поверх любого приложения
        boolean overlayShown = false;
        if (OverlayAlertManager.canDrawOverlays(context)) {
            OverlayAlertManager.showOverlayPopup(context, item);
            overlayShown = true;
        }

        // 2. Если экран заблокирован или разрешение на оверлей ещё не выдано — запускаем полноэкранную Activity
        boolean isLocked = false;
        try {
            KeyguardManager km = (KeyguardManager) context.getSystemService(Context.KEYGUARD_SERVICE);
            if (km != null && km.isKeyguardLocked()) {
                isLocked = true;
            }
        } catch (Exception ignored) {
        }

        if (isLocked || !overlayShown) {
            try {
                context.startActivity(alertIntent);
            } catch (Exception ignored) {
            }
        }
    }
}
