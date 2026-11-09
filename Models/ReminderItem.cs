using System;
using System.Runtime.Serialization;

namespace StreloReminder.Models
{
    public enum RecurrenceType
    {
        None = 0,
        EveryMinutes = 1,
        Daily = 2,
        Weekdays = 3,
        Weekly = 4
    }

    public enum ReminderPriority
    {
        Normal = 0,
        High = 1
    }

    [DataContract]
    public class ReminderItem
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [DataMember(Order = 1)]
        public string Id { get; set; }

        [DataMember(Order = 2)]
        public string Title { get; set; }

        [DataMember(Order = 3)]
        public string Notes { get; set; }

        /// <summary>
        /// Храним дату/время в строковом ISO-формате для 100% совместимости сериализации
        /// между .NET Framework, .NET 8 и мобильным приложением Android.
        /// </summary>
        [DataMember(Order = 4)]
        public string TriggerTimeIso { get; set; }

        [DataMember(Order = 5)]
        public int RecurrenceValue { get; set; }

        [DataMember(Order = 6)]
        public int IntervalMinutes { get; set; }

        [DataMember(Order = 7)]
        public int PriorityValue { get; set; }

        [DataMember(Order = 8)]
        public bool IsEnabled { get; set; }

        [DataMember(Order = 9)]
        public bool IsCompleted { get; set; }

        [DataMember(Order = 10)]
        public bool PlaySound { get; set; }

        /// <summary>
        /// Время последнего изменения записи (Unix timestamp в миллисекундах UTC)
        /// для двусторонней синхронизации между ПК и Android.
        /// </summary>
        [DataMember(Order = 11)]
        public long UpdatedAtUnixMs { get; set; }

        /// <summary>
        /// Флаг мягкого удаления (tombstone) для корректной синхронизации удалений между ПК и телефоном.
        /// </summary>
        [DataMember(Order = 12)]
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Флаг того, что прямо сейчас по этому напоминанию открыто всплывающее окно.
        /// Не сериализуется в файл.
        /// </summary>
        [IgnoreDataMember]
        public bool IsPopupOpen { get; set; }

        public ReminderItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = string.Empty;
            Notes = string.Empty;
            TriggerTime = DateTime.Now.AddMinutes(15);
            Recurrence = RecurrenceType.None;
            IntervalMinutes = 30;
            Priority = ReminderPriority.Normal;
            IsEnabled = true;
            IsCompleted = false;
            PlaySound = true;
            UpdatedAtUnixMs = GetCurrentUnixTimeMs();
            IsDeleted = false;
            IsPopupOpen = false;
        }

        public static long GetCurrentUnixTimeMs()
        {
            return (long)(DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
        }

        public void Touch()
        {
            UpdatedAtUnixMs = GetCurrentUnixTimeMs();
        }

        public ReminderItem Clone()
        {
            return new ReminderItem
            {
                Id = this.Id,
                Title = this.Title,
                Notes = this.Notes,
                TriggerTimeIso = this.TriggerTimeIso,
                RecurrenceValue = this.RecurrenceValue,
                IntervalMinutes = this.IntervalMinutes,
                PriorityValue = this.PriorityValue,
                IsEnabled = this.IsEnabled,
                IsCompleted = this.IsCompleted,
                PlaySound = this.PlaySound,
                UpdatedAtUnixMs = this.UpdatedAtUnixMs,
                IsDeleted = this.IsDeleted,
                IsPopupOpen = this.IsPopupOpen
            };
        }

        [IgnoreDataMember]
        public DateTime TriggerTime
        {
            get
            {
                DateTime dt;
                if (!string.IsNullOrEmpty(TriggerTimeIso) && DateTime.TryParse(TriggerTimeIso, out dt))
                {
                    return dt;
                }
                return DateTime.Now;
            }
            set
            {
                TriggerTimeIso = value.ToString("yyyy-MM-ddTHH:mm:ss");
            }
        }

        [IgnoreDataMember]
        public RecurrenceType Recurrence
        {
            get { return (RecurrenceType)RecurrenceValue; }
            set { RecurrenceValue = (int)value; }
        }

        [IgnoreDataMember]
        public ReminderPriority Priority
        {
            get { return (ReminderPriority)PriorityValue; }
            set { PriorityValue = (int)value; }
        }

        public string GetRecurrenceDisplayText()
        {
            switch (Recurrence)
            {
                case RecurrenceType.None:
                    return "Один раз";
                case RecurrenceType.EveryMinutes:
                    if (IntervalMinutes >= 60 && IntervalMinutes % 60 == 0)
                        return string.Format("Каждые {0} ч.", IntervalMinutes / 60);
                    return string.Format("Каждые {0} мин.", IntervalMinutes);
                case RecurrenceType.Daily:
                    return "Ежедневно";
                case RecurrenceType.Weekdays:
                    return "По будням (Пн–Пт)";
                case RecurrenceType.Weekly:
                    return "Еженедельно";
                default:
                    return "Один раз";
            }
        }

        public string GetPriorityDisplayText()
        {
            return Priority == ReminderPriority.High ? "Важное (!)" : "Обычное";
        }

        public string GetRemainingTimeText(DateTime now)
        {
            if (IsCompleted)
                return "Выполнено";
            if (!IsEnabled)
                return "Отключено";
            if (IsPopupOpen)
                return "Сработало сейчас!";

            TimeSpan diff = TriggerTime - now;
            if (diff.TotalSeconds <= 0)
                return "Срабатывает...";

            int years;
            int months;
            int days;
            int hours;
            int minutes;
            int seconds;
            CalculateCalendarDiff(now, TriggerTime, out years, out months, out days, out hours, out minutes, out seconds);

            if (years > 0)
            {
                return string.Format("{0} г. {1} мес. {2} дн. {3} ч.", years, months, days, hours);
            }
            if (months > 0)
            {
                return string.Format("{0} мес. {1} дн. {2} ч. {3} мин.", months, days, hours, minutes);
            }
            if (days > 0)
            {
                return string.Format("{0} дн. {1} ч. {2} мин. {3} сек.", days, hours, minutes, seconds);
            }
            if (hours > 0)
            {
                return string.Format("{0} ч. {1} мин. {2} сек.", hours, minutes, seconds);
            }
            if (minutes > 0)
            {
                return string.Format("{0} мин. {1} сек.", minutes, seconds);
            }
            return string.Format("{0} сек.", Math.Max(1, seconds));
        }

        public static void CalculateCalendarDiff(
            DateTime from,
            DateTime to,
            out int years,
            out int months,
            out int days,
            out int hours,
            out int minutes,
            out int seconds)
        {
            years = 0;
            months = 0;
            days = 0;
            hours = 0;
            minutes = 0;
            seconds = 0;

            if (to <= from)
                return;

            DateTime cursor = from;

            while (cursor.AddYears(years + 1) <= to)
            {
                years++;
            }
            cursor = cursor.AddYears(years);

            while (cursor.AddMonths(months + 1) <= to)
            {
                months++;
            }
            cursor = cursor.AddMonths(months);

            TimeSpan rem = to - cursor;
            days = rem.Days;
            hours = rem.Hours;
            minutes = rem.Minutes;
            seconds = rem.Seconds;
        }

        /// <summary>
        /// Вычисляет следующее время срабатывания для повторяющихся напоминаний,
        /// либо помечает однократное напоминание выполненным.
        /// </summary>
        public void AdvanceAfterComplete(DateTime now)
        {
            if (Recurrence == RecurrenceType.None)
            {
                IsCompleted = true;
                IsEnabled = false;
                Touch();
                return;
            }

            DateTime next = TriggerTime;
            switch (Recurrence)
            {
                case RecurrenceType.EveryMinutes:
                    int step = Math.Max(1, IntervalMinutes);
                    next = now.AddMinutes(step);
                    break;

                case RecurrenceType.Daily:
                    do
                    {
                        next = next.AddDays(1);
                    } while (next <= now);
                    break;

                case RecurrenceType.Weekdays:
                    do
                    {
                        next = next.AddDays(1);
                    } while (next <= now || next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday);
                    break;

                case RecurrenceType.Weekly:
                    do
                    {
                        next = next.AddDays(7);
                    } while (next <= now);
                    break;
            }

            TriggerTime = next;
            IsCompleted = false;
            IsEnabled = true;
            Touch();
        }

        /// <summary>
        /// Откладывает напоминание на указанное число минут от текущего момента.
        /// </summary>
        public void Snooze(int minutes)
        {
            int mins = Math.Max(1, minutes);
            TriggerTime = DateTime.Now.AddMinutes(mins);
            IsCompleted = false;
            IsEnabled = true;
            Touch();
        }

        /// <summary>
        /// Откладывает напоминание на заданное смещение (годы, месяцы, дни, часы, минуты, секунды) от текущего момента.
        /// </summary>
        public void SnoozeByOffset(int years, int months, int days, int hours, int minutes, int seconds)
        {
            DateTime target = DateTime.Now
                .AddYears(Math.Max(0, years))
                .AddMonths(Math.Max(0, months))
                .AddDays(Math.Max(0, days))
                .AddHours(Math.Max(0, hours))
                .AddMinutes(Math.Max(0, minutes))
                .AddSeconds(Math.Max(0, seconds));

            if (target <= DateTime.Now)
            {
                target = DateTime.Now.AddSeconds(10);
            }

            TriggerTime = target;
            IsCompleted = false;
            IsEnabled = true;
            Touch();
        }
    }
}
