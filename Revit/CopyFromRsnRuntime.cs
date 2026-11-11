using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using SP_CopyFromRSN.Models;
using SP_CopyFromRSN.Services;

namespace SP_CopyFromRSN.Revit
{
    /// <summary>Общее состояние add-in между UI, Idling и ExternalEvent.</summary>
    public static class CopyFromRsnRuntime
    {
        private static readonly object ExternalEventSync = new object();
        private static CopyFromRsnExternalEventHandler? _handler;
        private static ExternalEvent? _externalEvent;

        public static UIApplication? UiApplication { get; set; }

        public static string RevitVersion { get; set; } = "2023";

        public static CopyFromRsnProfile ActiveProfile { get; set; } =
            new CopyFromRsnProfile();

        public static bool IsRunning { get; set; }

        /// <summary>Прогресс/сообщения в консоль UI. Второй аргумент — ошибка.</summary>
        public static event Action<string, bool>? StatusChanged;

        public static event Action<string, bool>? RunCompleted;

        public static void RaiseStatus(string message, bool isError = false) =>
            StatusChanged?.Invoke(message, isError);

        public static void RaiseRunCompleted(string result, bool isError) =>
            RunCompleted?.Invoke(result, isError);

        public static void ReloadActiveProfile()
        {
            ActiveProfile = CopyFromRsnProfileStorage.LoadActiveProfile(RevitVersion);
        }

        /// <summary>
        /// Создаёт ExternalEvent при старте add-in и при открытии команды
        /// (на случай, если статика OnStartup недоступна в текущей загрузке DLL).
        /// Вызывать только из контекста Revit API (OnStartup / IExternalCommand).
        /// </summary>
        public static void EnsureExternalEvent()
        {
            lock (ExternalEventSync)
            {
                if (_externalEvent != null && _handler != null)
                    return;

                _handler = new CopyFromRsnExternalEventHandler();
                _externalEvent = ExternalEvent.Create(_handler);
            }
        }

        public static void RaiseCopy(CopyFromRsnProfile profile) =>
            RaiseCopy(new[] { profile });

        public static void RaiseCopy(IReadOnlyList<CopyFromRsnProfile> profiles)
        {
            if (profiles == null || profiles.Count == 0)
                throw new InvalidOperationException("Нет конфигураций для запуска.");

            lock (ExternalEventSync)
            {
                if (_handler == null || _externalEvent == null)
                    throw new InvalidOperationException(
                        "ExternalEvent не инициализирован. Закройте Revit, пересоберите add-in и откройте окно заново через кнопку на ленте.");

                if (IsRunning)
                    throw new InvalidOperationException("Копирование уже выполняется.");

                _handler.RequestRun(profiles);
                _externalEvent.Raise();
            }
        }

        /// <summary>
        /// Выполняет копирование на текущем потоке Revit.
        /// Нужно для запуска из модального окна (ExternalEvent там не срабатывает).
        /// </summary>
        public static string ExecuteCopy(UIApplication app, CopyFromRsnProfile profile) =>
            ExecuteCopyBatch(app, new[] { profile });

        /// <summary>
        /// Последовательно выполняет несколько профилей в одном контексте API.
        /// Промежуточные статусы идут в StatusChanged; итог — один RunCompleted.
        /// </summary>
        public static string ExecuteCopyBatch(UIApplication app, IReadOnlyList<CopyFromRsnProfile> profiles)
        {
            if (profiles == null || profiles.Count == 0)
                throw new InvalidOperationException("Нет конфигураций для запуска.");

            if (IsRunning)
                throw new InvalidOperationException("Копирование уже выполняется.");

            IsRunning = true;
            var summaries = new List<string>();
            bool anyError = false;

            try
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    CopyFromRsnProfile profile = profiles[i];
                    string label = string.IsNullOrWhiteSpace(profile.Name)
                        ? ("#" + (i + 1))
                        : profile.Name;

                    RaiseStatus(
                        "Конфигурация «" + label + "» (" + (i + 1) + "/" + profiles.Count + ")...");

                    try
                    {
                        string result = ExecuteCopyCore(app, profile);
                        summaries.Add("«" + label + "»: " + result);
                    }
                    catch (Exception exception)
                    {
                        anyError = true;
                        string error = exception.Message;
                        if (error.StartsWith("Ошибка: ", StringComparison.Ordinal))
                            error = error.Substring("Ошибка: ".Length);

                        summaries.Add("«" + label + "»: ошибка — " + error);
                        try
                        {
                            CopyFromRsnService.LogStatic(
                                profile.LocalDownloadFolder, exception.ToString(), true);
                        }
                        catch
                        {
                            // Журнал не должен скрывать исходную ошибку
                        }
                    }
                }

                string combined = profiles.Count == 1
                    ? summaries[0]
                    : "Пакетный запуск завершён (" + profiles.Count + "):\n" +
                      string.Join("\n", summaries);

                if (anyError && profiles.Count == 1)
                    combined = "Ошибка: " + summaries[0];
                else if (anyError)
                    combined = "Пакетный запуск с ошибками (" + profiles.Count + "):\n" +
                               string.Join("\n", summaries);

                RaiseRunCompleted(combined, anyError);
                if (anyError && profiles.Count == 1)
                    throw new InvalidOperationException(combined);

                return combined;
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static string ExecuteCopyCore(UIApplication app, CopyFromRsnProfile profile)
        {
            RaiseStatus("Выполняется копирование моделей («" + profile.Name + "»)...");

            var service = new CopyFromRsnService(
                app.Application,
                profile,
                (message, isError) =>
                {
                    CopyFromRsnService.LogStatic(profile.LocalDownloadFolder, message, isError);
                    RaiseStatus("[" + profile.Name + "] " + message, isError);
                });

            if (profile.SuppressOpenWarnings)
            {
                using (CopyFromRsnOpenWarningSuppression.Begin())
                    return service.Execute();
            }

            return service.Execute();
        }
    }
}
