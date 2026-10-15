package com.strelo.reminder;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.DatePickerDialog;
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
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
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
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

public class MainActivity extends Activity {

    private final List<ReminderItem> reminders = new ArrayList<>();
    private final Set<String> triggeredInSession = new HashSet<>();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private CloudSyncConfig cloudConfig;
    private boolean isSyncInProgress = false;
    private int secondsSinceLastSync = 0;

    private TextView tvClock;
    private TextView tvCloudStatus;
    private TextView tvSummary;
    private CheckBox cbDoNotDisturb;
    private LinearLayout listContainer;

    private final Runnable tickRunnable = new Runnable() {
        @Override
        public void run() {
            onTick();
            mainHandler.postDelayed(this, 1000L);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        AlarmScheduler.ensureNotificationChannel(this);
        requestNotificationPermissionIfNeeded();

        cloudConfig = StorageService.loadCloudConfig(this);
        buildMainUi();
        reloadFromStorage();
        updateCloudStatusHeader();
    }

    @Override
    protected void onResume() {
        super.onResume();
        reloadFromStorage();
        mainHandler.removeCallbacks(tickRunnable);
        mainHandler.post(tickRunnable);
        if (cloudConfig != null && cloudConfig.isConfigured()) {
            triggerBackgroundSync(false);
        }
    }

    @Override
    protected void onPause() {
        mainHandler.removeCallbacks(tickRunnable);
        super.onPause();
    }

    private void requestNotificationPermissionIfNeeded() {
        if (Build.VERSION.SDK_INT >= 33) {
            if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 101);
            }
        }
    }

    private void buildMainUi() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.parseColor("#F1F5F9"));

        // 1. ВЕРХНЯЯ ШАПКА
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.VERTICAL);
        header.setBackgroundColor(Color.parseColor("#1E3A8A"));
        header.setPadding(dp(16), dp(14), dp(16), dp(14));

        TextView tvTitle = new TextView(this);
        tvTitle.setText("🔔 Strelo Напоминалка (Android ↔ ПК)");
        tvTitle.setTextColor(Color.WHITE);
        tvTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        tvTitle.setTypeface(Typeface.DEFAULT_BOLD);

        tvClock = new TextView(this);
        tvClock.setText("Сейчас: --");
        tvClock.setTextColor(Color.parseColor("#BFDBFE"));
        tvClock.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        tvClock.setPadding(0, dp(3), 0, 0);

        tvCloudStatus = new TextView(this);
        tvCloudStatus.setText("Облако: не настроено");
        tvCloudStatus.setTextColor(Color.parseColor("#A7F3D0"));
        tvCloudStatus.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        tvCloudStatus.setPadding(0, dp(2), 0, dp(8));

        LinearLayout headerButtons = new LinearLayout(this);
        headerButtons.setOrientation(LinearLayout.HORIZONTAL);
        headerButtons.setGravity(Gravity.CENTER_VERTICAL);

        Button btnSyncNow = createButton("Синхр.", "#2563EB", "#FFFFFF");
        LinearLayout.LayoutParams syncLp = new LinearLayout.LayoutParams(dp(86), dp(36));
        btnSyncNow.setLayoutParams(syncLp);
        btnSyncNow.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (cloudConfig == null || !cloudConfig.isConfigured()) {
                    openCloudSyncDialog();
                } else {
                    triggerBackgroundSync(true);
                }
            }
        });

        Button btnCloudCfg = createButton("Облако (ПК)", "#2563EB", "#FFFFFF");
        LinearLayout.LayoutParams cloudLp = new LinearLayout.LayoutParams(dp(120), dp(36));
        cloudLp.leftMargin = dp(8);
        btnCloudCfg.setLayoutParams(cloudLp);
        btnCloudCfg.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                openCloudSyncDialog();
            }
        });

        cbDoNotDisturb = new CheckBox(this);
        cbDoNotDisturb.setText("Пауза");
        cbDoNotDisturb.setTextColor(Color.WHITE);
        cbDoNotDisturb.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        cbDoNotDisturb.setChecked(StorageService.isDoNotDisturb(this));
        LinearLayout.LayoutParams dndLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
        );
        dndLp.leftMargin = dp(10);
        cbDoNotDisturb.setLayoutParams(dndLp);
        cbDoNotDisturb.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                StorageService.setDoNotDisturb(MainActivity.this, isChecked);
            }
        });

        headerButtons.addView(btnSyncNow);
        headerButtons.addView(btnCloudCfg);
        headerButtons.addView(cbDoNotDisturb);

        header.addView(tvTitle);
        header.addView(tvClock);
        header.addView(tvCloudStatus);
        header.addView(headerButtons);
        root.addView(header);

        // 2. ПАНЕЛЬ ДЕЙСТВИЙ (ДОБАВИТЬ, ТЕСТ ОКНА, ОЧИСТИТЬ)
        LinearLayout actionToolbar = new LinearLayout(this);
        actionToolbar.setOrientation(LinearLayout.HORIZONTAL);
        actionToolbar.setPadding(dp(12), dp(10), dp(12), dp(6));

        Button btnAdd = createButton("+ Новое напоминание", "#2563EB", "#FFFFFF");
        LinearLayout.LayoutParams addLp = new LinearLayout.LayoutParams(0, dp(42), 1.4f);
        btnAdd.setLayoutParams(addLp);
        btnAdd.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                openReminderEditorDialog(null);
            }
        });

        Button btnTest = createButton("Тест окна", "#DBEAFE", "#1E40AF");
        LinearLayout.LayoutParams testLp = new LinearLayout.LayoutParams(0, dp(42), 0.85f);
        testLp.leftMargin = dp(6);
        btnTest.setLayoutParams(testLp);
        btnTest.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                Intent i = new Intent(MainActivity.this, ReminderAlertActivity.class);
                startActivity(i);
            }
        });

        Button btnClearDone = createButton("Очистить вып.", "#E2E8F0", "#334155");
        LinearLayout.LayoutParams clearLp = new LinearLayout.LayoutParams(0, dp(42), 0.95f);
        clearLp.leftMargin = dp(6);
        btnClearDone.setLayoutParams(clearLp);
        btnClearDone.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                clearCompletedReminders();
            }
        });

        actionToolbar.addView(btnAdd);
        actionToolbar.addView(btnTest);
        actionToolbar.addView(btnClearDone);
        root.addView(actionToolbar);

        // 3. ЗАГОЛОВОК СПИСКА НАПОМИНАНИЙ
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

    private void reloadFromStorage() {
        reminders.clear();
        reminders.addAll(StorageService.loadReminders(this));
        AlarmScheduler.rescheduleAll(this, reminders);
        renderRemindersList();
    }

    private void saveAndRefresh(boolean pushToCloud) {
        StorageService.saveReminders(this, reminders);
        AlarmScheduler.rescheduleAll(this, reminders);
        renderRemindersList();
        if (pushToCloud) {
            triggerBackgroundSync(true);
        }
    }

    private void onTick() {
        long now = System.currentTimeMillis();
        SimpleDateFormat sdf = new SimpleDateFormat("dd.MM.yyyy HH:mm:ss", Locale.getDefault());
        tvClock.setText("Сейчас: " + sdf.format(new Date(now)));

        if (!StorageService.isDoNotDisturb(this)) {
            for (ReminderItem r : reminders) {
                if (r != null && !r.isDeleted && r.isEnabled && !r.isCompleted) {
                    if (r.getTriggerTimeMillis() <= now && !triggeredInSession.contains(r.id)) {
                        triggeredInSession.add(r.id);
                        AlarmScheduler.triggerReminderNotificationAndPopup(this, r);
                    }
                }
            }
        }

        // Обновляем живой обратный отсчёт на карточках
        int count = listContainer.getChildCount();
        for (int i = 0; i < count; i++) {
            View card = listContainer.getChildAt(i);
            Object tag = card.getTag();
            if (tag instanceof ReminderItem) {
                ReminderItem item = (ReminderItem) tag;
                TextView tvRem = card.findViewWithTag("tv_remaining");
                if (tvRem != null) {
                    tvRem.setText("Осталось: " + item.getRemainingTimeText(now));
                }
            }
        }

        // Автосинхронизация с облаком
        if (cloudConfig != null && cloudConfig.isConfigured() && !isSyncInProgress) {
            secondsSinceLastSync++;
            int interval = Math.max(10, cloudConfig.autoSyncIntervalSeconds);
            if (secondsSinceLastSync >= interval) {
                secondsSinceLastSync = 0;
                triggerBackgroundSync(false);
            }
        }
    }

    private void renderRemindersList() {
        Collections.sort(reminders, new Comparator<ReminderItem>() {
            @Override
            public int compare(ReminderItem a, ReminderItem b) {
                boolean aAct = !a.isDeleted && a.isEnabled && !a.isCompleted;
                boolean bAct = !b.isDeleted && b.isEnabled && !b.isCompleted;
                if (aAct != bAct) {
                    return aAct ? -1 : 1;
                }
                return Long.compare(a.getTriggerTimeMillis(), b.getTriggerTimeMillis());
            }
        });

        listContainer.removeAllViews();

        long now = System.currentTimeMillis();
        int totalVisible = 0;
        int activeCount = 0;

        for (final ReminderItem item : reminders) {
            if (item == null || item.isDeleted) continue;
            totalVisible++;
            if (item.isEnabled && !item.isCompleted) {
                activeCount++;
            }

            LinearLayout card = new LinearLayout(this);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setTag(item);
            card.setPadding(dp(14), dp(12), dp(14), dp(12));
            LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
            );
            cardLp.bottomMargin = dp(10);
            card.setLayoutParams(cardLp);

            GradientDrawable bg = new GradientDrawable();
            bg.setCornerRadius(dp(10));
            if (item.isCompleted) {
                bg.setColor(Color.parseColor("#F8FAFC"));
                bg.setStroke(dp(1), Color.parseColor("#E2E8F0"));
            } else if (!item.isEnabled) {
                bg.setColor(Color.parseColor("#F1F5F9"));
                bg.setStroke(dp(1), Color.parseColor("#CBD5E1"));
            } else if (item.priorityValue == ReminderItem.PRIORITY_HIGH) {
                bg.setColor(Color.parseColor("#FEF2F2"));
                bg.setStroke(dp(1), Color.parseColor("#FCA5A5"));
            } else {
                bg.setColor(Color.WHITE);
                bg.setStroke(dp(1), Color.parseColor("#CBD5E1"));
            }
            card.setBackground(bg);

            String statusBadge;
            if (item.isCompleted) statusBadge = "✓ Выполнено";
            else if (!item.isEnabled) statusBadge = "⏸ Пауза";
            else if (item.priorityValue == ReminderItem.PRIORITY_HIGH) statusBadge = "⚡ Важное";
            else statusBadge = "🟢 Активно";

            TextView tvTitle = new TextView(this);
            tvTitle.setText(statusBadge + "  •  " + item.title);
            tvTitle.setTextColor(item.isCompleted ? Color.parseColor("#64748B") : Color.parseColor("#0F172A"));
            tvTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
            tvTitle.setTypeface(Typeface.DEFAULT_BOLD);
            card.addView(tvTitle);

            if (item.notes != null && !item.notes.trim().isEmpty()) {
                TextView tvNotes = new TextView(this);
                tvNotes.setText(item.notes);
                tvNotes.setTextColor(Color.parseColor("#475569"));
                tvNotes.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
                tvNotes.setPadding(0, dp(3), 0, 0);
                card.addView(tvNotes);
            }

            TextView tvWhen = new TextView(this);
            tvWhen.setText("Когда: " + item.getFormattedTriggerTime() + "  (" + item.getRecurrenceDisplayText() + ")");
            tvWhen.setTextColor(Color.parseColor("#334155"));
            tvWhen.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            tvWhen.setPadding(0, dp(5), 0, 0);
            card.addView(tvWhen);

            TextView tvRem = new TextView(this);
            tvRem.setTag("tv_remaining");
            tvRem.setText("Осталось: " + item.getRemainingTimeText(now));
            tvRem.setTextColor(Color.parseColor("#1E40AF"));
            tvRem.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            tvRem.setTypeface(Typeface.DEFAULT_BOLD);
            tvRem.setPadding(0, dp(2), 0, dp(8));
            card.addView(tvRem);

            // Кнопки управления карточкой
            LinearLayout btnRow = new LinearLayout(this);
            btnRow.setOrientation(LinearLayout.HORIZONTAL);

            Button btnDone = createButton("✓ ОК", "#DCFCE7", "#166534");
            LinearLayout.LayoutParams lp1 = new LinearLayout.LayoutParams(0, dp(34), 1f);
            btnDone.setLayoutParams(lp1);
            btnDone.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            btnDone.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    item.advanceAfterComplete(System.currentTimeMillis());
                    triggeredInSession.remove(item.id);
                    saveAndRefresh(true);
                }
            });

            Button btnSnooze10 = createButton("+10 мин", "#DBEAFE", "#1E40AF");
            LinearLayout.LayoutParams lp2 = new LinearLayout.LayoutParams(0, dp(34), 1.1f);
            lp2.leftMargin = dp(5);
            btnSnooze10.setLayoutParams(lp2);
            btnSnooze10.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            btnSnooze10.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    item.snoozeByOffset(0, 0, 0, 0, 10, 0);
                    triggeredInSession.remove(item.id);
                    saveAndRefresh(true);
                }
            });

            Button btnToggle = createButton(item.isEnabled && !item.isCompleted ? "Пауза" : "Вкл", "#E2E8F0", "#334155");
            LinearLayout.LayoutParams lp3 = new LinearLayout.LayoutParams(0, dp(34), 1f);
            lp3.leftMargin = dp(5);
            btnToggle.setLayoutParams(lp3);
            btnToggle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            btnToggle.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (item.isCompleted) {
                        item.isCompleted = false;
                        item.isEnabled = true;
                        if (item.getTriggerTimeMillis() <= System.currentTimeMillis()) {
                            item.snoozeByOffset(0, 0, 0, 0, 15, 0);
                        }
                    } else {
                        item.isEnabled = !item.isEnabled;
                    }
                    item.touch();
                    triggeredInSession.remove(item.id);
                    saveAndRefresh(true);
                }
            });

            Button btnEdit = createButton("Изм.", "#E2E8F0", "#1E293B");
            LinearLayout.LayoutParams lp4 = new LinearLayout.LayoutParams(0, dp(34), 0.95f);
            lp4.leftMargin = dp(5);
            btnEdit.setLayoutParams(lp4);
            btnEdit.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            btnEdit.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    openReminderEditorDialog(item);
                }
            });

            Button btnDel = createButton("Удал.", "#FEE2E2", "#B91C1C");
            LinearLayout.LayoutParams lp5 = new LinearLayout.LayoutParams(0, dp(34), 0.95f);
            lp5.leftMargin = dp(5);
            btnDel.setLayoutParams(lp5);
            btnDel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            btnDel.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    item.isDeleted = true;
                    item.isEnabled = false;
                    item.touch();
                    saveAndRefresh(true);
                }
            });

            btnRow.addView(btnDone);
            btnRow.addView(btnSnooze10);
            btnRow.addView(btnToggle);
            btnRow.addView(btnEdit);
            btnRow.addView(btnDel);
            card.addView(btnRow);

            listContainer.addView(card);
        }

        if (totalVisible == 0) {
            TextView tvEmpty = new TextView(this);
            tvEmpty.setText("Список напоминаний пуст.\nНажмите «+ Новое напоминание» или настройте «Облако (ПК)» для синхронизации с компьютером.");
            tvEmpty.setTextColor(Color.parseColor("#64748B"));
            tvEmpty.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
            tvEmpty.setGravity(Gravity.CENTER);
            tvEmpty.setPadding(dp(20), dp(40), dp(20), dp(40));
            listContainer.addView(tvEmpty);
        }

        tvSummary.setText(String.format(
                Locale.getDefault(),
                "Список напоминаний (Всего: %d, Активных: %d)",
                totalVisible,
                activeCount
        ));
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
            saveAndRefresh(true);
        }
    }

    /**
     * Диалог создания / редактирования напоминания с выбором:
     * 1) «Через сколько» (Годы, Месяцы, Дни, Часы, Минуты, Секунды + быстрые кнопки + живой предпросмотр)
     * 2) «В точное время и дату» (DatePicker + TimePicker)
     */
    private void openReminderEditorDialog(final ReminderItem editingItem) {
        final boolean isEdit = (editingItem != null);

        ScrollView scroll = new ScrollView(this);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(dp(16), dp(12), dp(16), dp(12));

        TextView lblTitle = new TextView(this);
        lblTitle.setText("О чём напомнить:");
        lblTitle.setTypeface(Typeface.DEFAULT_BOLD);
        layout.addView(lblTitle);

        final EditText etTitle = new EditText(this);
        etTitle.setHint("Например: Позвонить клиенту");
        if (isEdit) etTitle.setText(editingItem.title);
        layout.addView(etTitle);

        TextView lblNotes = new TextView(this);
        lblNotes.setText("Описание / заметка (необязательно):");
        lblNotes.setPadding(0, dp(8), 0, 0);
        layout.addView(lblNotes);

        final EditText etNotes = new EditText(this);
        etNotes.setHint("Дополнительные детали...");
        if (isEdit) etNotes.setText(editingItem.notes);
        layout.addView(etNotes);

        // Выбор режима времени
        RadioGroup rgMode = new RadioGroup(this);
        rgMode.setOrientation(RadioGroup.HORIZONTAL);
        rgMode.setPadding(0, dp(10), 0, dp(6));

        final RadioButton rbOffset = new RadioButton(this);
        rbOffset.setText("Через сколько");
        rbOffset.setId(View.generateViewId());

        final RadioButton rbExact = new RadioButton(this);
        rbExact.setText("Точная дата и время");
        rbExact.setId(View.generateViewId());

        rgMode.addView(rbOffset);
        rgMode.addView(rbExact);
        layout.addView(rgMode);

        // Блок 1: 6 полей (Годы, Месяцы, Дни, Часы, Минуты, Секунды)
        final LinearLayout pnlOffset = new LinearLayout(this);
        pnlOffset.setOrientation(LinearLayout.VERTICAL);
        pnlOffset.setPadding(dp(8), dp(8), dp(8), dp(8));
        pnlOffset.setBackgroundColor(Color.parseColor("#F8FAFC"));

        LinearLayout rowUnits1 = new LinearLayout(this);
        rowUnits1.setOrientation(LinearLayout.HORIZONTAL);
        final EditText etYears = createUnitField(rowUnits1, "Годы", "0");
        final EditText etMonths = createUnitField(rowUnits1, "Месяцы", "0");
        final EditText etDays = createUnitField(rowUnits1, "Дни", "0");
        pnlOffset.addView(rowUnits1);

        LinearLayout rowUnits2 = new LinearLayout(this);
        rowUnits2.setOrientation(LinearLayout.HORIZONTAL);
        rowUnits2.setPadding(0, dp(6), 0, 0);
        final EditText etHours = createUnitField(rowUnits2, "Часы", "0");
        final EditText etMinutes = createUnitField(rowUnits2, "Минуты", "15");
        final EditText etSeconds = createUnitField(rowUnits2, "Секунды", "0");
        pnlOffset.addView(rowUnits2);

        if (isEdit) {
            int[] diff = ReminderItem.calculateCalendarDiff(System.currentTimeMillis(), editingItem.getTriggerTimeMillis());
            etYears.setText(String.valueOf(diff[0]));
            etMonths.setText(String.valueOf(diff[1]));
            etDays.setText(String.valueOf(diff[2]));
            etHours.setText(String.valueOf(diff[3]));
            etMinutes.setText(String.valueOf(diff[4]));
            etSeconds.setText(String.valueOf(diff[5]));
        }

        // Быстрые кнопки пресетов
        LinearLayout presetRow1 = new LinearLayout(this);
        presetRow1.setOrientation(LinearLayout.HORIZONTAL);
        presetRow1.setPadding(0, dp(8), 0, 0);

        LinearLayout presetRow2 = new LinearLayout(this);
        presetRow2.setOrientation(LinearLayout.HORIZONTAL);
        presetRow2.setPadding(0, dp(4), 0, 0);

        final TextView tvPreview = new TextView(this);
        tvPreview.setTextColor(Color.parseColor("#1E40AF"));
        tvPreview.setTypeface(Typeface.DEFAULT_BOLD);
        tvPreview.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        tvPreview.setPadding(0, dp(8), 0, 0);

        final Runnable updatePreview = new Runnable() {
            @Override
            public void run() {
                long targetMs = computeOffsetTargetMillis(etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
                SimpleDateFormat sdf = new SimpleDateFormat("dd.MM.yyyy HH:mm:ss", Locale.getDefault());
                tvPreview.setText("Сработает: " + sdf.format(new Date(targetMs)));
            }
        };

        TextWatcher watcher = new TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {}

            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {}

            @Override
            public void afterTextChanged(Editable s) {
                updatePreview.run();
            }
        };
        etYears.addTextChangedListener(watcher);
        etMonths.addTextChangedListener(watcher);
        etDays.addTextChangedListener(watcher);
        etHours.addTextChangedListener(watcher);
        etMinutes.addTextChangedListener(watcher);
        etSeconds.addTextChangedListener(watcher);

        addPresetButton(presetRow1, "30 сек", 0, 0, 0, 0, 0, 30, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
        addPresetButton(presetRow1, "5 мин", 0, 0, 0, 0, 5, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
        addPresetButton(presetRow1, "15 мин", 0, 0, 0, 0, 15, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
        addPresetButton(presetRow1, "1 час", 0, 0, 0, 1, 0, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);

        addPresetButton(presetRow2, "1 день", 0, 0, 1, 0, 0, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
        addPresetButton(presetRow2, "1 мес", 0, 1, 0, 0, 0, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
        addPresetButton(presetRow2, "1 год", 1, 0, 0, 0, 0, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
        addPresetButton(presetRow2, "Сброс", 0, 0, 0, 0, 0, 0, etYears, etMonths, etDays, etHours, etMinutes, etSeconds);

        pnlOffset.addView(presetRow1);
        pnlOffset.addView(presetRow2);
        pnlOffset.addView(tvPreview);
        layout.addView(pnlOffset);
        updatePreview.run();

        // Блок 2: Точная дата и время
        final LinearLayout pnlExact = new LinearLayout(this);
        pnlExact.setOrientation(LinearLayout.VERTICAL);
        pnlExact.setPadding(dp(8), dp(8), dp(8), dp(8));
        pnlExact.setBackgroundColor(Color.parseColor("#F8FAFC"));
        pnlExact.setVisibility(View.GONE);

        final Calendar selectedCal = Calendar.getInstance();
        if (isEdit) {
            selectedCal.setTimeInMillis(editingItem.getTriggerTimeMillis());
        } else {
            selectedCal.add(Calendar.MINUTE, 15);
        }

        final TextView tvSelectedExact = new TextView(this);
        tvSelectedExact.setTypeface(Typeface.DEFAULT_BOLD);
        tvSelectedExact.setTextColor(Color.parseColor("#1E40AF"));
        final SimpleDateFormat exactSdf = new SimpleDateFormat("dd.MM.yyyy HH:mm:ss", Locale.getDefault());
        tvSelectedExact.setText("Выбрано: " + exactSdf.format(selectedCal.getTime()));

        LinearLayout exactBtnRow = new LinearLayout(this);
        exactBtnRow.setOrientation(LinearLayout.HORIZONTAL);
        exactBtnRow.setPadding(0, dp(6), 0, 0);

        Button btnPickDate = createButton("Выбрать дату", "#E2E8F0", "#1E293B");
        LinearLayout.LayoutParams pdLp = new LinearLayout.LayoutParams(0, dp(38), 1f);
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
                                tvSelectedExact.setText("Выбрано: " + exactSdf.format(selectedCal.getTime()));
                            }
                        },
                        selectedCal.get(Calendar.YEAR),
                        selectedCal.get(Calendar.MONTH),
                        selectedCal.get(Calendar.DAY_OF_MONTH)
                ).show();
            }
        });

        Button btnPickTime = createButton("Выбрать время", "#E2E8F0", "#1E293B");
        LinearLayout.LayoutParams ptLp = new LinearLayout.LayoutParams(0, dp(38), 1f);
        ptLp.leftMargin = dp(8);
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
                                tvSelectedExact.setText("Выбрано: " + exactSdf.format(selectedCal.getTime()));
                            }
                        },
                        selectedCal.get(Calendar.HOUR_OF_DAY),
                        selectedCal.get(Calendar.MINUTE),
                        true
                ).show();
            }
        });

        exactBtnRow.addView(btnPickDate);
        exactBtnRow.addView(btnPickTime);
        pnlExact.addView(tvSelectedExact);
        pnlExact.addView(exactBtnRow);
        layout.addView(pnlExact);

        rgMode.setOnCheckedChangeListener(new RadioGroup.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(RadioGroup group, int checkedId) {
                boolean offsetMode = (checkedId == rbOffset.getId());
                pnlOffset.setVisibility(offsetMode ? View.VISIBLE : View.GONE);
                pnlExact.setVisibility(offsetMode ? View.GONE : View.VISIBLE);
            }
        });
        if (isEdit) {
            rbExact.setChecked(true);
        } else {
            rbOffset.setChecked(true);
        }

        // Повтор
        TextView lblRec = new TextView(this);
        lblRec.setText("Повтор:");
        lblRec.setPadding(0, dp(10), 0, 0);
        layout.addView(lblRec);

        final Spinner spRecurrence = new Spinner(this);
        String[] recOptions = new String[]{
                "Один раз (без повтора)",
                "Каждые N минут",
                "Ежедневно",
                "По будням (Пн–Пт)",
                "Еженедельно"
        };
        spRecurrence.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, recOptions));
        if (isEdit) spRecurrence.setSelection(editingItem.recurrenceValue);
        layout.addView(spRecurrence);

        final EditText etIntervalMins = new EditText(this);
        etIntervalMins.setHint("Интервал в минутах (например 30)");
        etIntervalMins.setInputType(InputType.TYPE_CLASS_NUMBER);
        etIntervalMins.setText(isEdit ? String.valueOf(editingItem.intervalMinutes) : "30");
        etIntervalMins.setVisibility(isEdit && editingItem.recurrenceValue == ReminderItem.RECURRENCE_EVERY_MINUTES ? View.VISIBLE : View.GONE);
        layout.addView(etIntervalMins);

        spRecurrence.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                etIntervalMins.setVisibility(position == ReminderItem.RECURRENCE_EVERY_MINUTES ? View.VISIBLE : View.GONE);
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {}
        });

        // Важность и звук
        TextView lblPrio = new TextView(this);
        lblPrio.setText("Важность:");
        lblPrio.setPadding(0, dp(8), 0, 0);
        layout.addView(lblPrio);

        final Spinner spPriority = new Spinner(this);
        String[] prioOptions = new String[]{"Обычное", "Важное (!)"};
        spPriority.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, prioOptions));
        if (isEdit) spPriority.setSelection(editingItem.priorityValue);
        layout.addView(spPriority);

        final CheckBox cbSound = new CheckBox(this);
        cbSound.setText("Звуковой сигнал и вибрация");
        cbSound.setChecked(!isEdit || editingItem.playSound);
        layout.addView(cbSound);

        scroll.addView(layout);

        new AlertDialog.Builder(this)
                .setTitle(isEdit ? "Редактирование напоминания" : "Новое напоминание")
                .setView(scroll)
                .setPositiveButton("Сохранить", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        String title = etTitle.getText().toString().trim();
                        if (title.isEmpty()) {
                            Toast.makeText(MainActivity.this, "Введите текст напоминания", Toast.LENGTH_SHORT).show();
                            return;
                        }

                        long triggerMillis;
                        if (rbOffset.isChecked()) {
                            triggerMillis = computeOffsetTargetMillis(etYears, etMonths, etDays, etHours, etMinutes, etSeconds);
                        } else {
                            triggerMillis = selectedCal.getTimeInMillis();
                        }

                        ReminderItem target = isEdit ? editingItem : new ReminderItem();
                        target.title = title;
                        target.notes = etNotes.getText().toString().trim();
                        target.setTriggerTimeMillis(triggerMillis);
                        target.recurrenceValue = spRecurrence.getSelectedItemPosition();
                        int interval = 30;
                        try {
                            interval = Math.max(1, Integer.parseInt(etIntervalMins.getText().toString().trim()));
                        } catch (Exception ignored) {
                        }
                        target.intervalMinutes = interval;
                        target.priorityValue = spPriority.getSelectedItemPosition();
                        target.playSound = cbSound.isChecked();
                        target.isEnabled = true;
                        target.isCompleted = false;
                        target.isDeleted = false;
                        target.touch();

                        if (!isEdit) {
                            reminders.add(target);
                        }
                        triggeredInSession.remove(target.id);
                        saveAndRefresh(true);
                    }
                })
                .setNegativeButton("Отмена", null)
                .show();
    }

    private EditText createUnitField(LinearLayout parentRow, String label, String defaultVal) {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams colLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        colLp.rightMargin = dp(4);
        col.setLayoutParams(colLp);

        TextView tv = new TextView(this);
        tv.setText(label);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        tv.setTextColor(Color.parseColor("#475569"));

        EditText et = new EditText(this);
        et.setText(defaultVal);
        et.setInputType(InputType.TYPE_CLASS_NUMBER);
        et.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);

        col.addView(tv);
        col.addView(et);
        parentRow.addView(col);
        return et;
    }

    private void addPresetButton(
            LinearLayout row,
            String text,
            final int y,
            final int mo,
            final int d,
            final int h,
            final int m,
            final int s,
            final EditText etY,
            final EditText etMo,
            final EditText etD,
            final EditText etH,
            final EditText etM,
            final EditText etS) {

        Button btn = createButton(text, "Сброс".equals(text) ? "#FEE2E2" : "#E2E8F0", "Сброс".equals(text) ? "#B91C1C" : "#1E293B");
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, dp(32), 1f);
        lp.rightMargin = dp(4);
        btn.setLayoutParams(lp);
        btn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        btn.setOnClickListener(new View.OnClickListener() {
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
        row.addView(btn);
    }

    private static int parseUnitInt(EditText et) {
        try {
            return Math.max(0, Integer.parseInt(et.getText().toString().trim()));
        } catch (Exception e) {
            return 0;
        }
    }

    private static long computeOffsetTargetMillis(
            EditText etY,
            EditText etMo,
            EditText etD,
            EditText etH,
            EditText etM,
            EditText etS) {

        int y = parseUnitInt(etY);
        int mo = parseUnitInt(etMo);
        int d = parseUnitInt(etD);
        int h = parseUnitInt(etH);
        int m = parseUnitInt(etM);
        int s = parseUnitInt(etS);

        if (y == 0 && mo == 0 && d == 0 && h == 0 && m == 0 && s == 0) {
            s = 10;
        }

        Calendar cal = Calendar.getInstance();
        cal.add(Calendar.YEAR, y);
        cal.add(Calendar.MONTH, mo);
        cal.add(Calendar.DAY_OF_MONTH, d);
        cal.add(Calendar.HOUR_OF_DAY, h);
        cal.add(Calendar.MINUTE, m);
        cal.add(Calendar.SECOND, s);
        return cal.getTimeInMillis();
    }

    /**
     * Диалог настройки облачной синхронизации между Android и ПК Windows.
     */
    private void openCloudSyncDialog() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(dp(16), dp(12), dp(16), dp(12));

        TextView tvIntro = new TextView(this);
        tvIntro.setText("Быстрое подключение: скопируйте код подключения в программе Strelo на компьютере (кнопка «Облако (Android)») и нажмите кнопку ниже:");
        tvIntro.setTextColor(Color.parseColor("#334155"));
        tvIntro.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        layout.addView(tvIntro);

        LinearLayout codeBtnRow = new LinearLayout(this);
        codeBtnRow.setOrientation(LinearLayout.HORIZONTAL);
        codeBtnRow.setPadding(0, dp(8), 0, dp(10));

        Button btnPasteCode = createButton("Вставить код с ПК", "#DBEAFE", "#1E40AF");
        LinearLayout.LayoutParams pasteLp = new LinearLayout.LayoutParams(0, dp(38), 1f);
        btnPasteCode.setLayoutParams(pasteLp);

        Button btnCopyCode = createButton("Скопировать код", "#E2E8F0", "#1E293B");
        LinearLayout.LayoutParams copyLp = new LinearLayout.LayoutParams(0, dp(38), 1f);
        copyLp.leftMargin = dp(6);
        btnCopyCode.setLayoutParams(copyLp);

        codeBtnRow.addView(btnPasteCode);
        codeBtnRow.addView(btnCopyCode);
        layout.addView(codeBtnRow);

        TextView lblMode = new TextView(this);
        lblMode.setText("Способ облачной синхронизации:");
        lblMode.setTypeface(Typeface.DEFAULT_BOLD);
        layout.addView(lblMode);

        final Spinner spProvider = new Spinner(this);
        String[] providers = new String[]{
                "Отключено (только локально)",
                "GitHub Gist (бесплатное облако)",
                "Прямой облачный JSON URL"
        };
        spProvider.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, providers));
        spProvider.setSelection(cloudConfig.providerTypeValue);
        layout.addView(spProvider);

        final EditText etToken = new EditText(this);
        etToken.setHint("GitHub Token (с правом gist)");
        etToken.setText(cloudConfig.gitHubToken);
        layout.addView(etToken);

        final EditText etGistId = new EditText(this);
        etGistId.setHint("ID облачного Gist");
        etGistId.setText(cloudConfig.gistId);
        layout.addView(etGistId);

        final Button btnCreateGist = createButton("Создать новое облако Gist", "#DBEAFE", "#1E40AF");
        LinearLayout.LayoutParams cgLp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(38)
        );
        cgLp.topMargin = dp(4);
        btnCreateGist.setLayoutParams(cgLp);
        layout.addView(btnCreateGist);

        final EditText etCustomUrl = new EditText(this);
        etCustomUrl.setHint("HTTPS URL облачного JSON (GET/PUT)");
        etCustomUrl.setText(cloudConfig.customCloudUrl);
        layout.addView(etCustomUrl);

        TextView lblInterval = new TextView(this);
        lblInterval.setText("Интервал автосинхронизации (сек.):");
        lblInterval.setPadding(0, dp(8), 0, 0);
        layout.addView(lblInterval);

        final EditText etInterval = new EditText(this);
        etInterval.setInputType(InputType.TYPE_CLASS_NUMBER);
        etInterval.setText(String.valueOf(cloudConfig.autoSyncIntervalSeconds));
        layout.addView(etInterval);

        spProvider.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                boolean isGist = (position == CloudSyncConfig.PROVIDER_GITHUB_GIST);
                boolean isUrl = (position == CloudSyncConfig.PROVIDER_CUSTOM_URL);
                etToken.setVisibility(isGist ? View.VISIBLE : View.GONE);
                etGistId.setVisibility(isGist ? View.VISIBLE : View.GONE);
                btnCreateGist.setVisibility(isGist ? View.VISIBLE : View.GONE);
                etCustomUrl.setVisibility(isUrl ? View.VISIBLE : View.GONE);
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {}
        });

        btnPasteCode.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                if (cm != null && cm.hasPrimaryClip() && cm.getPrimaryClip() != null && cm.getPrimaryClip().getItemCount() > 0) {
                    CharSequence text = cm.getPrimaryClip().getItemAt(0).getText();
                    CloudSyncConfig parsed = CloudSyncConfig.fromConnectionCode(text != null ? text.toString() : "");
                    if (parsed != null) {
                        spProvider.setSelection(parsed.providerTypeValue);
                        etToken.setText(parsed.gitHubToken);
                        etGistId.setText(parsed.gistId);
                        etCustomUrl.setText(parsed.customCloudUrl);
                        etInterval.setText(String.valueOf(parsed.autoSyncIntervalSeconds));
                        Toast.makeText(MainActivity.this, "Код подключения успешно загружен!", Toast.LENGTH_SHORT).show();
                        return;
                    }
                }
                Toast.makeText(MainActivity.this, "В буфере обмена не найден код STRELO1:...", Toast.LENGTH_SHORT).show();
            }
        });

        btnCopyCode.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                CloudSyncConfig temp = new CloudSyncConfig();
                temp.providerTypeValue = spProvider.getSelectedItemPosition();
                temp.gitHubToken = etToken.getText().toString().trim();
                temp.gistId = etGistId.getText().toString().trim();
                temp.customCloudUrl = etCustomUrl.getText().toString().trim();
                try {
                    temp.autoSyncIntervalSeconds = Math.max(10, Integer.parseInt(etInterval.getText().toString().trim()));
                } catch (Exception ignored) {
                }
                if (!temp.isConfigured()) {
                    Toast.makeText(MainActivity.this, "Сначала заполните настройки облака", Toast.LENGTH_SHORT).show();
                    return;
                }
                ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                if (cm != null) {
                    cm.setPrimaryClip(ClipData.newPlainText("StreloSyncCode", temp.toConnectionCode()));
                    Toast.makeText(MainActivity.this, "Код скопирован! Вставьте его на ПК.", Toast.LENGTH_SHORT).show();
                }
            }
        });

        btnCreateGist.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                final String token = etToken.getText().toString().trim();
                if (token.isEmpty()) {
                    Toast.makeText(MainActivity.this, "Введите GitHub Token", Toast.LENGTH_SHORT).show();
                    return;
                }
                Toast.makeText(MainActivity.this, "Создание облака Gist...", Toast.LENGTH_SHORT).show();
                final List<ReminderItem> snapshot = new ArrayList<>(reminders);
                new Thread(new Runnable() {
                    @Override
                    public void run() {
                        final CloudSyncService.CreateGistResult res = CloudSyncService.createNewGist(token, snapshot);
                        mainHandler.post(new Runnable() {
                            @Override
                            public void run() {
                                if (res.success) {
                                    etGistId.setText(res.gistId);
                                    Toast.makeText(MainActivity.this, "Облако создано! ID: " + res.gistId, Toast.LENGTH_LONG).show();
                                } else {
                                    Toast.makeText(MainActivity.this, res.errorMessage, Toast.LENGTH_LONG).show();
                                }
                            }
                        });
                    }
                }).start();
            }
        });

        scroll.addView(layout);

        new AlertDialog.Builder(this)
                .setTitle("Облачная синхронизация (Android ↔ ПК)")
                .setView(scroll)
                .setPositiveButton("Сохранить и синхр.", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        cloudConfig.providerTypeValue = spProvider.getSelectedItemPosition();
                        cloudConfig.gitHubToken = etToken.getText().toString().trim();
                        cloudConfig.gistId = etGistId.getText().toString().trim();
                        cloudConfig.customCloudUrl = etCustomUrl.getText().toString().trim();
                        try {
                            cloudConfig.autoSyncIntervalSeconds = Math.max(10, Integer.parseInt(etInterval.getText().toString().trim()));
                        } catch (Exception e) {
                            cloudConfig.autoSyncIntervalSeconds = 20;
                        }
                        StorageService.saveCloudConfig(MainActivity.this, cloudConfig);
                        updateCloudStatusHeader();
                        if (cloudConfig.isConfigured()) {
                            triggerBackgroundSync(true);
                        }
                    }
                })
                .setNegativeButton("Закрыть", null)
                .show();
    }

    private void updateCloudStatusHeader() {
        if (cloudConfig == null || !cloudConfig.isConfigured()) {
            tvCloudStatus.setText("Облако: не настроено (нажмите «Облако (ПК)»)");
            tvCloudStatus.setTextColor(Color.parseColor("#BFDBFE"));
        } else {
            String mode = cloudConfig.providerTypeValue == CloudSyncConfig.PROVIDER_GITHUB_GIST
                    ? "GitHub Gist"
                    : "Cloud URL";
            tvCloudStatus.setText("Облако активно (" + mode + ")");
            tvCloudStatus.setTextColor(Color.parseColor("#A7F3D0"));
        }
    }

    private void triggerBackgroundSync(final boolean forcePush) {
        if (cloudConfig == null || !cloudConfig.isConfigured() || isSyncInProgress) {
            return;
        }
        isSyncInProgress = true;
        secondsSinceLastSync = 0;
        tvCloudStatus.setText("Синхронизация с облаком...");

        final List<ReminderItem> snapshot = new ArrayList<>();
        for (ReminderItem r : reminders) {
            if (r != null) snapshot.add(r.copy());
        }

        new Thread(new Runnable() {
            @Override
            public void run() {
                final CloudSyncService.SyncResult res = CloudSyncService.synchronize(cloudConfig, snapshot, forcePush);
                mainHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        isSyncInProgress = false;
                        if (res.success) {
                            tvCloudStatus.setText("Облако: " + res.message);
                            tvCloudStatus.setTextColor(Color.parseColor("#A7F3D0"));
                            if (res.localListUpdated && res.mergedReminders != null) {
                                boolean[] dummy = new boolean[2];
                                List<ReminderItem> merged = CloudSyncService.mergeReminders(reminders, res.mergedReminders, dummy);
                                reminders.clear();
                                reminders.addAll(merged);
                                StorageService.saveReminders(MainActivity.this, reminders);
                                AlarmScheduler.rescheduleAll(MainActivity.this, reminders);
                                renderRemindersList();
                            }
                        } else {
                            tvCloudStatus.setText(res.message);
                            tvCloudStatus.setTextColor(Color.parseColor("#FECACA"));
                        }
                    }
                });
            }
        }).start();
    }

    private Button createButton(String text, String bgHex, String fgHex) {
        Button btn = new Button(this);
        btn.setAllCaps(false);
        btn.setText(text);
        btn.setTextColor(Color.parseColor(fgHex));
        btn.setTypeface(Typeface.DEFAULT_BOLD);
        btn.setPadding(dp(8), dp(4), dp(8), dp(4));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.parseColor(bgHex));
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
