using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SP_CopyFromRSN.Models;
using SP_CopyFromRSN.Revit;

namespace SP_CopyFromRSN.Services
{
    /// <summary>Расчёт слотов и запуск по расписанию (локальное время компьютера).</summary>
    public static class CopyFromRsnScheduleService
    {
        /// <summary>Интервал опроса таймера в открытом окне.</summary>
        public static readonly TimeSpan CheckIntervalValue = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Максимальное опоздание относительно слота: если таймер «проспал» дольше —
        /// слот считается пропущенным (чтобы не гонять утренний запуск вечером).
        /// </summary>
        public static readonly TimeSpan MaxLateness = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Слот, который пора выполнить: уже наступил по DateTime.Now,
        /// не старше MaxLateness и ещё не отмечен в state-файле.
        /// </summary>
        public static DateTime? GetDueScheduledRun(DateTime now, CopyFromRsnProfile profile)
        {
            if (!profile.ScheduleEnabled || profile.RunTimes.Count == 0 || profile.WeekDays.Count == 0)
                return null;

            var allowedDays = new HashSet<int>(profile.WeekDays);
            var parsedTimes = profile.RunTimes
                .Select(ParseTime)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .OrderBy(t => t)
                .ToList();

            if (parsedTimes.Count == 0)
                return null;

            DateTime? due = null;

            // Сегодня и вчера — вчера нужен для слотов около полуночи
            for (int dayOffset = 0; dayOffset <= 1; dayOffset++)
            {
                DateTime day = now.Date.AddDays(-dayOffset);
                if (!allowedDays.Contains((int)day.DayOfWeek))
                    continue;

                foreach (TimeSpan runTime in parsedTimes)
                {
                    DateTime candidate = day.Add(runTime);
                    // Ещё не наступил — или слишком давно (не догоняем старые слоты)
                    if (candidate > now || now - candidate > MaxLateness)
                        continue;

                    if (!due.HasValue || candidate > due.Value)
                        due = candidate;
                }
            }

            return due;
        }

        /// <summary>
        /// Если есть просроченный (в пределах MaxLateness) слот — ставит копирование в очередь.
        /// Время — локальные часы компьютера (DateTime.Now).
        /// </summary>
        /// <returns>Сообщение для статуса или null, если запускать нечего.</returns>
        public static string? TryRaiseDueRun(CopyFromRsnProfile profile, DateTime now) =>
            TryRaiseDueRun(profile, new[] { profile }, now);

        /// <summary>
        /// Как <see cref="TryRaiseDueRun(CopyFromRsnProfile, DateTime)"/>, но в очередь
        /// ставится пакет профилей (например все «Активные конфигурации»).
        /// Слот расписания и state-файл берутся у <paramref name="scheduleProfile"/>.
        /// </summary>
        public static string? TryRaiseDueRun(
            CopyFromRsnProfile scheduleProfile,
            IReadOnlyList<CopyFromRsnProfile> profilesToRun,
            DateTime now)
        {
            if (scheduleProfile == null)
                return null;

            if (CopyFromRsnRuntime.IsRunning)
                return null;

            DateTime? due = GetDueScheduledRun(now, scheduleProfile);
            if (!due.HasValue)
                return null;

            if (ReadLastScheduledRun(scheduleProfile) >= due.Value)
                return null;

            IReadOnlyList<CopyFromRsnProfile> batch =
                profilesToRun != null && profilesToRun.Count > 0
                    ? profilesToRun
                    : new[] { scheduleProfile };

            string message = "Запуск по расписанию " + due.Value.ToString("yyyy-MM-dd HH:mm") +
                             " (время компьютера).";
            if (batch.Count > 1)
                message += " Активных конфигураций: " + batch.Count + ".";

            try
            {
                CopyFromRsnRuntime.RaiseCopy(batch);
            }
            catch (InvalidOperationException)
            {
                // ExternalEvent ещё не готов — повторим на следующем тике таймера
                return null;
            }

            // State пишем только после успешной постановки в очередь, иначе слот «сгорает»
            WriteLastScheduledRun(scheduleProfile, due.Value);
            CopyFromRsnService.LogStatic(scheduleProfile.LocalDownloadFolder, message);
            return message;
        }

        /// <summary>Подсвечиваемый день в календаре: день недели включён в расписание.</summary>
        public static bool IsScheduledWeekDay(DateTime date, CopyFromRsnProfile profile) =>
            profile.WeekDays.Contains((int)date.DayOfWeek);

        public static TimeSpan? ParseTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string trimmed = value.Trim();

            // Строго ЧЧ:ММ (нужен ведущий ноль) — основной формат UI
            if (TimeSpan.TryParseExact(trimmed, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan result))
                return result;

            // Допускаем «9:05» и другие варианты
            if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out result))
                return result;

            if (TimeSpan.TryParse(trimmed, out result))
                return result;

            return null;
        }

        public static DateTime ReadLastScheduledRun(CopyFromRsnProfile profile)
        {
            try
            {
                string path = CopyFromRsnProfileStorage.GetStateFilePath(profile);
                if (!File.Exists(path))
                    return DateTime.MinValue;

                string text = File.ReadAllText(path).Trim();
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime result))
                    return result;

                return DateTime.TryParse(text, out result) ? result : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public static void WriteLastScheduledRun(CopyFromRsnProfile profile, DateTime value)
        {
            try
            {
                string path = CopyFromRsnProfileStorage.GetStateFilePath(profile);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? profile.LocalDownloadFolder);
                File.WriteAllText(path, value.ToString("O", CultureInfo.InvariantCulture));
            }
            catch
            {
                // Состояние расписания не критично для работы
            }
        }
    }
}
