package com.strelo.reminder;

import android.app.NotificationManager;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.media.Ringtone;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.Vibrator;
import android.provider.Settings;
import android.util.DisplayMetrics;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.List;

/**
 * Менеджер всплывающего окна поверх всех остальных приложений (SYSTEM_ALERT_WINDOW / TYPE_APPLICATION_OVERLAY).
 * Гарантирует появление окна напоминания прямо поверх любого открытого приложения на телефоне (браузер, мессенджер, видео).
 */
public class OverlayAlertManager {

    private static View currentOverlayView;
    private static WindowManager currentWindowManager;
    private static Ringtone currentRingtone;
    private static final Handler mainHandler = new Handler(Looper.getMainLooper());

    public static boolean canDrawOverlays(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            return Settings.canDrawOverlays(context);
        }
        return true;
    }

    public static void showOverlayPopup(final Context context, final ReminderItem item) {
        if (context == null || item == null) return;
        if (!canDrawOverlays(context)) return;

        final Context appCtx = context.getApplicationContext();
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                showOverlayOnMainThread(appCtx, item);
            }
        });
    }

    public static void dismissOverlay() {
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                stopSound();
                if (currentOverlayView != null && currentWindowManager != null) {
                    try {
                        currentWindowManager.removeView(currentOverlayView);
                    } catch (Exception ignored) {
                    }
                }
                currentOverlayView = null;
            }
        });
    }

    private static void showOverlayOnMainThread(final Context appCtx, final ReminderItem item) {
        dismissOverlayImmediate();

        try {
            final WindowManager wm = (WindowManager) appCtx.getSystemService(Context.WINDOW_SERVICE);
            if (wm == null) return;
            currentWindowManager = wm;

            DisplayMetrics dm = appCtx.getResources().getDisplayMetrics();
            int cardWidth = Math.min(dm.widthPixels - dp(appCtx, 20), dp(appCtx, 420));

            boolean isHigh = item.priorityValue == ReminderItem.PRIORITY_HIGH;
            int headerColor = isHigh ? Color.parseColor("#B91C1C") : Color.parseColor("#1E3A8A");

            // Внешний контейнер с тенью и рамкой
            LinearLayout rootCard = new LinearLayout(appCtx);
            rootCard.setOrientation(LinearLayout.VERTICAL);
            GradientDrawable cardBg = new GradientDrawable();
            cardBg.setColor(Color.WHITE);
            cardBg.setCornerRadius(dp(appCtx, 14));
            cardBg.setStroke(dp(appCtx, 2), headerColor);
            rootCard.setBackground(cardBg);

            // Шапка всплывающего окна
            LinearLayout header = new LinearLayout(appCtx);
            header.setOrientation(LinearLayout.VERTICAL);
            header.setPadding(dp(appCtx, 16), dp(appCtx, 12), dp(appCtx, 16), dp(appCtx, 10));
            GradientDrawable headerBg = new GradientDrawable();
            headerBg.setColor(headerColor);
            float r = dp(appCtx, 12);
            headerBg.setCornerRadii(new float[]{r, r, r, r, 0, 0, 0, 0});
            header.setBackground(headerBg);

            TextView tvBadge = new TextView(appCtx);
            tvBadge.setText(isHigh ? "⚡ ВАЖНОЕ НАПОМИНАНИЕ STRELO" : "🔔 НАПОМИНАНИЕ STRELO");
            tvBadge.setTextColor(Color.WHITE);
            tvBadge.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
            tvBadge.setTypeface(Typeface.DEFAULT_BOLD);

            TextView tvTime = new TextView(appCtx);
            tvTime.setText("Время: " + item.getFormattedTriggerTime() + " • " + item.getRecurrenceDisplayText());
            tvTime.setTextColor(Color.parseColor("#DBEAFE"));
            tvTime.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            tvTime.setPadding(0, dp(appCtx, 2), 0, 0);

            header.addView(tvBadge);
            header.addView(tvTime);
            rootCard.addView(header);

            // Тело карточки
            LinearLayout body = new LinearLayout(appCtx);
            body.setOrientation(LinearLayout.VERTICAL);
            body.setPadding(dp(appCtx, 16), dp(appCtx, 12), dp(appCtx, 16), dp(appCtx, 14));

            TextView tvTitle = new TextView(appCtx);
            tvTitle.setText(item.title);
            tvTitle.setTextColor(Color.parseColor("#0F172A"));
            tvTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
            tvTitle.setTypeface(Typeface.DEFAULT_BOLD);
            body.addView(tvTitle);

            if (item.notes != null && !item.notes.trim().isEmpty()) {
                TextView tvNotes = new TextView(appCtx);
                tvNotes.setText(item.notes);
                tvNotes.setTextColor(Color.parseColor("#334155"));
                tvNotes.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
                tvNotes.setPadding(0, dp(appCtx, 6), 0, 0);
                body.addView(tvNotes);
            }

            TextView tvSnoozeLbl = new TextView(appCtx);
            tvSnoozeLbl.setText("Отложить на:");
            tvSnoozeLbl.setTextColor(Color.parseColor("#475569"));
            tvSnoozeLbl.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
            tvSnoozeLbl.setTypeface(Typeface.DEFAULT_BOLD);
            tvSnoozeLbl.setPadding(0, dp(appCtx, 10), 0, dp(appCtx, 6));
            body.addView(tvSnoozeLbl);

            // Ряд быстрых кнопок откладывания
            LinearLayout snoozeRow = new LinearLayout(appCtx);
            snoozeRow.setOrientation(LinearLayout.HORIZONTAL);

            final String[] labels = new String[]{"+30 сек", "+5 мин", "+15 мин", "+1 час"};
            final int[][] offsets = new int[][]{
                    {0, 0, 0, 0, 0, 30},
                    {0, 0, 0, 0, 5, 0},
                    {0, 0, 0, 0, 15, 0},
                    {0, 0, 0, 1, 0, 0}
            };

            for (int i = 0; i < labels.length; i++) {
                final int[] off = offsets[i];
                Button b = createOverlayButton(appCtx, labels[i], "#E2E8F0", "#1E293B");
                b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
                b.setMinHeight(dp(appCtx, 38));
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                        0,
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        1f
                );
                if (i > 0) lp.leftMargin = dp(appCtx, 5);
                b.setLayoutParams(lp);
                b.setOnClickListener(new View.OnClickListener() {
                    @Override
                    public void onClick(View v) {
                        resolveSnoozeFromOverlay(appCtx, item, off[0], off[1], off[2], off[3], off[4], off[5]);
                    }
                });
                snoozeRow.addView(b);
            }
            body.addView(snoozeRow);

            // Нижний ряд: «Понятно (Выполнено)» и «На весь экран»
            LinearLayout bottomRow = new LinearLayout(appCtx);
            bottomRow.setOrientation(LinearLayout.HORIZONTAL);
            LinearLayout.LayoutParams botLp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
            );
            botLp.topMargin = dp(appCtx, 10);
            bottomRow.setLayoutParams(botLp);

            Button btnComplete = createOverlayButton(appCtx, "✓ Понятно (Выполнено)", "#16A34A", "#FFFFFF");
            btnComplete.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
            btnComplete.setMinHeight(dp(appCtx, 44));
            LinearLayout.LayoutParams compLp = new LinearLayout.LayoutParams(
                    0,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    1.6f
            );
            btnComplete.setLayoutParams(compLp);
            btnComplete.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    resolveCompleteFromOverlay(appCtx, item);
                }
            });

            Button btnExpand = createOverlayButton(appCtx, "Ещё...", "#DBEAFE", "#1E40AF");
            btnExpand.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            btnExpand.setMinHeight(dp(appCtx, 44));
            LinearLayout.LayoutParams expLp = new LinearLayout.LayoutParams(
                    0,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    0.7f
            );
            expLp.leftMargin = dp(appCtx, 6);
            btnExpand.setLayoutParams(expLp);
            btnExpand.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    dismissOverlayImmediate();
                    Intent fullIntent = new Intent(appCtx, ReminderAlertActivity.class);
                    fullIntent.putExtra(AlarmScheduler.EXTRA_REMINDER_ID, item.id);
                    fullIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
                    try {
                        appCtx.startActivity(fullIntent);
                    } catch (Exception ignored) {
                    }
                }
            });

            bottomRow.addView(btnComplete);
            bottomRow.addView(btnExpand);
            body.addView(bottomRow);

            rootCard.addView(body);

            int overlayType = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                    ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                    : WindowManager.LayoutParams.TYPE_PHONE;

            WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                    cardWidth,
                    WindowManager.LayoutParams.WRAP_CONTENT,
                    overlayType,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                            | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                            | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON
                            | WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED
                            | WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON,
                    PixelFormat.TRANSLUCENT
            );
            params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
            params.y = dp(appCtx, 28);

            wm.addView(rootCard, params);
            currentOverlayView = rootCard;

            playSoundAndVibrate(appCtx, item.playSound);
        } catch (Exception ignored) {
        }
    }

    private static void dismissOverlayImmediate() {
        stopSound();
        if (currentOverlayView != null && currentWindowManager != null) {
            try {
                currentWindowManager.removeView(currentOverlayView);
            } catch (Exception ignored) {
            }
        }
        currentOverlayView = null;
    }

    private static void playSoundAndVibrate(Context appCtx, boolean playSound) {
        try {
            Vibrator vib = (Vibrator) appCtx.getSystemService(Context.VIBRATOR_SERVICE);
            if (vib != null) {
                vib.vibrate(new long[]{0, 400, 200, 400}, -1);
            }
        } catch (Exception ignored) {
        }

        if (!playSound) return;
        try {
            Uri uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
            if (uri == null) {
                uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM);
            }
            currentRingtone = RingtoneManager.getRingtone(appCtx, uri);
            if (currentRingtone != null) {
                currentRingtone.play();
            }
        } catch (Exception ignored) {
        }
    }

    private static void stopSound() {
        try {
            if (currentRingtone != null && currentRingtone.isPlaying()) {
                currentRingtone.stop();
            }
        } catch (Exception ignored) {
        }
        currentRingtone = null;
    }

    private static void cancelSystemNotification(Context appCtx, String reminderId) {
        try {
            NotificationManager nm = (NotificationManager) appCtx.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null && reminderId != null) {
                nm.cancel(reminderId.hashCode());
            }
        } catch (Exception ignored) {
        }
    }

    private static void resolveCompleteFromOverlay(final Context appCtx, ReminderItem target) {
        dismissOverlayImmediate();
        cancelSystemNotification(appCtx, target.id);

        final List<ReminderItem> list = StorageService.loadReminders(appCtx);
        for (ReminderItem r : list) {
            if (r != null && r.id.equals(target.id)) {
                r.advanceAfterComplete(System.currentTimeMillis());
                break;
            }
        }
        StorageService.saveReminders(appCtx, list);
        AlarmScheduler.rescheduleAll(appCtx, list);
        syncInBackground(appCtx, list);
    }

    private static void resolveSnoozeFromOverlay(
            final Context appCtx,
            ReminderItem target,
            int y, int mo, int d, int h, int m, int s) {

        dismissOverlayImmediate();
        cancelSystemNotification(appCtx, target.id);

        final List<ReminderItem> list = StorageService.loadReminders(appCtx);
        for (ReminderItem r : list) {
            if (r != null && r.id.equals(target.id)) {
                r.snoozeByOffset(y, mo, d, h, m, s);
                break;
            }
        }
        StorageService.saveReminders(appCtx, list);
        AlarmScheduler.rescheduleAll(appCtx, list);
        syncInBackground(appCtx, list);
    }

    private static void syncInBackground(final Context appCtx, final List<ReminderItem> list) {
        final CloudSyncConfig cfg = StorageService.loadCloudConfig(appCtx);
        if (cfg == null || !cfg.isConfigured()) return;
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

    private static Button createOverlayButton(Context ctx, String text, String bgHex, String fgHex) {
        Button btn = new Button(ctx);
        btn.setAllCaps(false);
        btn.setIncludeFontPadding(false);
        btn.setSingleLine(true);
        btn.setGravity(Gravity.CENTER);
        btn.setText(text);
        btn.setTextColor(Color.parseColor(fgHex));
        btn.setTypeface(Typeface.DEFAULT_BOLD);
        btn.setPadding(dp(ctx, 6), dp(ctx, 6), dp(ctx, 6), dp(ctx, 6));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.parseColor(bgHex));
        bg.setCornerRadius(dp(ctx, 8));
        btn.setBackground(bg);
        return btn;
    }

    private static int dp(Context ctx, int value) {
        return (int) TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP,
                value,
                ctx.getResources().getDisplayMetrics()
        );
    }
}
