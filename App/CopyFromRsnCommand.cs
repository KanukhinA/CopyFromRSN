using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SP_CopyFromRSN.Revit;
using SP_CopyFromRSN.UI;

namespace SP_CopyFromRSN.App
{
    [Transaction(TransactionMode.Manual)]
    public class CopyFromRsnCommand : IExternalCommand
    {
        private static CopyFromRsnView? _openView;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                CopyFromRsnRuntime.UiApplication = commandData.Application;
                CopyFromRsnRuntime.RevitVersion =
                    commandData.Application.Application.VersionNumber;
                CopyFromRsnRuntime.ReloadActiveProfile();
                // Дублируем инициализацию: команда может идти из другого load-context DLL
                CopyFromRsnRuntime.EnsureExternalEvent();

                // Уже открыто — выводим на передний план (копирование идёт через ExternalEvent)
                if (_openView != null)
                {
                    if (_openView.WindowState == System.Windows.WindowState.Minimized)
                        _openView.WindowState = System.Windows.WindowState.Normal;
                    _openView.Activate();
                    return Result.Succeeded;
                }

                var viewModel = new CopyFromRsnViewModel(commandData.Application);
                var view = new CopyFromRsnView(viewModel);
                viewModel.AttachUiDispatcher(view.Dispatcher);

                var helper = new WindowInteropHelper(view)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                _openView = view;
                view.Closed += (_, __) =>
                {
                    if (ReferenceEquals(_openView, view))
                        _openView = null;
                };

                // Безмодальное окно: ExternalEvent и таймер расписания работают, пока окно открыто
                view.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
