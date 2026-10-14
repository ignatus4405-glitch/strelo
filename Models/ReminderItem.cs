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
        [DataMember(Order = 1)]
        public string Id { get; set; }

        [DataMember(Order = 2)]
        public string Title { get; set; }

        [DataMember(Order = 3)]
        public string Notes { get; set; }

        /// <summary>
        /// Храним дату/время в строковом ISO-формате для 100% совместимости сериализации
        /// между .NET Framework и .NET 6/8.
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
            IsPopupOpen = false;
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

            if (diff.TotalDays >= 1)
            {
                int days = (int)diff.TotalDays;
                return string.Format("{0} дн. {1} ч. {2} мин.", days, diff.Hours, diff.Minutes);
            }
            if (diff.TotalHours >= 1)
            {
                return string.Format("{0} ч. {1} мин. {2} сек.", (int)diff.TotalHours, diff.Minutes, diff.Seconds);
            }
            if (diff.TotalMinutes >= 1)
            {
                return string.Format("{0} мин. {1} сек.", (int)diff.TotalMinutes, diff.Seconds);
            }
            return string.Format("{0} сек.", Math.Max(1, (int)Math.Ceiling(diff.TotalSeconds)));
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
        }
    }
}
