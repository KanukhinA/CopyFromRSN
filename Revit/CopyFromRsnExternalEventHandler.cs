using System;
using Autodesk.Revit.UI;
using SP_CopyFromRSN.Models;

namespace SP_CopyFromRSN.Revit
{
    /// <summary>Запуск копирования в главном потоке Revit (расписание / UI).</summary>
    public sealed class CopyFromRsnExternalEventHandler : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private CopyFromRsnProfile? _pendingProfile;

        public void RequestRun(CopyFromRsnProfile profile)
        {
            lock (_sync)
            {
                _pendingProfile = profile.Clone();
            }
        }

        public void Execute(UIApplication app)
        {
            CopyFromRsnProfile? profile;
            lock (_sync)
            {
                profile = _pendingProfile;
                _pendingProfile = null;
            }

            if (profile == null)
                return;

            try
            {
                // Результат/ошибка уходят в StatusChanged / RunCompleted — без TaskDialog
                CopyFromRsnRuntime.ExecuteCopy(app, profile);
            }
            catch
            {
                // Сообщение уже передано через RaiseRunCompleted
            }
        }

        public string GetName() => "SP CopyFromRSN";
    }
}
