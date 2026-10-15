package com.strelo.reminder;

import android.app.Activity;
import android.app.NotificationManager;
import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.media.Ringtone;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Vibrator;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import java.util.List;

public class ReminderAlertActivity extends Activity {

    private ReminderItem currentReminder;
    private Ringtone activeRingtone;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setShowWhenLocked(true);
            setTurnScreenOn(true);
        } else {
            getWindow().addFlags(
                    WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED
                            | WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON
                            | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON
            );
        }

        String reminderId = getIntent() != null
                ? getIntent().getStringExtra(AlarmScheduler.EXTRA_REMINDER_ID)
                : null;

        List<ReminderItem> list = StorageService.loadReminders(this);
        if (reminderId != null) {
            for (ReminderItem r : list) {
                if (r != null && reminderId.equals(r.id)) {
                    currentReminder = r;
                    break;
                }
            }
        }

        if (currentReminder == null) {
            currentReminder = new ReminderItem();
            currentReminder.title = "Пример напоминания: Сделать перерыв!";
            currentReminder.notes = "Это демонстрационное всплывающее окно уведомления Strelo на Android.";
        }

        buildAlertUi();
        playAlertSoundAndVibrate();
    }

    private void buildAlertUi() {
        boolean isHigh = currentReminder.priorityValue == ReminderItem.PRIORITY_HIGH;
        int headerColor = isHigh ? Color.parseColor("#B91C1C") : Color.parseColor("#1E40AF");

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.parseColor("#F1F5F9"));

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);

        // Шапка
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.VERTICAL);
        header.setBackgroundColor(headerColor);
        header.setPadding(dp(20), dp(22), dp(20), dp(18));

        TextView tvBadge = new TextView(this);
        tvBadge.setText(isHigh ? "⚡ ВАЖНОЕ НАПОМИНАНИЕ" : "🔔 НАПОМИНАНИЕ STRELO");
        tvBadge.setTextColor(Color.WHITE);
        tvBadge.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        tvBadge.setTypeface(Typeface.DEFAULT_BOLD);

        TextView tvMeta = new TextView(this);
        tvMeta.setText("Назначено: " + currentReminder.getFormattedTriggerTime()
                + "  •  Повтор: " + currentReminder.getRecurrenceDisplayText());
        tvMeta.setTextColor(Color.parseColor("#E2E8F0"));
        tvMeta.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        tvMeta.setPadding(0, dp(6), 0, 0);

        header.addView(tvBadge);
        header.addView(tvMeta);
        root.addView(header);

        // Основной блок
        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        body.setPadding(dp(16), dp(16), dp(16), dp(20));

        // Карточка текста напоминания
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(18), dp(16), dp(18), dp(18));
        GradientDrawable cardBg = new GradientDrawable();
        cardBg.setColor(Color.WHITE);
        cardBg.setCornerRadius(dp(10));
        cardBg.setStroke(dp(1), Color.parseColor("#CBD5E1"));
        card.setBackground(cardBg);

        TextView tvTitle = new TextView(this);
        tvTitle.setText(currentReminder.title);
        tvTitle.setTextColor(Color.parseColor("#0F172A"));
        tvTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        tvTitle.setTypeface(Typeface.DEFAULT_BOLD);

        TextView tvNotes = new TextView(this);
        boolean hasNotes = currentReminder.notes != null && !currentReminder.notes.trim().isEmpty();
        tvNotes.setText(hasNotes ? currentReminder.notes : "Дополнительное описание не указано.");
        tvNotes.setTextColor(hasNotes ? Color.parseColor("#334155") : Color.parseColor("#94A3B8"));
        tvNotes.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        tvNotes.setPadding(0, dp(10), 0, 0);

        card.addView(tvTitle);
        card.addView(tvNotes);
        body.addView(card);

        // Заголовок «Отложить»
        TextView tvSnoozeCaption = new TextView(this);
        tvSnoozeCaption.setText("Отложить напоминание через:");
        tvSnoozeCaption.setTextColor(Color.parseColor("#334155"));
        tvSnoozeCaption.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        tvSnoozeCaption.setTypeface(Typeface.DEFAULT_BOLD);
        tvSnoozeCaption.setPadding(0, dp(18), 0, dp(8));
        body.addView(tvSnoozeCaption);

        // Быстрые кнопки откладывания
        LinearLayout quickRow = new LinearLayout(this);
        quickRow.setOrientation(LinearLayout.HORIZONTAL);

        final String[] quickLabels = new String[]{"+30 сек", "+5 мин", "+15 мин", "+1 час", "+1 день"};
        final int[][] quickOffsets = new int[][]{
                {0, 0, 0, 0, 0, 30},
                {0, 0, 0, 0, 5, 0},
                {0, 0, 0, 0, 15, 0},
                {0, 0, 0, 1, 0, 0},
                {0, 0, 1, 0, 0, 0}
        };

        for (int i = 0; i < quickLabels.length; i++) {
            final int[] off = quickOffsets[i];
            Button btn = createStyledButton(quickLabels[i], "#E2E8F0", "#1E293B");
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, dp(42), 1f);
            if (i > 0) lp.leftMargin = dp(6);
            btn.setLayoutParams(lp);
            btn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            btn.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    resolveSnooze(off[0], off[1], off[2], off[3], off[4], off[5]);
                }
            });
            quickRow.addView(btn);
        }
        body.addView(quickRow);

        // Произвольное откладывание (число + единица: сек, мин, ч, дн, мес, лет)
        LinearLayout customRow = new LinearLayout(this);
        customRow.setOrientation(LinearLayout.HORIZONTAL);
        customRow.setGravity(Gravity.CENTER_VERTICAL);
        customRow.setPadding(0, dp(10), 0, 0);

        final EditText etCustomVal = new EditText(this);
        etCustomVal.setText("10");
        etCustomVal.setInputType(InputType.TYPE_CLASS_NUMBER);
        etCustomVal.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams etLp = new LinearLayout.LayoutParams(dp(80), dp(44));
        etCustomVal.setLayoutParams(etLp);

        final Spinner spUnit = new Spinner(this);
        String[] units = new String[]{"сек.", "мин.", "ч.", "дн.", "мес.", "лет"};
        ArrayAdapter<String> unitAdapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, units);
        spUnit.setAdapter(unitAdapter);
        spUnit.setSelection(1); // мин.
        LinearLayout.LayoutParams spLp = new LinearLayout.LayoutParams(0, dp(44), 1f);
        spLp.leftMargin = dp(8);
        spLp.rightMargin = dp(8);
        spUnit.setLayoutParams(spLp);

        Button btnCustomSnooze = createStyledButton("Отложить", "#DBEAFE", "#1E40AF");
        LinearLayout.LayoutParams btnCustomLp = new LinearLayout.LayoutParams(dp(120), dp(44));
        btnCustomSnooze.setLayoutParams(btnCustomLp);
        btnCustomSnooze.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                int val = 10;
                try {
                    val = Math.max(1, Integer.parseInt(etCustomVal.getText().toString().trim()));
                } catch (Exception ignored) {
                }
                int unitIdx = spUnit.getSelectedItemPosition();
                switch (unitIdx) {
                    case 0:
                        resolveSnooze(0, 0, 0, 0, 0, val);
                        break;
                    case 1:
                        resolveSnooze(0, 0, 0, 0, val, 0);
                        break;
                    case 2:
                        resolveSnooze(0, 0, 0, val, 0, 0);
                        break;
                    case 3:
                        resolveSnooze(0, 0, val, 0, 0, 0);
                        break;
                    case 4:
                        resolveSnooze(0, val, 0, 0, 0, 0);
                        break;
                    case 5:
                        resolveSnooze(val, 0, 0, 0, 0, 0);
                        break;
                    default:
                        resolveSnooze(0, 0, 0, 0, val, 0);
                        break;
                }
            }
        });

        customRow.addView(etCustomVal);
        customRow.addView(spUnit);
        customRow.addView(btnCustomSnooze);
        body.addView(customRow);

        // Главная кнопка «Понятно (Выполнено)»
        String completeLabel = currentReminder.recurrenceValue == ReminderItem.RECURRENCE_NONE
                ? "✓  Понятно (Выполнено)"
                : "✓  ОК (Перенести на следующий повтор)";
        Button btnComplete = createStyledButton(completeLabel, "#16A34A", "#FFFFFF");
        btnComplete.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        LinearLayout.LayoutParams completeLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(52)
        );
        completeLp.topMargin = dp(20);
        btnComplete.setLayoutParams(completeLp);
        btnComplete.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                resolveComplete();
            }
        });
        body.addView(btnComplete);

        root.addView(body);
        scroll.addView(root);
        setContentView(scroll);
    }

    private void playAlertSoundAndVibrate() {
        try {
            Vibrator vib = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);
            if (vib != null) {
                vib.vibrate(new long[]{0, 400, 200, 400}, -1);
            }
        } catch (Exception ignored) {
        }

        if (!currentReminder.playSound) return;
        try {
            Uri uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
            if (uri == null) {
                uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM);
            }
            activeRingtone = RingtoneManager.getRingtone(getApplicationContext(), uri);
            if (activeRingtone != null) {
                activeRingtone.play();
            }
        } catch (Exception ignored) {
        }
    }

    private void stopAlertSound() {
        try {
            if (activeRingtone != null && activeRingtone.isPlaying()) {
                activeRingtone.stop();
            }
        } catch (Exception ignored) {
        }
        try {
            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null && currentReminder != null) {
                nm.cancel(currentReminder.id.hashCode());
            }
        } catch (Exception ignored) {
        }
    }

    private void resolveComplete() {
        stopAlertSound();
        List<ReminderItem> list = StorageService.loadReminders(this);
        for (ReminderItem r : list) {
            if (r != null && r.id.equals(currentReminder.id)) {
                r.advanceAfterComplete(System.currentTimeMillis());
                break;
            }
        }
        StorageService.saveReminders(this, list);
        AlarmScheduler.rescheduleAll(this, list);
        pushChangesToCloudInBackground(list);
        finish();
    }

    private void resolveSnooze(int years, int months, int days, int hours, int minutes, int seconds) {
        stopAlertSound();
        List<ReminderItem> list = StorageService.loadReminders(this);
        for (ReminderItem r : list) {
            if (r != null && r.id.equals(currentReminder.id)) {
                r.snoozeByOffset(years, months, days, hours, minutes, seconds);
                break;
            }
        }
        StorageService.saveReminders(this, list);
        AlarmScheduler.rescheduleAll(this, list);
        pushChangesToCloudInBackground(list);
        finish();
    }

    private void pushChangesToCloudInBackground(final List<ReminderItem> list) {
        final CloudSyncConfig cfg = StorageService.loadCloudConfig(this);
        if (!cfg.isConfigured()) return;
        final Context appCtx = getApplicationContext();
        new Thread(new Runnable() {
            @Override
            public void run() {
                CloudSyncService.SyncResult res = CloudSyncService.synchronize(cfg, list, true);
                if (res.success && res.mergedReminders != null) {
                    StorageService.saveReminders(appCtx, res.mergedReminders);
                    AlarmScheduler.rescheduleAll(appCtx, res.mergedReminders);
                }
            }
        }).start();
    }

    private Button createStyledButton(String text, String bgColorHex, String textColorHex) {
        Button btn = new Button(this);
        btn.setAllCaps(false);
        btn.setText(text);
        btn.setTextColor(Color.parseColor(textColorHex));
        btn.setTypeface(Typeface.DEFAULT_BOLD);
        btn.setPadding(dp(6), dp(4), dp(6), dp(4));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.parseColor(bgColorHex));
        bg.setCornerRadius(dp(8));
        btn.setBackground(bg);
        return btn;
    }

    private int dp(int value) {
        return (int) TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP,
                value,
                getResources().getDisplayMetrics()
        );
    }

    @Override
    protected void onDestroy() {
        stopAlertSound();
        super.onDestroy();
    }
}
