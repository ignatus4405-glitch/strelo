package com.strelo.reminder;

import org.json.JSONException;
import org.json.JSONObject;

import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Calendar;
import java.util.Date;
import java.util.Locale;
import java.util.UUID;

public class ReminderItem {
    public static final int RECURRENCE_NONE = 0;
    public static final int RECURRENCE_EVERY_MINUTES = 1;
    public static final int RECURRENCE_DAILY = 2;
    public static final int RECURRENCE_WEEKDAYS = 3;
    public static final int RECURRENCE_WEEKLY = 4;

    public static final int PRIORITY_NORMAL = 0;
    public static final int PRIORITY_HIGH = 1;

    private static final String ISO_PATTERN = "yyyy-MM-dd'T'HH:mm:ss";
    private static final String DISPLAY_PATTERN = "dd.MM.yyyy HH:mm:ss";

    public String id;
    public String title;
    public String notes;
    public String triggerTimeIso;
    public int recurrenceValue;
    public int intervalMinutes;
    public int priorityValue;
    public boolean isEnabled;
    public boolean isCompleted;
    public boolean playSound;
    public long updatedAtUnixMs;
    public boolean isDeleted;

    public ReminderItem() {
        this.id = UUID.randomUUID().toString().replace("-", "");
        this.title = "";
        this.notes = "";
        Calendar cal = Calendar.getInstance();
        cal.add(Calendar.MINUTE, 15);
        setTriggerTimeMillis(cal.getTimeInMillis());
        this.recurrenceValue = RECURRENCE_NONE;
        this.intervalMinutes = 30;
        this.priorityValue = PRIORITY_NORMAL;
        this.isEnabled = true;
        this.isCompleted = false;
        this.playSound = true;
        this.updatedAtUnixMs = System.currentTimeMillis();
        this.isDeleted = false;
    }

    public void touch() {
        this.updatedAtUnixMs = System.currentTimeMillis();
    }

    public ReminderItem copy() {
        ReminderItem c = new ReminderItem();
        c.id = this.id;
        c.title = this.title;
        c.notes = this.notes;
        c.triggerTimeIso = this.triggerTimeIso;
        c.recurrenceValue = this.recurrenceValue;
        c.intervalMinutes = this.intervalMinutes;
        c.priorityValue = this.priorityValue;
        c.isEnabled = this.isEnabled;
        c.isCompleted = this.isCompleted;
        c.playSound = this.playSound;
        c.updatedAtUnixMs = this.updatedAtUnixMs;
        c.isDeleted = this.isDeleted;
        return c;
    }

    public long getTriggerTimeMillis() {
        if (triggerTimeIso == null || triggerTimeIso.isEmpty()) {
            return System.currentTimeMillis();
        }
        try {
            String normalized = triggerTimeIso.trim();
            if (normalized.length() > 19) {
                normalized = normalized.substring(0, 19);
            }
            SimpleDateFormat sdf = new SimpleDateFormat(ISO_PATTERN, Locale.US);
            Date d = sdf.parse(normalized);
            return d != null ? d.getTime() : System.currentTimeMillis();
        } catch (ParseException e) {
            return System.currentTimeMillis();
        }
    }

    public void setTriggerTimeMillis(long millis) {
        SimpleDateFormat sdf = new SimpleDateFormat(ISO_PATTERN, Locale.US);
        this.triggerTimeIso = sdf.format(new Date(millis));
    }

    public String getFormattedTriggerTime() {
        SimpleDateFormat sdf = new SimpleDateFormat(DISPLAY_PATTERN, Locale.getDefault());
        return sdf.format(new Date(getTriggerTimeMillis()));
    }

    public String getRecurrenceDisplayText() {
        switch (recurrenceValue) {
            case RECURRENCE_NONE:
                return "Один раз";
            case RECURRENCE_EVERY_MINUTES:
                if (intervalMinutes >= 60 && intervalMinutes % 60 == 0) {
                    return String.format(Locale.getDefault(), "Каждые %d ч.", intervalMinutes / 60);
                }
                return String.format(Locale.getDefault(), "Каждые %d мин.", intervalMinutes);
            case RECURRENCE_DAILY:
                return "Ежедневно";
            case RECURRENCE_WEEKDAYS:
                return "По будням (Пн–Пт)";
            case RECURRENCE_WEEKLY:
                return "Еженедельно";
            default:
                return "Один раз";
        }
    }

    public String getRemainingTimeText(long nowMillis) {
        if (isCompleted) return "Выполнено";
        if (!isEnabled) return "Пауза";

        long targetMillis = getTriggerTimeMillis();
        long diffMs = targetMillis - nowMillis;
        if (diffMs <= 0) {
            return "Срабатывает!";
        }

        int[] parts = calculateCalendarDiff(nowMillis, targetMillis);
        int years = parts[0];
        int months = parts[1];
        int days = parts[2];
        int hours = parts[3];
        int minutes = parts[4];
        int seconds = parts[5];

        if (years > 0) {
            return String.format(Locale.getDefault(), "%d г. %d мес. %d дн. %d ч.", years, months, days, hours);
        }
        if (months > 0) {
            return String.format(Locale.getDefault(), "%d мес. %d дн. %d ч. %d мин.", months, days, hours, minutes);
        }
        if (days > 0) {
            return String.format(Locale.getDefault(), "%d дн. %d ч. %d мин. %d сек.", days, hours, minutes, seconds);
        }
        if (hours > 0) {
            return String.format(Locale.getDefault(), "%d ч. %d мин. %d сек.", hours, minutes, seconds);
        }
        if (minutes > 0) {
            return String.format(Locale.getDefault(), "%d мин. %d сек.", minutes, seconds);
        }
        return String.format(Locale.getDefault(), "%d сек.", Math.max(1, seconds));
    }

    /**
     * Вычисляет календарную разницу [years, months, days, hours, minutes, seconds]
     * между двумя моментами времени.
     */
    public static int[] calculateCalendarDiff(long fromMillis, long toMillis) {
        int[] result = new int[]{0, 0, 0, 0, 0, 0};
        if (toMillis <= fromMillis) {
            return result;
        }

        Calendar cursor = Calendar.getInstance();
        cursor.setTimeInMillis(fromMillis);

        int years = 0;
        while (true) {
            Calendar next = (Calendar) cursor.clone();
            next.add(Calendar.YEAR, 1);
            if (next.getTimeInMillis() <= toMillis) {
                years++;
                cursor = next;
            } else {
                break;
            }
        }

        int months = 0;
        while (true) {
            Calendar next = (Calendar) cursor.clone();
            next.add(Calendar.MONTH, 1);
            if (next.getTimeInMillis() <= toMillis) {
                months++;
                cursor = next;
            } else {
                break;
            }
        }

        long remMs = Math.max(0, toMillis - cursor.getTimeInMillis());
        long totalSec = remMs / 1000L;
        int days = (int) (totalSec / 86400L);
        totalSec %= 86400L;
        int hours = (int) (totalSec / 3600L);
        totalSec %= 3600L;
        int minutes = (int) (totalSec / 60L);
        int seconds = (int) (totalSec % 60L);

        result[0] = years;
        result[1] = months;
        result[2] = days;
        result[3] = hours;
        result[4] = minutes;
        result[5] = seconds;
        return result;
    }

    public void advanceAfterComplete(long nowMillis) {
        if (recurrenceValue == RECURRENCE_NONE) {
            isCompleted = true;
            isEnabled = false;
            touch();
            return;
        }

        Calendar next = Calendar.getInstance();
        next.setTimeInMillis(getTriggerTimeMillis());

        switch (recurrenceValue) {
            case RECURRENCE_EVERY_MINUTES:
                int step = Math.max(1, intervalMinutes);
                next.setTimeInMillis(nowMillis);
                next.add(Calendar.MINUTE, step);
                break;

            case RECURRENCE_DAILY:
                do {
                    next.add(Calendar.DAY_OF_MONTH, 1);
                } while (next.getTimeInMillis() <= nowMillis);
                break;

            case RECURRENCE_WEEKDAYS:
                do {
                    next.add(Calendar.DAY_OF_MONTH, 1);
                    int dow = next.get(Calendar.DAY_OF_WEEK);
                    while (dow == Calendar.SATURDAY || dow == Calendar.SUNDAY) {
                        next.add(Calendar.DAY_OF_MONTH, 1);
                        dow = next.get(Calendar.DAY_OF_WEEK);
                    }
                } while (next.getTimeInMillis() <= nowMillis);
                break;

            case RECURRENCE_WEEKLY:
                do {
                    next.add(Calendar.DAY_OF_MONTH, 7);
                } while (next.getTimeInMillis() <= nowMillis);
                break;
        }

        setTriggerTimeMillis(next.getTimeInMillis());
        isCompleted = false;
        isEnabled = true;
        touch();
    }

    public void snoozeByOffset(int years, int months, int days, int hours, int minutes, int seconds) {
        Calendar cal = Calendar.getInstance();
        cal.add(Calendar.YEAR, Math.max(0, years));
        cal.add(Calendar.MONTH, Math.max(0, months));
        cal.add(Calendar.DAY_OF_MONTH, Math.max(0, days));
        cal.add(Calendar.HOUR_OF_DAY, Math.max(0, hours));
        cal.add(Calendar.MINUTE, Math.max(0, minutes));
        cal.add(Calendar.SECOND, Math.max(0, seconds));

        if (cal.getTimeInMillis() <= System.currentTimeMillis()) {
            cal.setTimeInMillis(System.currentTimeMillis() + 10000L);
        }

        setTriggerTimeMillis(cal.getTimeInMillis());
        isCompleted = false;
        isEnabled = true;
        touch();
    }

    public JSONObject toJson() throws JSONException {
        JSONObject obj = new JSONObject();
        obj.put("Id", id != null ? id : "");
        obj.put("Title", title != null ? title : "");
        obj.put("Notes", notes != null ? notes : "");
        obj.put("TriggerTimeIso", triggerTimeIso != null ? triggerTimeIso : "");
        obj.put("RecurrenceValue", recurrenceValue);
        obj.put("IntervalMinutes", intervalMinutes);
        obj.put("PriorityValue", priorityValue);
        obj.put("IsEnabled", isEnabled);
        obj.put("IsCompleted", isCompleted);
        obj.put("PlaySound", playSound);
        obj.put("UpdatedAtUnixMs", updatedAtUnixMs);
        obj.put("IsDeleted", isDeleted);
        return obj;
    }

    public static ReminderItem fromJson(JSONObject obj) {
        if (obj == null) return null;
        ReminderItem r = new ReminderItem();
        r.id = obj.optString("Id", r.id);
        r.title = obj.optString("Title", "");
        r.notes = obj.optString("Notes", "");
        r.triggerTimeIso = obj.optString("TriggerTimeIso", r.triggerTimeIso);
        r.recurrenceValue = obj.optInt("RecurrenceValue", RECURRENCE_NONE);
        r.intervalMinutes = obj.optInt("IntervalMinutes", 30);
        r.priorityValue = obj.optInt("PriorityValue", PRIORITY_NORMAL);
        r.isEnabled = obj.optBoolean("IsEnabled", true);
        r.isCompleted = obj.optBoolean("IsCompleted", false);
        r.playSound = obj.optBoolean("PlaySound", true);
        r.updatedAtUnixMs = obj.optLong("UpdatedAtUnixMs", 0L);
        r.isDeleted = obj.optBoolean("IsDeleted", false);
        return r;
    }
}
