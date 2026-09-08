using System;
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

        public static event Action<string>? StatusChanged;

        public static event Action<string, bool>? RunCompleted;

        public static void RaiseStatus(string message) => StatusChanged?.Invoke(message);

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

        public static void RaiseCopy(CopyFromRsnProfile profile)
        {
            lock (ExternalEventSync)
            {
                if (_handler == null || _externalEvent == null)
                    throw new InvalidOperationException(
                        "ExternalEvent не инициализирован. Закройте Revit, пересоберите add-in и откройте окно заново через кнопку на ленте.");

                if (IsRunning)
                    throw new InvalidOperationException("Копирование уже выполняется.");

                _handler.RequestRun(profile);
                _externalEvent.Raise();
            }
        }

        /// <summary>
        /// Выполняет копирование на текущем потоке Revit.
        /// Нужно для запуска из модального окна (ExternalEvent там не срабатывает).
        /// </summary>
        public static string ExecuteCopy(UIApplication app, CopyFromRsnProfile profile)
        {
            if (IsRunning)
                throw new InvalidOperationException("Копирование уже выполняется.");

            IsRunning = true;
            try
            {
                RaiseStatus("Выполняется копирование моделей...");

                var service = new CopyFromRsnService(
                    app.Application,
                    profile,
                    (message, isError) =>
                    {
                        CopyFromRsnService.LogStatic(profile.LocalDownloadFolder, message, isError);
                        RaiseStatus(message);
                    });

                string result;
                if (profile.SuppressOpenWarnings)
                {
                    using (CopyFromRsnOpenWarningSuppression.Begin())
                        result = service.Execute();
                }
                else
                {
                    result = service.Execute();
                }

                RaiseRunCompleted(result, false);
                return result;
            }
            catch (Exception exception)
            {
                string error = "Ошибка: " + exception.Message;
                try
                {
                    CopyFromRsnService.LogStatic(
                        profile.LocalDownloadFolder, exception.ToString(), true);
                }
                catch
                {
                    // Журнал не должен скрывать исходную ошибку
                }

                RaiseRunCompleted(error, true);
                throw new InvalidOperationException(error, exception);
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
