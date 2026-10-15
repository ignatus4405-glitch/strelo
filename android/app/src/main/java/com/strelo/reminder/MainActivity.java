package com.strelo.reminder;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.DatePickerDialog;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.TimePickerDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.DatePicker;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.TimePicker;
import android.widget.Toast;

import java.text.SimpleDateFormat;
import java.util.Calendar;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class MainActivity extends Activity {

    private static final String UPDATE_CHANNEL_ID = "strelo_updates_channel";
    private static final int UPDATE_NOTIFICATION_ID = 9901;

    private List<ReminderItem> reminders;
    private CloudSyncConfig cloudConfig;

    private TextView tvClock;
    private TextView tvCloudStatus;
    private TextView tvSummary;
    private Button btnCheckUpdate;
    private LinearLayout listContainer;

    private final Handler uiHandler = new Handler(Looper.getMainLooper());
    private boolean isSyncInProgress = false;
    private int secondsSinceLastSync = 0;

    private boolean isUpdateCheckInProgress = false;
    private boolean initialUpdateCheckDone = false;
    private int secondsSinceStartup = 0;
    private String notifiedUpdateVersion = null;
    private UpdateService.UpdateCheckResult latestAvailableUpdate = null;

    private final Runnable tickRunnable = new Runnable() {
        @Override
        public void run() {
            long now = System.currentTimeMillis();
            SimpleDateFormat sdf = new SimpleDateFormat("dd.MM.yyyy HH:mm:ss", Locale.getDefault());
            if (tvClock != null) {
                tvClock.setText("Сейчас: " + sdf.format(new Date(now)) + "  •  v" + UpdateService.CURRENT_VERSION);
            }

            // Проверяем, не наступило ли время какого-либо напоминания прямо при открытом приложении
            boolean changed = false;
            if (reminders != null) {
                for (ReminderItem r : reminders) {
                    if (r != null && !r.isDeleted && r.isEnabled && !r.isCompleted && r.triggerTimeMillis <= now) {
                        Intent alertIntent = new Intent(MainActivity.this, ReminderAlertActivity.class);
                        alertIntent.putExtra(AlarmScheduler.EXTRA_REMINDER_ID, r.id);
                        startActivity(alertIntent);
                        r.snooze(1);
                        changed = true;
                        break;
                    }
                }
            }
            if (changed) {
                StorageService.saveReminders(MainActivity.this, reminders);
            }

            refreshListViewsOnly(now);

            if (cloudConfig != null && cloudConfig.isConfigured() && !isSyncInProgress) {
                secondsSinceLastSync++;
                int interval = Math.max(10, cloudConfig.autoSyncIntervalSeconds);
                if (secondsSinceLastSync >= interval) {
                    secondsSinceLastSync = 0;
                    triggerCloudSync(false, false);
                }
            }

            // Автоматическая проверка выхода новой версии на GitHub (через 3 сек после запуска и каждые 30 минут)
            secondsSinceStartup++;
            if (!initialUpdateCheckDone && secondsSinceStartup >= 3) {
                initialUpdateCheckDone = true;
                checkForGitHubUpdates(false);
            } else if (secondsSinceStartup > 0 && secondsSinceStartup % 1800 == 0) {
                checkForGitHubUpdates(false);
            }

            uiHandler.postDelayed(this, 1000);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        requestNotificationPermissionIfNeeded();
        createUpdateNotificationChannel();

        cloudConfig = StorageService.loadCloudConfig(this);
        reminders = StorageService.loadReminders(this);
        AlarmScheduler.rescheduleAll(this, reminders);

        buildMainUi();
        rebuildRemindersList();

        if (cloudConfig.isConfigured()) {
            triggerCloudSync(false, false);
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        reminders = StorageService.loadReminders(this);
        cloudConfig = StorageService.loadCloudConfig(this);
        updateCloudStatusBadge();
        rebuildRemindersList();

        uiHandler.removeCallbacks(tickRunnable);
        uiHandler.post(tickRunnable);
    }

    @Override
    protected void onPause() {
        super.onPause();
        uiHandler.removeCallbacks(tickRunnable);
    }

    private void requestNotificationPermissionIfNeeded() {
        if (Build.VERSION.SDK_INT >= 33) {
            if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 101);
            }
        }
    }

    private void createUpdateNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) {
                NotificationChannel ch = new NotificationChannel(
                        UPDATE_CHANNEL_ID,
                        "Обновления приложения Strelo",
                        NotificationManager.IMPORTANCE_DEFAULT
                );
                ch.setDescription("Уведомления о выходе новой версии Strelo на GitHub");
                nm.createNotificationChannel(ch);
            }
        }
    }

    private void buildMainUi() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.parseColor("#F1F5F9"));

        // 1. ВЕРХНЯЯ ПАНЕЛЬ (ШАПКА)
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.VERTICAL);
        header.setBackgroundColor(Color.parseColor("#1E3A8A"));
        header.setPadding(dp(16), dp(14), dp(16), dp(14));

        TextView tvTitle = new TextView(this);
        tvTitle.setText("🔔 Strelo Напоминалка");
        tvTitle.setTextColor(Color.WHITE);
        tvTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 19);
        tvTitle.setTypeface(Typeface.DEFAULT_BOLD);

        tvClock = new TextView(this);
        tvClock.setText("Сейчас: --  •  v" + UpdateService.CURRENT_VERSION);
        tvClock.setTextColor(Color.parseColor("#BFDBFE"));
        tvClock.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        tvClock.setPadding(0, dp(2), 0, dp(8));

        // Панель кнопок шапки: Синхр. | Облако (ПК) | Обновл.
        LinearLayout headerButtonsRow = new LinearLayout(this);
        headerButtonsRow.setOrientation(LinearLayout.HORIZONTAL);
        headerButtonsRow.setGravity(Gravity.CENTER_VERTICAL);

        Button btnSyncNow = createStyledButton("Синхр.", "#2563EB", "#FFFFFF");
        btnSyncNow.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        btnSyncNow.setSingleLine(true);
        btnSyncNow.setMinHeight(dp(36));
        btnSyncNow.setPadding(dp(10), dp(6), dp(10), dp(6));
        LinearLayout.LayoutParams syncLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                0.9f
        );
        syncLp.rightMargin = dp(6);
        btnSyncNow.setLayoutParams(syncLp);
        btnSyncNow.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (!cloudConfig.isConfigured()) {
                    showCloudSyncDialog();
                } else {
                    triggerCloudSync(true, true);
                }
            }
        });

        Button btnCloudSettings = createStyledButton("Облако (ПК)", "#1D4ED8", "#FFFFFF");
        btnCloudSettings.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        btnCloudSettings.setSingleLine(true);
        btnCloudSettings.setMinHeight(dp(36));
        btnCloudSettings.setPadding(dp(10), dp(6), dp(10), dp(6));
        LinearLayout.LayoutParams cloudLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1.2f
        );
        cloudLp.rightMargin = dp(6);
        btnCloudSettings.setLayoutParams(cloudLp);
        btnCloudSettings.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showCloudSyncDialog();
            }
        });

        btnCheckUpdate = createStyledButton("Обновл.", "#2563EB", "#FFFFFF");
        btnCheckUpdate.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        btnCheckUpdate.setSingleLine(true);
        btnCheckUpdate.setMinHeight(dp(36));
        btnCheckUpdate.setPadding(dp(10), dp(6), dp(10), dp(6));
        LinearLayout.LayoutParams updLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1.0f
        );
        btnCheckUpdate.setLayoutParams(updLp);
        btnCheckUpdate.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                checkForGitHubUpdates(true);
            }
        });

        headerButtonsRow.addView(btnSyncNow);
        headerButtonsRow.addView(btnCloudSettings);
        headerButtonsRow.addView(btnCheckUpdate);

        tvCloudStatus = new TextView(this);
        tvCloudStatus.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        tvCloudStatus.setPadding(0, dp(6), 0, 0);
        updateCloudStatusBadge();

        header.addView(tvTitle);
        header.addView(tvClock);
        header.addView(headerButtonsRow);
        header.addView(tvCloudStatus);
        root.addView(header);

        // 2. ПАНЕЛЬ ДЕЙСТВИЙ (2 ряда с высотой WRAP_CONTENT, чтобы текст кнопок никогда не обрезался)
        LinearLayout actionPanel = new LinearLayout(this);
        actionPanel.setOrientation(LinearLayout.VERTICAL);
        actionPanel.setPadding(dp(12), dp(12), dp(12), dp(6));

        // Ряд 1: Крупная кнопка «+ Новое напоминание» во всю ширину экрана
        Button btnAdd = createStyledButton("+ Новое напоминание", "#2563EB", "#FFFFFF");
        btnAdd.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        btnAdd.setSingleLine(true);
        btnAdd.setMinHeight(dp(46));
        btnAdd.setPadding(dp(14), dp(11), dp(14), dp(11));
        LinearLayout.LayoutParams addLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
        );
        btnAdd.setLayoutParams(addLp);
        btnAdd.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showReminderEditorDialog(null);
            }
        });
        actionPanel.addView(btnAdd);

        // Ряд 2: Две кнопки в ряд по 50% ширины («Тест уведомления» и «Очистить выполненные»)
        LinearLayout secondaryActionRow = new LinearLayout(this);
        secondaryActionRow.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams secRowLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
        );
        secRowLp.topMargin = dp(8);
        secondaryActionRow.setLayoutParams(secRowLp);

        Button btnTestAlert = createStyledButton("Тест уведомления", "#EEF2FF", "#1E40AF");
        btnTestAlert.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        btnTestAlert.setSingleLine(true);
        btnTestAlert.setMinHeight(dp(42));
        btnTestAlert.setPadding(dp(10), dp(9), dp(10), dp(9));
        LinearLayout.LayoutParams testLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        testLp.rightMargin = dp(6);
        btnTestAlert.setLayoutParams(testLp);
        btnTestAlert.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                Intent i = new Intent(MainActivity.this, ReminderAlertActivity.class);
                startActivity(i);
            }
        });

        Button btnClearDone = createStyledButton("Очистить выполненные", "#E2E8F0", "#334155");
        btnClearDone.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        btnClearDone.setSingleLine(true);
        btnClearDone.setMinHeight(dp(42));
        btnClearDone.setPadding(dp(10), dp(9), dp(10), dp(9));
        LinearLayout.LayoutParams clearLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        btnClearDone.setLayoutParams(clearLp);
        btnClearDone.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                clearCompletedReminders();
            }
        });

        secondaryActionRow.addView(btnTestAlert);
        secondaryActionRow.addView(btnClearDone);
        actionPanel.addView(secondaryActionRow);
        root.addView(actionPanel);

        // 3. СВОДКА
        tvSummary = new TextView(this);
        tvSummary.setText("Список напоминаний (Всего: 0, Активных: 0)");
        tvSummary.setTextColor(Color.parseColor("#1E293B"));
        tvSummary.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        tvSummary.setTypeface(Typeface.DEFAULT_BOLD);
        tvSummary.setPadding(dp(14), dp(6), dp(14), dp(6));
        root.addView(tvSummary);

        // 4. ПРОКРУЧИВАЕМЫЙ СПИСОК КАРТОЧЕК НАПОМИНАНИЙ
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        LinearLayout.LayoutParams scrollLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                0,
                1f
        );
        scroll.setLayoutParams(scrollLp);

        listContainer = new LinearLayout(this);
        listContainer.setOrientation(LinearLayout.VERTICAL);
        listContainer.setPadding(dp(12), dp(4), dp(12), dp(24));
        scroll.addView(listContainer);

        root.addView(scroll);
        setContentView(root);
    }

    private void checkForGitHubUpdates(final boolean isManualClick) {
        if (isManualClick && latestAvailableUpdate != null && latestAvailableUpdate.updateAvailable) {
            showUpdateAvailableDialog(latestAvailableUpdate);
            return;
        }
        if (isUpdateCheckInProgress) {
            return;
        }
        isUpdateCheckInProgress = true;
        if (isManualClick && btnCheckUpdate != null) {
            btnCheckUpdate.setText("Проверка...");
        }

        new Thread(new Runnable() {
            @Override
            public void run() {
                final UpdateService.UpdateCheckResult res = UpdateService.checkForUpdates();
                uiHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        isUpdateCheckInProgress = false;
                        handleUpdateCheckResult(res, isManualClick);
                    }
                });
            }
        }).start();
    }

    private void handleUpdateCheckResult(UpdateService.UpdateCheckResult res, boolean isManualClick) {
        if (res == null || !res.success) {
            if (btnCheckUpdate != null) {
                btnCheckUpdate.setText("Обновл.");
            }
            if (isManualClick) {
                Toast.makeText(
                        this,
                        res != null && !res.errorMessage.isEmpty()
                                ? res.errorMessage
                                : "Не удалось проверить обновления на GitHub.",
                        Toast.LENGTH_LONG
                ).show();
            }
            return;
        }

        if (res.updateAvailable) {
            latestAvailableUpdate = res;
            if (btnCheckUpdate != null) {
                btnCheckUpdate.setText("v" + res.latestVersion + " ⬇");
                GradientDrawable bg = new GradientDrawable();
                bg.setColor(Color.parseColor("#16A34A"));
                bg.setCornerRadius(dp(8));
                btnCheckUpdate.setBackground(bg);
            }

            boolean alreadyNotified = res.latestVersion != null && res.latestVersion.equalsIgnoreCase(notifiedUpdateVersion);
            if (isManualClick || !alreadyNotified) {
                notifiedUpdateVersion = res.latestVersion;
                showUpdateSystemNotification(res);
                showUpdateAvailableDialog(res);
            }
        } else {
            if (btnCheckUpdate != null) {
                btnCheckUpdate.setText("Обновл.");
            }
            if (isManualClick) {
                new AlertDialog.Builder(this)
                        .setTitle("Обновлений нет")
                        .setMessage("У вас установлена самая актуальная версия Strelo для Android (v" + res.currentVersion + ").")
                        .setPositiveButton("ОК", null)
                        .show();
            }
        }
    }

    private void showUpdateSystemNotification(UpdateService.UpdateCheckResult res) {
        try {
            String targetUrl = (res.apkDownloadUrl != null && !res.apkDownloadUrl.isEmpty())
                    ? res.apkDownloadUrl
                    : res.releaseUrl;
            Intent browserIntent = new Intent(Intent.ACTION_VIEW, Uri.parse(targetUrl));
            int flags = PendingIntent.FLAG_UPDATE_CURRENT;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                flags |= PendingIntent.FLAG_IMMUTABLE;
            }
            PendingIntent pi = PendingIntent.getActivity(this, 9901, browserIntent, flags);

            Notification.Builder builder;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                builder = new Notification.Builder(this, UPDATE_CHANNEL_ID);
            } else {
                builder = new Notification.Builder(this);
            }

            builder.setSmallIcon(android.R.drawable.stat_sys_download_done)
                    .setContentTitle("Доступна новая версия Strelo v" + res.latestVersion)
                    .setContentText("Нажмите, чтобы скачать и установить обновление с GitHub")
                    .setAutoCancel(true)
                    .setContentIntent(pi);

            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) {
                nm.notify(UPDATE_NOTIFICATION_ID, builder.build());
            }
        } catch (Exception ignored) {
        }
    }

    private void showUpdateAvailableDialog(final UpdateService.UpdateCheckResult res) {
        if (isFinishing()) return;
        String message = "Вышла новая версия Strelo на GitHub!\n\n"
                + "Установленная версия: v" + res.currentVersion + "\n"
                + "Новая версия: v" + res.latestVersion + "\n\n"
                + "Скачать и установить новую версию APK прямо сейчас?";

        new AlertDialog.Builder(this)
                .setTitle("Доступно обновление v" + res.latestVersion)
                .setMessage(message)
                .setPositiveButton("Скачать APK", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        String url = (res.apkDownloadUrl != null && !res.apkDownloadUrl.isEmpty())
                                ? res.apkDownloadUrl
                                : res.releaseUrl;
                        try {
                            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
                            startActivity(intent);
                        } catch (Exception ex) {
                            Toast.makeText(MainActivity.this, "Не удалось открыть ссылку: " + url, Toast.LENGTH_LONG).show();
                        }
                    }
                })
                .setNegativeButton("Позже", null)
                .show();
    }

    private void updateCloudStatusBadge() {
        if (tvCloudStatus == null) return;
        if (cloudConfig == null || !cloudConfig.isConfigured()) {
            tvCloudStatus.setText("Облако: не настроено (нажмите «Облако (ПК)» для привязки к Windows)");
            tvCloudStatus.setTextColor(Color.parseColor("#DBEAFE"));
        } else {
            String mode = cloudConfig.providerType == CloudSyncConfig.PROVIDER_GITHUB_GIST
                    ? "GitHub Gist"
                    : "Cloud URL";
            tvCloudStatus.setText("✓ Облако активно (" + mode + ") • Автосинхронизация с ПК каждые "
                    + cloudConfig.autoSyncIntervalSeconds + " сек.");
            tvCloudStatus.setTextColor(Color.parseColor("#A7F3D0"));
        }
    }

    private void rebuildRemindersList() {
        if (listContainer == null) return;
        listContainer.removeAllViews();

        Collections.sort(reminders, new Comparator<ReminderItem>() {
            @Override
            public int compare(ReminderItem a, ReminderItem b) {
                boolean aActive = !a.isDeleted && a.isEnabled && !a.isCompleted;
                boolean bActive = !b.isDeleted && b.isEnabled && !b.isCompleted;
                if (aActive != bActive) {
                    return aActive ? -1 : 1;
                }
                return Long.compare(a.triggerTimeMillis, b.triggerTimeMillis);
            }
        });

        long now = System.currentTimeMillis();
        int totalVisible = 0;
        int activeCount = 0;

        for (final ReminderItem item : reminders) {
            if (item == null || item.isDeleted) continue;
            totalVisible++;
            if (item.isEnabled && !item.isCompleted) {
                activeCount++;
            }
            listContainer.addView(createReminderCard(item, now));
        }

        if (totalVisible == 0) {
            TextView tvEmpty = new TextView(this);
            tvEmpty.setText("Список напоминаний пуст.\nНажмите «+ Новое напоминание» или настройте «Облако (ПК)», чтобы загрузить напоминания с компьютера.");
            tvEmpty.setTextColor(Color.parseColor("#64748B"));
            tvEmpty.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
            tvEmpty.setGravity(Gravity.CENTER);
            tvEmpty.setPadding(dp(20), dp(40), dp(20), dp(40));
            listContainer.addView(tvEmpty);
        }

        if (tvSummary != null) {
            tvSummary.setText("Список напоминаний (Всего: " + totalVisible + ", Активных: " + activeCount + ")");
        }
    }

    private void refreshListViewsOnly(long now) {
        if (listContainer == null) return;
        int count = listContainer.getChildCount();
        for (int i = 0; i < count; i++) {
            View child = listContainer.getChildAt(i);
            Object tag = child.getTag();
            if (tag instanceof ReminderCardHolder) {
                ReminderCardHolder holder = (ReminderCardHolder) tag;
                holder.tvRemaining.setText("Осталось: " + holder.item.getRemainingTimeText(now));
            }
        }
    }

    private static class ReminderCardHolder {
        ReminderItem item;
        TextView tvRemaining;
    }

    private View createReminderCard(final ReminderItem item, long now) {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(14), dp(12), dp(14), dp(12));

        LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
        );
        cardLp.bottomMargin = dp(10);
        card.setLayoutParams(cardLp);

        int bgColor = Color.WHITE;
        int borderColor = Color.parseColor("#CBD5E1");
        if (item.isCompleted) {
            bgColor = Color.parseColor("#F8FAFC");
        } else if (!item.isEnabled) {
            bgColor = Color.parseColor("#F1F5F9");
        } else if (item.priorityValue == ReminderItem.PRIORITY_HIGH) {
            bgColor = Color.parseColor("#FEF2F2");
            borderColor = Color.parseColor("#FCA5A5");
        }

        GradientDrawable bg = new GradientDrawable();
        bg.setColor(bgColor);
        bg.setCornerRadius(dp(10));
        bg.setStroke(dp(1), borderColor);
        card.setBackground(bg);

        // Верхняя строка: Статус + Повтор
        LinearLayout topRow = new LinearLayout(this);
        topRow.setOrientation(LinearLayout.HORIZONTAL);

        TextView tvStatus = new TextView(this);
        tvStatus.setText(getStatusText(item));
        tvStatus.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        tvStatus.setTypeface(Typeface.DEFAULT_BOLD);
        tvStatus.setTextColor(item.priorityValue == ReminderItem.PRIORITY_HIGH
                ? Color.parseColor("#991B1B")
                : Color.parseColor("#1E40AF"));
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        tvStatus.setLayoutParams(stLp);

        TextView tvRecurrence = new TextView(this);
        tvRecurrence.setText(item.getRecurrenceDisplayText());
        tvRecurrence.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        tvRecurrence.setTextColor(Color.parseColor("#64748B"));

        topRow.addView(tvStatus);
        topRow.addView(tvRecurrence);
        card.addView(topRow);

        // Заголовок напоминания
        TextView tvTitle = new TextView(this);
        tvTitle.setText(item.title);
        tvTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16.5f);
        tvTitle.setTypeface(Typeface.DEFAULT_BOLD);
        tvTitle.setTextColor(item.isCompleted ? Color.parseColor("#94A3B8") : Color.parseColor("#0F172A"));
        tvTitle.setPadding(0, dp(4), 0, dp(2));
        card.addView(tvTitle);

        // Описание (если есть)
        if (item.notes != null && !item.notes.trim().isEmpty()) {
            TextView tvNotes = new TextView(this);
            tvNotes.setText(item.notes);
            tvNotes.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
            tvNotes.setTextColor(Color.parseColor("#475569"));
            tvNotes.setPadding(0, 0, 0, dp(4));
            card.addView(tvNotes);
        }

        // Время срабатывания и обратный отсчёт
        TextView tvTrigger = new TextView(this);
        tvTrigger.setText("Когда: " + item.getFormattedTriggerTime());
        tvTrigger.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        tvTrigger.setTextColor(Color.parseColor("#334155"));
        card.addView(tvTrigger);

        TextView tvRemaining = new TextView(this);
        tvRemaining.setText("Осталось: " + item.getRemainingTimeText(now));
        tvRemaining.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        tvRemaining.setTypeface(Typeface.DEFAULT_BOLD);
        tvRemaining.setTextColor(Color.parseColor("#1D4ED8"));
        tvRemaining.setPadding(0, dp(2), 0, dp(8));
        card.addView(tvRemaining);

        // Кнопки управления карточкой (WRAP_CONTENT высота, чтобы текст не обрезался)
        LinearLayout actRow = new LinearLayout(this);
        actRow.setOrientation(LinearLayout.HORIZONTAL);

        Button btnEdit = createCardActionButton("Изменить", "#E2E8F0", "#1E293B", 0);
        btnEdit.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showReminderEditorDialog(item);
            }
        });

        Button btnToggle = createCardActionButton(
                item.isCompleted ? "Вернуть" : (item.isEnabled ? "Пауза" : "Вкл"),
                "#E2E8F0",
                "#1E293B",
                dp(6)
        );
        btnToggle.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (item.isCompleted) {
                    item.isCompleted = false;
                    item.isEnabled = true;
                    if (item.triggerTimeMillis <= System.currentTimeMillis()) {
                        item.triggerTimeMillis = System.currentTimeMillis() + 15 * 60000L;
                    }
                } else {
                    item.isEnabled = !item.isEnabled;
                }
                item.touch();
                saveAndSyncChanges();
            }
        });

        Button btnSnooze10 = createCardActionButton("+10 мин", "#DBEAFE", "#1E40AF", dp(6));
        btnSnooze10.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                item.snooze(10);
                saveAndSyncChanges();
            }
        });

        Button btnDelete = createCardActionButton("Удалить", "#FEE2E2", "#B91C1C", dp(6));
        btnDelete.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                item.isDeleted = true;
                item.isEnabled = false;
                item.touch();
                saveAndSyncChanges();
            }
        });

        actRow.addView(btnEdit);
        actRow.addView(btnToggle);
        actRow.addView(btnSnooze10);
        actRow.addView(btnDelete);
        card.addView(actRow);

        ReminderCardHolder holder = new ReminderCardHolder();
        holder.item = item;
        holder.tvRemaining = tvRemaining;
        card.setTag(holder);

        return card;
    }

    private Button createCardActionButton(String text, String bgHex, String fgHex, int leftMarginPx) {
        Button btn = createStyledButton(text, bgHex, fgHex);
        btn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        btn.setSingleLine(true);
        btn.setMinHeight(dp(38));
        btn.setPadding(dp(6), dp(6), dp(6), dp(6));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        lp.leftMargin = leftMarginPx;
        btn.setLayoutParams(lp);
        return btn;
    }

    private String getStatusText(ReminderItem r) {
        if (r.isCompleted) return "✓ Выполнено";
        if (!r.isEnabled) return "⏸ Пауза";
        if (r.priorityValue == ReminderItem.PRIORITY_HIGH) return "⚡ Важное";
        return "🟢 Активно";
    }

    private void clearCompletedReminders() {
        boolean changed = false;
        for (ReminderItem r : reminders) {
            if (r != null && !r.isDeleted && r.isCompleted) {
                r.isDeleted = true;
                r.isEnabled = false;
                r.touch();
                changed = true;
            }
        }
        if (changed) {
            saveAndSyncChanges();
        } else {
            Toast.makeText(this, "Нет выполненных напоминаний для очистки.", Toast.LENGTH_SHORT).show();
        }
    }

    private void saveAndSyncChanges() {
        StorageService.saveReminders(this, reminders);
        AlarmScheduler.rescheduleAll(this, reminders);
        rebuildRemindersList();
        if (cloudConfig != null && cloudConfig.isConfigured()) {
            triggerCloudSync(true, false);
        }
    }

    // =========================================================================
    // ДИАЛОГ СОЗДАНИЯ / РЕДАКТИРОВАНИЯ НАПОМИНАНИЯ (6 ЕДИНИЦ ТАЙМЕРА + ТОЧНАЯ ДАТА)
    // =========================================================================
    private void showReminderEditorDialog(final ReminderItem editingItem) {
        final boolean isEdit = (editingItem != null);

        ScrollView scroll = new ScrollView(this);
        LinearLayout form = new LinearLayout(this);
        form.setOrientation(LinearLayout.VERTICAL);
        form.setPadding(dp(18), dp(14), dp(18), dp(10));
        scroll.addView(form);

        TextView lblTitle = new TextView(this);
        lblTitle.setText("О чём напомнить:");
        lblTitle.setTypeface(Typeface.DEFAULT_BOLD);
        form.addView(lblTitle);

        final EditText etTitle = new EditText(this);
        etTitle.setHint("Например: Позвонить клиенту");
        if (isEdit) etTitle.setText(editingItem.title);
        form.addView(etTitle);

        TextView lblNotes = new TextView(this);
        lblNotes.setText("Дополнительное описание / заметка:");
        lblNotes.setPadding(0, dp(8), 0, 0);
        form.addView(lblNotes);

        final EditText etNotes = new EditText(this);
        etNotes.setHint("Необязательно");
        if (isEdit) etNotes.setText(editingItem.notes);
        form.addView(etNotes);

        // Переключатель режима: «Через сколько» или «В точное время и дату»
        RadioGroup rgMode = new RadioGroup(this);
        rgMode.setOrientation(LinearLayout.HORIZONTAL);
        rgMode.setPadding(0, dp(10), 0, dp(6));

        final RadioButton rbTimer = new RadioButton(this);
        rbTimer.setText("Через сколько");
        rbTimer.setId(View.generateViewId());

        final RadioButton rbExact = new RadioButton(this);
        rbExact.setText("Точная дата и время");
        rbExact.setId(View.generateViewId());

        rgMode.addView(rbTimer);
        rgMode.addView(rbExact);
        form.addView(rgMode);

        // Панель «Через сколько» (Годы, Месяцы, Дни, Часы, Минуты, Секунды)
        final LinearLayout pnlTimer = new LinearLayout(this);
        pnlTimer.setOrientation(LinearLayout.VERTICAL);
        pnlTimer.setPadding(dp(10), dp(8), dp(10), dp(8));
        pnlTimer.setBackgroundColor(Color.parseColor("#F8FAFC"));

        LinearLayout rowUnits1 = new LinearLayout(this);
        rowUnits1.setOrientation(LinearLayout.HORIZONTAL);
        final EditText etYears = addUnitField(rowUnits1, "Годы", "0");
        final EditText etMonths = addUnitField(rowUnits1, "Месяцы", "0");
        final EditText etDays = addUnitField(rowUnits1, "Дни", "0");
        pnlTimer.addView(rowUnits1);

        LinearLayout rowUnits2 = new LinearLayout(this);
        rowUnits2.setOrientation(LinearLayout.HORIZONTAL);
        rowUnits2.setPadding(0, dp(6), 0, 0);
        final EditText etHours = addUnitField(rowUnits2, "Часы", "0");
        final EditText etMinutes = addUnitField(rowUnits2, "Минуты", "15");
        final EditText etSeconds = addUnitField(rowUnits2, "Секунды", "0");
        pnlTimer.addView(rowUnits2);

        // Быстрые пресеты таймера
        LinearLayout presetRow1 = new LinearLayout(this);
        presetRow1.setOrientation(LinearLayout.HORIZONTAL);
        presetRow1.setPadding(0, dp(8), 0, 0);
        addPresetBtn(presetRow1, "30 сек", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 0, 0, 0, 0, 30);
        addPresetBtn(presetRow1, "5 мин", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 0, 0, 0, 5, 0);
        addPresetBtn(presetRow1, "15 мин", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 0, 0, 0, 15, 0);
        addPresetBtn(presetRow1, "1 час", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 0, 0, 1, 0, 0);
        pnlTimer.addView(presetRow1);

        LinearLayout presetRow2 = new LinearLayout(this);
        presetRow2.setOrientation(LinearLayout.HORIZONTAL);
        presetRow2.setPadding(0, dp(4), 0, 0);
        addPresetBtn(presetRow2, "1 день", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 0, 1, 0, 0, 0);
        addPresetBtn(presetRow2, "1 мес", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 1, 0, 0, 0, 0);
        addPresetBtn(presetRow2, "1 год", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 1, 0, 0, 0, 0, 0);
        addPresetBtn(presetRow2, "Сброс", etYears, etMonths, etDays, etHours, etMinutes, etSeconds, 0, 0, 0, 0, 0, 0);
        pnlTimer.addView(presetRow2);

        form.addView(pnlTimer);

        // Панель «Точная дата и время»
        final LinearLayout pnlExact = new LinearLayout(this);
        pnlExact.setOrientation(LinearLayout.VERTICAL);
        pnlExact.setPadding(dp(10), dp(8), dp(10), dp(8));
        pnlExact.setBackgroundColor(Color.parseColor("#F8FAFC"));
        pnlExact.setVisibility(View.GONE);

        final Calendar selectedCal = Calendar.getInstance();
        if (isEdit && editingItem.triggerTimeMillis > System.currentTimeMillis()) {
            selectedCal.setTimeInMillis(editingItem.triggerTimeMillis);
        } else {
            selectedCal.add(Calendar.MINUTE, 15);
        }

        final TextView tvSelectedExact = new TextView(this);
        tvSelectedExact.setTypeface(Typeface.DEFAULT_BOLD);
        tvSelectedExact.setTextColor(Color.parseColor("#1E40AF"));
        tvSelectedExact.setPadding(0, 0, 0, dp(8));
        updateExactLabel(tvSelectedExact, selectedCal);
        pnlExact.addView(tvSelectedExact);

        LinearLayout pickersRow = new LinearLayout(this);
        pickersRow.setOrientation(LinearLayout.HORIZONTAL);

        Button btnPickDate = createStyledButton("Выбрать дату", "#E2E8F0", "#1E293B");
        btnPickDate.setMinHeight(dp(40));
        LinearLayout.LayoutParams pdLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        pdLp.rightMargin = dp(6);
        btnPickDate.setLayoutParams(pdLp);
        btnPickDate.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                new DatePickerDialog(
                        MainActivity.this,
                        new DatePickerDialog.OnDateSetListener() {
                            @Override
                            public void onDateSet(DatePicker view, int year, int month, int dayOfMonth) {
                                selectedCal.set(Calendar.YEAR, year);
                                selectedCal.set(Calendar.MONTH, month);
                                selectedCal.set(Calendar.DAY_OF_MONTH, dayOfMonth);
                                updateExactLabel(tvSelectedExact, selectedCal);
                            }
                        },
                        selectedCal.get(Calendar.YEAR),
                        selectedCal.get(Calendar.MONTH),
                        selectedCal.get(Calendar.DAY_OF_MONTH)
                ).show();
            }
        });

        Button btnPickTime = createStyledButton("Выбрать время", "#E2E8F0", "#1E293B");
        btnPickTime.setMinHeight(dp(40));
        LinearLayout.LayoutParams ptLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        btnPickTime.setLayoutParams(ptLp);
        btnPickTime.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                new TimePickerDialog(
                        MainActivity.this,
                        new TimePickerDialog.OnTimeSetListener() {
                            @Override
                            public void onTimeSet(TimePicker view, int hourOfDay, int minute) {
                                selectedCal.set(Calendar.HOUR_OF_DAY, hourOfDay);
                                selectedCal.set(Calendar.MINUTE, minute);
                                selectedCal.set(Calendar.SECOND, 0);
                                updateExactLabel(tvSelectedExact, selectedCal);
                            }
                        },
                        selectedCal.get(Calendar.HOUR_OF_DAY),
                        selectedCal.get(Calendar.MINUTE),
                        true
                ).show();
            }
        });

        pickersRow.addView(btnPickDate);
        pickersRow.addView(btnPickTime);
        pnlExact.addView(pickersRow);
        form.addView(pnlExact);

        rgMode.setOnCheckedChangeListener(new RadioGroup.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(RadioGroup group, int checkedId) {
                boolean timerMode = (checkedId == rbTimer.getId());
                pnlTimer.setVisibility(timerMode ? View.VISIBLE : View.GONE);
                pnlExact.setVisibility(timerMode ? View.GONE : View.VISIBLE);
            }
        });

        if (isEdit) {
            rbExact.setChecked(true);
        } else {
            rbTimer.setChecked(true);
        }

        // Повтор
        TextView lblRec = new TextView(this);
        lblRec.setText("Повтор:");
        lblRec.setPadding(0, dp(10), 0, dp(2));
        form.addView(lblRec);

        final Spinner spRecurrence = new Spinner(this);
        String[] recOptions = new String[]{
                "Один раз (без повтора)",
                "Каждые N минут",
                "Ежедневно",
                "По будням (Пн–Пт)",
                "Еженедельно"
        };
        spRecurrence.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, recOptions));
        if (isEdit) {
            spRecurrence.setSelection(Math.max(0, Math.min(4, editingItem.recurrenceValue)));
        }
        form.addView(spRecurrence);

        final EditText etIntervalMin = new EditText(this);
        etIntervalMin.setInputType(InputType.TYPE_CLASS_NUMBER);
        etIntervalMin.setHint("Интервал в минутах (например 30)");
        etIntervalMin.setText(isEdit ? String.valueOf(editingItem.intervalMinutes) : "30");
        form.addView(etIntervalMin);

        // Важность
        TextView lblPrio = new TextView(this);
        lblPrio.setText("Важность:");
        lblPrio.setPadding(0, dp(8), 0, dp(2));
        form.addView(lblPrio);

        final Spinner spPriority = new Spinner(this);
        String[] prioOptions = new String[]{"Обычное", "Важное (!)"};
        spPriority.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, prioOptions));
        if (isEdit) {
            spPriority.setSelection(Math.max(0, Math.min(1, editingItem.priorityValue)));
        }
        form.addView(spPriority);

        final CheckBox chkSound = new CheckBox(this);
        chkSound.setText("Звуковой сигнал и вибрация");
        chkSound.setChecked(!isEdit || editingItem.playSound);
        form.addView(chkSound);

        new AlertDialog.Builder(this)
                .setTitle(isEdit ? "Редактировать напоминание" : "Новое напоминание")
                .setView(scroll)
                .setPositiveButton("Сохранить", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        String title = etTitle.getText().toString().trim();
                        if (title.isEmpty()) {
                            Toast.makeText(MainActivity.this, "Введите текст напоминания!", Toast.LENGTH_SHORT).show();
                            return;
                        }

                        long triggerMillis;
                        if (rbTimer.isChecked()) {
                            int y = parseIntSafe(etYears.getText().toString(), 0);
                            int mo = parseIntSafe(etMonths.getText().toString(), 0);
                            int d = parseIntSafe(etDays.getText().toString(), 0);
                            int h = parseIntSafe(etHours.getText().toString(), 0);
                            int m = parseIntSafe(etMinutes.getText().toString(), 0);
                            int s = parseIntSafe(etSeconds.getText().toString(), 0);
                            if (y == 0 && mo == 0 && d == 0 && h == 0 && m == 0 && s == 0) {
                                s = 10;
                            }
                            Calendar c = Calendar.getInstance();
                            c.add(Calendar.YEAR, y);
                            c.add(Calendar.MONTH, mo);
                            c.add(Calendar.DAY_OF_MONTH, d);
                            c.add(Calendar.HOUR_OF_DAY, h);
                            c.add(Calendar.MINUTE, m);
                            c.add(Calendar.SECOND, s);
                            triggerMillis = c.getTimeInMillis();
                        } else {
                            triggerMillis = selectedCal.getTimeInMillis();
                        }

                        ReminderItem target = isEdit ? editingItem : new ReminderItem();
                        target.title = title;
                        target.notes = etNotes.getText().toString().trim();
                        target.triggerTimeMillis = triggerMillis;
                        target.recurrenceValue = spRecurrence.getSelectedItemPosition();
                        target.intervalMinutes = Math.max(1, parseIntSafe(etIntervalMin.getText().toString(), 30));
                        target.priorityValue = spPriority.getSelectedItemPosition();
                        target.playSound = chkSound.isChecked();
                        target.isEnabled = true;
                        target.isCompleted = false;
                        target.isDeleted = false;
                        target.touch();

                        if (!isEdit) {
                            reminders.add(target);
                        }
                        saveAndSyncChanges();
                    }
                })
                .setNegativeButton("Отмена", null)
                .show();
    }

    private void updateExactLabel(TextView tv, Calendar cal) {
        SimpleDateFormat sdf = new SimpleDateFormat("dd.MM.yyyy HH:mm:ss", Locale.getDefault());
        tv.setText("Выбрано: " + sdf.format(cal.getTime()));
    }

    private EditText addUnitField(LinearLayout parent, String label, String defaultVal) {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        lp.rightMargin = dp(4);
        col.setLayoutParams(lp);

        TextView tv = new TextView(this);
        tv.setText(label);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        col.addView(tv);

        EditText et = new EditText(this);
        et.setInputType(InputType.TYPE_CLASS_NUMBER);
        et.setText(defaultVal);
        et.setSingleLine(true);
        col.addView(et);

        parent.addView(col);
        return et;
    }

    private void addPresetBtn(
            LinearLayout row,
            String caption,
            final EditText etY,
            final EditText etMo,
            final EditText etD,
            final EditText etH,
            final EditText etM,
            final EditText etS,
            final int y,
            final int mo,
            final int d,
            final int h,
            final int m,
            final int s) {
        Button b = createStyledButton(caption, "#E2E8F0", "#1E293B");
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        b.setSingleLine(true);
        b.setMinHeight(dp(36));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        lp.rightMargin = dp(4);
        b.setLayoutParams(lp);
        b.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                etY.setText(String.valueOf(y));
                etMo.setText(String.valueOf(mo));
                etD.setText(String.valueOf(d));
                etH.setText(String.valueOf(h));
                etM.setText(String.valueOf(m));
                etS.setText(String.valueOf(s));
            }
        });
        row.addView(b);
    }

    private int parseIntSafe(String s, int fallback) {
        try {
            return Math.max(0, Integer.parseInt(s.trim()));
        } catch (Exception e) {
            return fallback;
        }
    }

    // =========================================================================
    // ДИАЛОГ НАСТРОЙКИ ОБЛАЧНОЙ СИНХРОНИЗАЦИИ С ПК (STRELO1: КОД + GIST / URL)
    // =========================================================================
    private void showCloudSyncDialog() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(18), dp(14), dp(18), dp(10));
        scroll.addView(box);

        TextView tvHelp = new TextView(this);
        tvHelp.setText("Вставьте «Код быстрого подключения» (STRELO1:...) из программы на ПК или настройте GitHub Gist / Cloud URL вручную.");
        tvHelp.setTextColor(Color.parseColor("#334155"));
        tvHelp.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        box.addView(tvHelp);

        final EditText etToken = new EditText(this);
        etToken.setHint("GitHub Personal Access Token (ghp_...)");
        etToken.setText(cloudConfig.gitHubToken);

        final EditText etGistId = new EditText(this);
        etGistId.setHint("ID облачного Gist");
        etGistId.setText(cloudConfig.gistId);

        final EditText etCustomUrl = new EditText(this);
        etCustomUrl.setHint("Или прямой Cloud JSON URL (https://...)");
        etCustomUrl.setText(cloudConfig.customCloudUrl);

        final EditText etInterval = new EditText(this);
        etInterval.setInputType(InputType.TYPE_CLASS_NUMBER);
        etInterval.setHint("Интервал автосинхронизации (сек)");
        etInterval.setText(String.valueOf(cloudConfig.autoSyncIntervalSeconds));

        // Кнопки быстрого обмена кодом STRELO1:
        LinearLayout codeRow = new LinearLayout(this);
        codeRow.setOrientation(LinearLayout.HORIZONTAL);
        codeRow.setPadding(0, dp(10), 0, dp(10));

        Button btnPasteCode = createStyledButton("Вставить код с ПК", "#16A34A", "#FFFFFF");
        btnPasteCode.setMinHeight(dp(42));
        LinearLayout.LayoutParams pasteLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        pasteLp.rightMargin = dp(6);
        btnPasteCode.setLayoutParams(pasteLp);
        btnPasteCode.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                if (cm != null && cm.hasPrimaryClip() && cm.getPrimaryClip().getItemCount() > 0) {
                    CharSequence text = cm.getPrimaryClip().getItemAt(0).getText();
                    String raw = text != null ? text.toString() : "";
                    CloudSyncConfig parsed = CloudSyncConfig.fromConnectionCode(raw);
                    if (parsed != null) {
                        cloudConfig.providerType = parsed.providerType;
                        etToken.setText(parsed.gitHubToken);
                        etGistId.setText(parsed.gistId);
                        etCustomUrl.setText(parsed.customCloudUrl);
                        etInterval.setText(String.valueOf(parsed.autoSyncIntervalSeconds));
                        Toast.makeText(MainActivity.this, "Код подключения успешно распознан!", Toast.LENGTH_SHORT).show();
                    } else {
                        Toast.makeText(MainActivity.this, "В буфере обмена нет кода STRELO1:...", Toast.LENGTH_LONG).show();
                    }
                }
            }
        });

        Button btnCopyCode = createStyledButton("Скопировать код", "#2563EB", "#FFFFFF");
        btnCopyCode.setMinHeight(dp(42));
        LinearLayout.LayoutParams copyLp = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
        );
        btnCopyCode.setLayoutParams(copyLp);
        btnCopyCode.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                applyFieldsToCloudConfig(etToken, etGistId, etCustomUrl, etInterval);
                if (!cloudConfig.isConfigured()) {
                    Toast.makeText(MainActivity.this, "Сначала заполните настройки облака.", Toast.LENGTH_SHORT).show();
                    return;
                }
                ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                if (cm != null) {
                    cm.setPrimaryClip(ClipData.newPlainText("StreloCloudCode", cloudConfig.toConnectionCode()));
                    Toast.makeText(MainActivity.this, "Код STRELO1:... скопирован!", Toast.LENGTH_SHORT).show();
                }
            }
        });

        codeRow.addView(btnPasteCode);
        codeRow.addView(btnCopyCode);
        box.addView(codeRow);

        TextView lblGist = new TextView(this);
        lblGist.setText("Вариант 1 — GitHub Gist (рекомендуется):");
        lblGist.setTypeface(Typeface.DEFAULT_BOLD);
        box.addView(lblGist);
        box.addView(etToken);
        box.addView(etGistId);

        Button btnCreateGist = createStyledButton("Создать новый Gist автоматически", "#DBEAFE", "#1E40AF");
        btnCreateGist.setMinHeight(dp(40));
        LinearLayout.LayoutParams cgLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
        );
        cgLp.topMargin = dp(4);
        cgLp.bottomMargin = dp(10);
        btnCreateGist.setLayoutParams(cgLp);
        btnCreateGist.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                final String token = etToken.getText().toString().trim();
                if (token.isEmpty()) {
                    Toast.makeText(MainActivity.this, "Введите GitHub Token ( с правом gist )!", Toast.LENGTH_SHORT).show();
                    return;
                }
                Toast.makeText(MainActivity.this, "Создание облачного Gist...", Toast.LENGTH_SHORT).show();
                new Thread(new Runnable() {
                    @Override
                    public void run() {
                        final String[] err = new String[1];
                        final String newId = CloudSyncService.createNewGist(token, reminders, err);
                        uiHandler.post(new Runnable() {
                            @Override
                            public void run() {
                                if (newId != null) {
                                    etGistId.setText(newId);
                                    Toast.makeText(MainActivity.this, "Создан Gist ID: " + newId, Toast.LENGTH_LONG).show();
                                } else {
                                    Toast.makeText(MainActivity.this, "Ошибка: " + err[0], Toast.LENGTH_LONG).show();
                                }
                            }
                        });
                    }
                }).start();
            }
        });
        box.addView(btnCreateGist);

        TextView lblCustom = new TextView(this);
        lblCustom.setText("Вариант 2 — Custom Cloud JSON URL (если не используется Gist):");
        lblCustom.setTypeface(Typeface.DEFAULT_BOLD);
        box.addView(lblCustom);
        box.addView(etCustomUrl);

        TextView lblInt = new TextView(this);
        lblInt.setText("Интервал автосинхронизации (в секундах):");
        lblInt.setPadding(0, dp(8), 0, 0);
        box.addView(lblInt);
        box.addView(etInterval);

        new AlertDialog.Builder(this)
                .setTitle("Облачная синхронизация (ПК ↔ Android)")
                .setView(scroll)
                .setPositiveButton("Сохранить и синхр.", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        applyFieldsToCloudConfig(etToken, etGistId, etCustomUrl, etInterval);
                        StorageService.saveCloudConfig(MainActivity.this, cloudConfig);
                        updateCloudStatusBadge();
                        if (cloudConfig.isConfigured()) {
                            triggerCloudSync(true, true);
                        }
                    }
                })
                .setNeutralButton("Отключить облако", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        cloudConfig.providerType = CloudSyncConfig.PROVIDER_NONE;
                        StorageService.saveCloudConfig(MainActivity.this, cloudConfig);
                        updateCloudStatusBadge();
                    }
                })
                .setNegativeButton("Закрыть", null)
                .show();
    }

    private void applyFieldsToCloudConfig(EditText etToken, EditText etGistId, EditText etCustomUrl, EditText etInterval) {
        String token = etToken.getText().toString().trim();
        String gistId = etGistId.getText().toString().trim();
        String customUrl = etCustomUrl.getText().toString().trim();
        int interval = Math.max(10, parseIntSafe(etInterval.getText().toString(), 30));

        cloudConfig.gitHubToken = token;
        cloudConfig.gistId = gistId;
        cloudConfig.customCloudUrl = customUrl;
        cloudConfig.autoSyncIntervalSeconds = interval;

        if (!token.isEmpty() && !gistId.isEmpty()) {
            cloudConfig.providerType = CloudSyncConfig.PROVIDER_GITHUB_GIST;
        } else if (!customUrl.isEmpty()) {
            cloudConfig.providerType = CloudSyncConfig.PROVIDER_CUSTOM_URL;
        } else {
            cloudConfig.providerType = CloudSyncConfig.PROVIDER_NONE;
        }
    }

    private void triggerCloudSync(final boolean forcePush, final boolean showToast) {
        if (cloudConfig == null || !cloudConfig.isConfigured() || isSyncInProgress) {
            return;
        }
        isSyncInProgress = true;
        secondsSinceLastSync = 0;
        if (tvCloudStatus != null) {
            tvCloudStatus.setText("Обмен данными с облаком...");
        }

        final List<ReminderItem> snapshot = reminders;
        new Thread(new Runnable() {
            @Override
            public void run() {
                final CloudSyncService.SyncResult res = CloudSyncService.synchronize(cloudConfig, snapshot, forcePush);
                uiHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        isSyncInProgress = false;
                        if (res.success) {
                            if (res.mergedReminders != null) {
                                reminders = res.mergedReminders;
                                StorageService.saveReminders(MainActivity.this, reminders);
                                AlarmScheduler.rescheduleAll(MainActivity.this, reminders);
                                rebuildRemindersList();
                            }
                            if (tvCloudStatus != null) {
                                tvCloudStatus.setText("✓ " + res.message);
                                tvCloudStatus.setTextColor(Color.parseColor("#A7F3D0"));
                            }
                            if (showToast) {
                                Toast.makeText(MainActivity.this, res.message, Toast.LENGTH_SHORT).show();
                            }
                        } else {
                            if (tvCloudStatus != null) {
                                tvCloudStatus.setText("⚠ " + res.message);
                                tvCloudStatus.setTextColor(Color.parseColor("#FECACA"));
                            }
                            if (showToast) {
                                Toast.makeText(MainActivity.this, res.message, Toast.LENGTH_LONG).show();
                            }
                        }
                    }
                });
            }
        }).start();
    }

    private Button createStyledButton(String text, String bgColorHex, String textColorHex) {
        Button btn = new Button(this);
        btn.setAllCaps(false);
        btn.setIncludeFontPadding(false);
        btn.setGravity(Gravity.CENTER);
        btn.setText(text);
        btn.setTextColor(Color.parseColor(textColorHex));
        btn.setTypeface(Typeface.DEFAULT_BOLD);
        btn.setPadding(dp(8), dp(6), dp(8), dp(6));
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
}
