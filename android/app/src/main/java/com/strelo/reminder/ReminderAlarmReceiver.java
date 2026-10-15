package com.strelo.reminder;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

import java.util.List;

public class ReminderAlarmReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (context == null || intent == null) return;

        String action = intent.getAction();
        List<ReminderItem> list = StorageService.loadReminders(context);

        if (Intent.ACTION_BOOT_COMPLETED.equals(action)) {
            AlarmScheduler.rescheduleAll(context, list);
            return;
        }

        if (AlarmScheduler.ACTION_TRIGGER_REMINDER.equals(action)) {
            String reminderId = intent.getStringExtra(AlarmScheduler.EXTRA_REMINDER_ID);
            if (reminderId == null || reminderId.isEmpty()) return;

            for (ReminderItem item : list) {
                if (item != null && reminderId.equals(item.id) && !item.isDeleted && item.isEnabled && !item.isCompleted) {
                    AlarmScheduler.triggerReminderNotificationAndPopup(context, item);
                    break;
                }
            }
        }
    }
}
