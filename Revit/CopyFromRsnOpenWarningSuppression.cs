using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace SP_CopyFromRSN.Revit
{
    /// <summary>
    /// Подавление диалогов и предупреждений при пакетном открытии моделей.
    /// DialogBoxShowing — TaskDialog / MessageBox;
    /// FailuresProcessing — предупреждения Revit;
    /// Win32 — запасной клик «Закрыть» для «Потеря элементов при импорте»
    /// (этот диалог часто не слушается OverrideResult).
    /// </summary>
    public static class CopyFromRsnOpenWarningSuppression
    {
        private static int _suppressionDepth;
        private static CancellationTokenSource? _win32Cts;
        private static Thread? _win32Thread;

        public static bool IsActive => _suppressionDepth > 0;

        public static IDisposable Begin() => new SuppressionScope();

        public static void Attach(UIControlledApplication application)
        {
            application.DialogBoxShowing += OnDialogBoxShowing;
            application.ControlledApplication.FailuresProcessing += OnFailuresProcessing;
        }

        public static void Detach(UIControlledApplication application)
        {
            application.DialogBoxShowing -= OnDialogBoxShowing;
            application.ControlledApplication.FailuresProcessing -= OnFailuresProcessing;
            StopWin32Watcher();
        }

        private static void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            if (!IsActive)
                return;

            if (e is TaskDialogShowingEventArgs taskDialog)
            {
                int? overrideResult = GetTaskDialogOverride(taskDialog.DialogId);
                if (!overrideResult.HasValue &&
                    IsImportLostElementsText(taskDialog.DialogId, taskDialog.Message))
                {
                    // Одна кнопка «Закрыть»
                    overrideResult = (int)TaskDialogResult.Close;
                }

                if (overrideResult.HasValue)
                    taskDialog.OverrideResult(overrideResult.Value);
                return;
            }

            if (e is MessageBoxShowingEventArgs messageBox)
            {
                if (IsImportLostElementsText(e.DialogId, messageBox.Message))
                {
                    // IDOK / Close для однокнопочного MessageBox
                    messageBox.OverrideResult(1);
                    return;
                }
            }

            // Диалог предупреждений документа при открытии
            if (string.Equals(e.DialogId, "Dialog_Revit_DocWarnDialog", StringComparison.Ordinal))
            {
                e.OverrideResult((int)TaskDialogResult.Ok);
                return;
            }

            if (IsImportLostElementsText(e.DialogId, null))
                e.OverrideResult(1);
        }

        private static int? GetTaskDialogOverride(string dialogId)
        {
            switch (dialogId)
            {
                case "TaskDialog_Missing_Third_Party_Updaters":
                case "TaskDialog_Missing_Third_Party_Updater":
                    return (int)TaskDialogResult.CommandLink2;

                case "TaskDialog_Local_Changes_Not_Synchronized_With_Central":
                    return (int)TaskDialogResult.CommandLink2;

                // Потеря элементов CAD/ActiveX при открытии (если Revit отдаёт TaskDialog)
                case "TaskDialog_Elements_Lost_On_Import":
                case "TaskDialog_Elements_Lost_on_Import":
                    return (int)TaskDialogResult.Close;

                default:
                    return null;
            }
        }

        /// <summary>Распознаём диалог потери элементов при импорте CAD (RU/EN).</summary>
        private static bool IsImportLostElementsText(string? dialogId, string? message)
        {
            if (!string.IsNullOrWhiteSpace(dialogId))
            {
                string id = dialogId!;
                if (id.IndexOf("Elements_Lost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Lost_On_Import", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Lost_on_Import", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (string.IsNullOrWhiteSpace(message))
                return false;

            string text = message!;
            bool hasActiveX = text.IndexOf("ActiveX", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasProxy = text.IndexOf("PROXYGRAPHICS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            text.IndexOf("PROXYGRAPICS", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasRu = text.IndexOf("утеряны при импорте", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("Потеря элементов", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("не могут быть импортированы", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasEn = text.IndexOf("lost during import", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("Elements Lost on Import", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("proprietary components", StringComparison.OrdinalIgnoreCase) >= 0;

            return hasActiveX || hasProxy || hasRu || hasEn;
        }

        private static void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            if (!IsActive)
                return;

            FailuresAccessor accessor = e.GetFailuresAccessor();

            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(failure);
            }

            e.SetProcessingResult(FailureProcessingResult.Continue);
        }

        private static void StartWin32Watcher()
        {
            if (_win32Thread != null && _win32Thread.IsAlive)
                return;

            _win32Cts = new CancellationTokenSource();
            CancellationToken token = _win32Cts.Token;
            _win32Thread = new Thread(() => Win32WatcherLoop(token))
            {
                IsBackground = true,
                Name = "SP_CopyFromRSN_DialogDismiss"
            };
            _win32Thread.Start();
        }

        private static void StopWin32Watcher()
        {
            try
            {
                _win32Cts?.Cancel();
            }
            catch
            {
                // ignore
            }

            _win32Cts = null;
            _win32Thread = null;
        }

        private static void Win32WatcherLoop(CancellationToken token)
        {
            int revitPid = Process.GetCurrentProcess().Id;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsActive)
                        Win32DialogDismisser.TryDismissImportLostDialogs(revitPid);
                }
                catch
                {
                    // Не роняем фоновый поток из‑за единичного сбоя EnumWindows
                }

                try
                {
                    Thread.Sleep(300);
                }
                catch
                {
                    break;
                }
            }
        }

        private sealed class SuppressionScope : IDisposable
        {
            public SuppressionScope()
            {
                if (Interlocked.Increment(ref _suppressionDepth) == 1)
                    StartWin32Watcher();
            }

            public void Dispose()
            {
                if (Interlocked.Decrement(ref _suppressionDepth) <= 0)
                    StopWin32Watcher();
            }
        }

        /// <summary>Win32: найти окно по заголовку и нажать «Закрыть» / Close.</summary>
        private static class Win32DialogDismisser
        {
            private const uint BmClick = 0x00F5;

            private static readonly string[] TitleMarkers =
            {
                "Потеря элементов при импорте",
                "Elements Lost on Import",
                "Elements lost on Import"
            };

            private static readonly string[] MessageMarkers =
            {
                "ActiveX",
                "PROXYGRAPHICS",
                "PROXYGRAPICS",
                "утеряны при импорте",
                "lost during import",
                "proprietary components",
                "не могут быть импортированы"
            };

            private static readonly string[] CloseButtonTexts =
            {
                "Закрыть",
                "Close",
                "OK",
                "ОК"
            };

            private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

            [DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

            public static void TryDismissImportLostDialogs(int revitProcessId)
            {
                var dialogs = new List<IntPtr>();
                EnumWindows((hWnd, _) =>
                {
                    if (!IsWindowVisible(hWnd))
                        return true;

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if ((int)pid != revitProcessId)
                        return true;

                    string title = GetText(hWnd);
                    if (IsMatchingTitle(title) || HasMatchingStaticText(hWnd))
                        dialogs.Add(hWnd);

                    return true;
                }, IntPtr.Zero);

                foreach (IntPtr dialog in dialogs)
                    ClickCloseButton(dialog);
            }

            private static bool IsMatchingTitle(string title)
            {
                if (string.IsNullOrWhiteSpace(title))
                    return false;

                foreach (string marker in TitleMarkers)
                {
                    if (title.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                return false;
            }

            private static bool HasMatchingStaticText(IntPtr dialog)
            {
                bool found = false;
                EnumChildWindows(dialog, (child, _) =>
                {
                    string className = GetClass(child);
                    // Static / DirectUI текст внутри окна Revit
                    if (className.IndexOf("Static", StringComparison.OrdinalIgnoreCase) < 0 &&
                        className.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !string.Equals(className, "DirectUIHWND", StringComparison.OrdinalIgnoreCase))
                        return true;

                    string text = GetText(child);
                    if (string.IsNullOrWhiteSpace(text))
                        return true;

                    foreach (string marker in MessageMarkers)
                    {
                        if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                            return false;
                        }
                    }

                    return true;
                }, IntPtr.Zero);

                return found;
            }

            private static void ClickCloseButton(IntPtr dialog)
            {
                var buttons = new List<(IntPtr Hwnd, string Text)>();
                EnumChildWindows(dialog, (child, _) =>
                {
                    string className = GetClass(child);
                    if (className.IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0)
                        return true;

                    string text = NormalizeButtonText(GetText(child));
                    if (!string.IsNullOrWhiteSpace(text))
                        buttons.Add((child, text));
                    return true;
                }, IntPtr.Zero);

                foreach (string wanted in CloseButtonTexts)
                {
                    foreach (var button in buttons)
                    {
                        if (string.Equals(button.Text, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            SendMessage(button.Hwnd, BmClick, IntPtr.Zero, IntPtr.Zero);
                            return;
                        }
                    }
                }

                // Если кнопка одна — жмём её
                if (buttons.Count == 1)
                    SendMessage(buttons[0].Hwnd, BmClick, IntPtr.Zero, IntPtr.Zero);
            }

            private static string NormalizeButtonText(string text) =>
                (text ?? string.Empty).Replace("&", string.Empty).Trim();

            private static string GetText(IntPtr hWnd)
            {
                var sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }

            private static string GetClass(IntPtr hWnd)
            {
                var sb = new StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
        }
    }
}
