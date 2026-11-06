using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using SP_CopyFromRSN.Models;

namespace SP_CopyFromRSN.Revit
{
    /// <summary>Запуск копирования в главном потоке Revit (расписание / UI).</summary>
    public sealed class CopyFromRsnExternalEventHandler : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private List<CopyFromRsnProfile> _pendingProfiles = new List<CopyFromRsnProfile>();

        public void RequestRun(CopyFromRsnProfile profile) =>
            RequestRun(new[] { profile });

        public void RequestRun(IEnumerable<CopyFromRsnProfile> profiles)
        {
            lock (_sync)
            {
                _pendingProfiles = profiles
                    .Where(p => p != null)
                    .Select(p => p.Clone())
                    .ToList();
            }
        }

        public void Execute(UIApplication app)
        {
            List<CopyFromRsnProfile> profiles;
            lock (_sync)
            {
                profiles = _pendingProfiles;
                _pendingProfiles = new List<CopyFromRsnProfile>();
            }

            if (profiles.Count == 0)
                return;

            try
            {
                // Пакет из одного или нескольких профилей — в одном вызове ExternalEvent
                CopyFromRsnRuntime.ExecuteCopyBatch(app, profiles);
            }
            catch
            {
                // Сообщение уже передано через RaiseRunCompleted
            }
        }

        public string GetName() => "SP CopyFromRSN";
    }
}
