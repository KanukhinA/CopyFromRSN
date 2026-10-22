using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using SP_CopyFromRSN.App;
using SP_CopyFromRSN.Models;
using SP_CopyFromRSN.Revit;
using SP_CopyFromRSN.Services;
using WinForms = System.Windows.Forms;

namespace SP_CopyFromRSN.UI
{
    /// <summary>ViewModel окна копирования моделей с RSN.</summary>
    public partial class CopyFromRsnViewModel : ObservableObject
    {
        private readonly UIApplication _uiApplication;
        private readonly string _revitVersion;
        private Dispatcher? _uiDispatcher;
        private DispatcherTimer? _scheduleTimer;

        private string _profileName = "По умолчанию";
        private string _sourceServer = string.Empty;
        private bool _useSourceRevitServer = true;
        private string _targetServer = string.Empty;
        private bool _useTargetRevitServer = true;
        private string _localDownloadFolder = @"C:\Temp\";
        private string _revitServerToolPath = string.Empty;
        private bool _repathLinks;
        private bool _suppressOpenWarnings = true;
        private bool _scheduleEnabled;
        private string _newRunTime = "08:00";
        private string _statusText = string.Empty;
        private bool _isBusy;
        private DateTime? _selectedCalendarDate = DateTime.Today;
        private ModelCopyEntry? _selectedModel;
        private LinkCopyEntry? _selectedLink;
        private string? _selectedRunTime;
        private string _loadedProfileName = string.Empty;
        private bool _suppressProfileSelectionLoad;

        /// <summary>Имя профиля, загруженного с диска (для переименования и выбора из списка).</summary>
        public string LoadedProfileName => _loadedProfileName;

        /// <summary>Пока обновляется список профилей — не реагировать на SelectionChanged ComboBox.</summary>
        public bool SuppressProfileSelectionLoad => _suppressProfileSelectionLoad;

        public ObservableCollection<string> ProfileNames { get; } = new ObservableCollection<string>();
        public ObservableCollection<ModelCopyEntry> Models { get; } = new ObservableCollection<ModelCopyEntry>();
        public ObservableCollection<LinkCopyEntry> Links { get; } = new ObservableCollection<LinkCopyEntry>();
        public ObservableCollection<string> RunTimes { get; } = new ObservableCollection<string>();
        public ObservableCollection<WeekDayOption> WeekDayOptions { get; } = new ObservableCollection<WeekDayOption>();

        public string ProfileName
        {
            get => _profileName;
            set => SetProperty(ref _profileName, value);
        }

        public string SourceServer
        {
            get => _sourceServer;
            set => SetProperty(ref _sourceServer, value);
        }

        /// <summary>Использовать исходный сервер RSN для относительных путей из таблицы моделей.</summary>
        public bool UseSourceRevitServer
        {
            get => _useSourceRevitServer;
            set => SetProperty(ref _useSourceRevitServer, value);
        }

        public string TargetServer
        {
            get => _targetServer;
            set => SetProperty(ref _targetServer, value);
        }

        /// <summary>Использовать целевой сервер RSN для относительных путей из таблицы моделей.</summary>
        public bool UseTargetRevitServer
        {
            get => _useTargetRevitServer;
            set => SetProperty(ref _useTargetRevitServer, value);
        }

        public string LocalDownloadFolder
        {
            get => _localDownloadFolder;
            set
            {
                if (!SetProperty(ref _localDownloadFolder, value))
                    return;

                // Сразу синхронизируем runtime-профиль — иначе до Save/перезапуска
                // копирование и логи могут идти в прежнюю папку.
                if (CopyFromRsnRuntime.ActiveProfile != null)
                    CopyFromRsnRuntime.ActiveProfile.LocalDownloadFolder = value ?? string.Empty;
            }
        }

        public string RevitServerToolPath
        {
            get => _revitServerToolPath;
            set => SetProperty(ref _revitServerToolPath, value);
        }

        public bool RepathLinks
        {
            get => _repathLinks;
            set => SetProperty(ref _repathLinks, value);
        }

        public bool SuppressOpenWarnings
        {
            get => _suppressOpenWarnings;
            set => SetProperty(ref _suppressOpenWarnings, value);
        }

        public bool ScheduleEnabled
        {
            get => _scheduleEnabled;
            set => SetProperty(ref _scheduleEnabled, value);
        }

        public string NewRunTime
        {
            get => _newRunTime;
            set => SetProperty(ref _newRunTime, value);
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (SetProperty(ref _statusText, value))
                    CopyStatusCommand?.NotifyCanExecuteChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    NotifyCommandsCanExecuteChanged();
            }
        }

        public DateTime? SelectedCalendarDate
        {
            get => _selectedCalendarDate;
            set => SetProperty(ref _selectedCalendarDate, value);
        }

        public ModelCopyEntry? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (SetProperty(ref _selectedModel, value))
                {
                    RemoveModelCommand?.NotifyCanExecuteChanged();
                    CopySelectedModelCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        public LinkCopyEntry? SelectedLink
        {
            get => _selectedLink;
            set
            {
                if (SetProperty(ref _selectedLink, value))
                {
                    RemoveLinkCommand?.NotifyCanExecuteChanged();
                    CopySelectedLinkCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        public string? SelectedRunTime
        {
            get => _selectedRunTime;
            set
            {
                if (SetProperty(ref _selectedRunTime, value))
                    RemoveRunTimeCommand?.NotifyCanExecuteChanged();
            }
        }

        public string CalendarHint
        {
            get
            {
                if (!SelectedCalendarDate.HasValue)
                    return string.Empty;

                bool active = CopyFromRsnScheduleService.IsScheduledWeekDay(
                    SelectedCalendarDate.Value, BuildProfileFromUi());

                return active
                    ? "Запуск в: " + string.Join(", ", RunTimes)
                    : "В этот день недели запуск не запланирован.";
            }
        }

        public RelayCommand SaveProfileCommand { get; }
        public RelayCommand NewProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand ImportCsvCommand { get; }
        public RelayCommand ExportCsvCommand { get; }
        public RelayCommand AddModelCommand { get; }
        public RelayCommand RemoveModelCommand { get; }
        public RelayCommand CopySelectedModelCommand { get; }
        public RelayCommand AddLinkCommand { get; }
        public RelayCommand RemoveLinkCommand { get; }
        public RelayCommand CopySelectedLinkCommand { get; }
        public RelayCommand AddRunTimeCommand { get; }
        public RelayCommand RemoveRunTimeCommand { get; }
        public RelayCommand RunNowCommand { get; }
        public RelayCommand BrowseFolderCommand { get; }
        public RelayCommand BrowseToolPathCommand { get; }
        public RelayCommand CopyStatusCommand { get; }

        public CopyFromRsnViewModel(UIApplication uiApplication)
        {
            _uiApplication = uiApplication;
            _revitVersion = uiApplication.Application.VersionNumber;

            SaveProfileCommand = new RelayCommand(SaveProfile, () => !IsBusy);
            NewProfileCommand = new RelayCommand(CreateNewProfile, () => !IsBusy);
            DeleteProfileCommand = new RelayCommand(DeleteProfile, () => !IsBusy);
            ImportCsvCommand = new RelayCommand(ImportCsv, () => !IsBusy);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => !IsBusy);
            AddModelCommand = new RelayCommand(() => Models.Add(new ModelCopyEntry()), () => !IsBusy);
            RemoveModelCommand = new RelayCommand(
                () => { if (SelectedModel != null) Models.Remove(SelectedModel); },
                () => !IsBusy && SelectedModel != null);
            CopySelectedModelCommand = new RelayCommand(CopySelectedModel, () => SelectedModel != null);
            AddLinkCommand = new RelayCommand(() => Links.Add(new LinkCopyEntry()), () => !IsBusy);
            RemoveLinkCommand = new RelayCommand(
                () => { if (SelectedLink != null) Links.Remove(SelectedLink); },
                () => !IsBusy && SelectedLink != null);
            CopySelectedLinkCommand = new RelayCommand(CopySelectedLink, () => SelectedLink != null);
            AddRunTimeCommand = new RelayCommand(AddRunTime, () => !IsBusy);
            RemoveRunTimeCommand = new RelayCommand(RemoveRunTime, () => !IsBusy && SelectedRunTime != null);
            RunNowCommand = new RelayCommand(RunNow, () => !IsBusy);
            BrowseFolderCommand = new RelayCommand(BrowseFolder, () => !IsBusy);
            BrowseToolPathCommand = new RelayCommand(BrowseToolPath, () => !IsBusy);
            CopyStatusCommand = new RelayCommand(CopyStatus, () => !string.IsNullOrWhiteSpace(StatusText));

            InitWeekDays();
            RefreshProfileList();
            LoadProfile(CopyFromRsnProfileStorage.LoadActiveProfile(_revitVersion).Name);

            CopyFromRsnRuntime.StatusChanged += OnRuntimeStatusChanged;
            CopyFromRsnRuntime.RunCompleted += OnRunCompleted;
        }

        public void Detach()
        {
            StopScheduleTimer();
            CopyFromRsnRuntime.StatusChanged -= OnRuntimeStatusChanged;
            CopyFromRsnRuntime.RunCompleted -= OnRunCompleted;
            _uiDispatcher = null;
        }

        public void AttachUiDispatcher(Dispatcher dispatcher)
        {
            _uiDispatcher = dispatcher;
            // Таймер расписания — на диспетчере окна (локальные часы ПК)
            StartScheduleTimer();
        }

        /// <summary>
        /// Опрос расписания по часам ПК (DateTime.Now), пока окно открыто.
        /// Revit Idling для этого не используем — он часто не вызывается вовремя.
        /// </summary>
        private void StartScheduleTimer()
        {
            StopScheduleTimer();

            if (_uiDispatcher == null)
                return;

            _scheduleTimer = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
            {
                Interval = CopyFromRsnScheduleService.CheckIntervalValue
            };
            _scheduleTimer.Tick += OnScheduleTimerTick;
            _scheduleTimer.Start();
        }

        private void StopScheduleTimer()
        {
            if (_scheduleTimer == null)
                return;

            _scheduleTimer.Stop();
            _scheduleTimer.Tick -= OnScheduleTimerTick;
            _scheduleTimer = null;
        }

        private void OnScheduleTimerTick(object? sender, EventArgs e)
        {
            // Расписание только при включённом флаге и когда копирование не идёт
            if (!ScheduleEnabled || IsBusy || CopyFromRsnRuntime.IsRunning)
                return;

            try
            {
                var profile = BuildProfileFromUi();
                DateTime now = DateTime.Now; // локальное время компьютера

                DateTime? due = CopyFromRsnScheduleService.GetDueScheduledRun(now, profile);
                if (!due.HasValue)
                    return;

                if (CopyFromRsnScheduleService.ReadLastScheduledRun(profile) >= due.Value)
                    return;

                CopyFromRsnService.ValidateProfile(profile);

                string? message = CopyFromRsnScheduleService.TryRaiseDueRun(profile, now);
                if (message == null)
                    return;

                IsBusy = true;
                StatusText = message;
            }
            catch (Exception ex)
            {
                // Показываем причину один раз на слот (без спама каждый тик — state ещё не записан,
                // поэтому при постоянной ошибке валидации будет повторяться; это ожидаемо)
                StatusText = "Расписание: " + ex.Message;
            }
        }

        private void InitWeekDays()
        {
            WeekDayOptions.Clear();
            WeekDayOptions.Add(new WeekDayOption(1, "Пн"));
            WeekDayOptions.Add(new WeekDayOption(2, "Вт"));
            WeekDayOptions.Add(new WeekDayOption(3, "Ср"));
            WeekDayOptions.Add(new WeekDayOption(4, "Чт"));
            WeekDayOptions.Add(new WeekDayOption(5, "Пт"));
            WeekDayOptions.Add(new WeekDayOption(6, "Сб"));
            WeekDayOptions.Add(new WeekDayOption(0, "Вс"));
        }

        private void RefreshProfileList()
        {
            // Clear у ItemsSource сбрасывает Text у ComboBox (TwoWay) — сохраняем имя
            string keepName = ProfileName;
            _suppressProfileSelectionLoad = true;
            try
            {
                ProfileNames.Clear();
                foreach (string name in CopyFromRsnProfileStorage.ListProfileNames(_revitVersion))
                    ProfileNames.Add(name);

                if (!string.IsNullOrWhiteSpace(keepName))
                    ProfileName = keepName;
            }
            finally
            {
                _suppressProfileSelectionLoad = false;
            }
        }

        public void LoadProfile(string profileName)
        {
            var profile = CopyFromRsnProfileStorage.LoadProfile(_revitVersion, profileName);
            _loadedProfileName = profileName;
            ApplyProfileToUi(profile);
            OnPropertyChanged(nameof(CalendarHint));
        }

        private void ApplyProfileToUi(CopyFromRsnProfile profile)
        {
            ProfileName = profile.Name;
            SourceServer = profile.SourceServer;
            UseSourceRevitServer = profile.UseSourceRevitServer;
            TargetServer = profile.TargetServer;
            UseTargetRevitServer = profile.UseTargetRevitServer;
            LocalDownloadFolder = profile.LocalDownloadFolder;
            RevitServerToolPath = string.IsNullOrWhiteSpace(profile.RevitServerToolPath)
                ? CopyFromRsnProfileStorage.GetDefaultRevitServerToolPath(_revitVersion)
                : profile.RevitServerToolPath;
            RepathLinks = profile.RepathLinks;
            SuppressOpenWarnings = profile.SuppressOpenWarnings;
            ScheduleEnabled = profile.ScheduleEnabled;

            Models.Clear();
            foreach (ModelCopyEntryData model in profile.Models)
                Models.Add(ModelCopyEntry.FromData(model));

            Links.Clear();
            foreach (LinkCopyEntryData link in profile.Links)
                Links.Add(LinkCopyEntry.FromData(link));

            RunTimes.Clear();
            foreach (string time in profile.RunTimes)
                RunTimes.Add(time);

            foreach (WeekDayOption option in WeekDayOptions)
                option.IsSelected = profile.WeekDays.Contains(option.DayOfWeek);
        }

        private CopyFromRsnProfile BuildProfileFromUi()
        {
            // TextBox по умолчанию пишет в source только на LostFocus —
            // при «Сохранить»/«Запустить» без ухода с поля уходил старый путь.
            CommitPendingTextEdits();

            var profile = new CopyFromRsnProfile
            {
                Name = ProfileName,
                SourceServer = SourceServer,
                UseSourceRevitServer = UseSourceRevitServer,
                TargetServer = TargetServer,
                UseTargetRevitServer = UseTargetRevitServer,
                LocalDownloadFolder = LocalDownloadFolder,
                RevitServerToolPath = RevitServerToolPath,
                RepathLinks = RepathLinks,
                SuppressOpenWarnings = SuppressOpenWarnings,
                ScheduleEnabled = ScheduleEnabled,
                Models = Models.Select(m => m.ToData()).ToList(),
                Links = Links.Select(l => l.ToData()).ToList(),
                RunTimes = RunTimes.ToList(),
                WeekDays = WeekDayOptions.Where(d => d.IsSelected).Select(d => d.DayOfWeek).ToList()
            };

            return CopyFromRsnProfileStorage.Normalize(profile);
        }

        /// <summary>Сбрасывает несохранённый ввод из сфокусированного TextBox в свойства ViewModel.</summary>
        private static void CommitPendingTextEdits()
        {
            if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox textBox)
                return;

            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }

        private void SaveProfile()
        {
            string newName = (ProfileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Укажите имя профиля.", "SP CopyFromRSN");
                return;
            }

            var profile = BuildProfileFromUi();
            profile.Name = newName;
            CopyFromRsnProfileStorage.SaveProfile(_revitVersion, profile);

            // При переименовании удаляем старый файл профиля
            if (!string.IsNullOrWhiteSpace(_loadedProfileName) &&
                !string.Equals(_loadedProfileName, newName, StringComparison.OrdinalIgnoreCase) &&
                CopyFromRsnProfileStorage.ListProfileNames(_revitVersion)
                    .Any(n => string.Equals(n, _loadedProfileName, StringComparison.OrdinalIgnoreCase)))
            {
                CopyFromRsnProfileStorage.DeleteProfile(_revitVersion, _loadedProfileName);
            }

            _loadedProfileName = newName;
            ProfileName = newName;
            CopyFromRsnProfileStorage.SetActiveProfile(_revitVersion, profile.Name);
            CopyFromRsnRuntime.ActiveProfile = profile;
            RefreshProfileList();
            StatusText = "Профиль сохранён: " + profile.Name;
        }

        private void CreateNewProfile()
        {
            string baseName = "Новый профиль";
            int index = 1;
            string name = baseName;
            while (ProfileNames.Contains(name))
            {
                index++;
                name = baseName + " " + index;
            }

            var profile = CopyFromRsnProfileStorage.CreateDefaultProfile(_revitVersion);
            profile.Name = name;
            _loadedProfileName = string.Empty;
            ApplyProfileToUi(profile);
            StatusText = "Создан новый профиль. Укажите имя и сохраните.";
        }

        private void DeleteProfile()
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
                return;

            if (MessageBox.Show(
                    "Удалить профиль «" + ProfileName + "»?",
                    "SP CopyFromRSN",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            CopyFromRsnProfileStorage.DeleteProfile(_revitVersion, ProfileName);
            RefreshProfileList();

            if (ProfileNames.Count > 0)
                LoadProfile(ProfileNames[0]);
            else
                ApplyProfileToUi(CopyFromRsnProfileStorage.CreateDefaultProfile(_revitVersion));
        }

        private void ImportCsv()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv|Все файлы (*.*)|*.*",
                Title = "Импорт списка моделей"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var profile = BuildProfileFromUi();
                CopyFromRsnCsvIO.ImportModelsCsv(dialog.FileName, profile);
                ApplyProfileToUi(profile);

                string serverHint = string.IsNullOrWhiteSpace(profile.TargetServer)
                    ? ""
                    : " Целевой сервер: " + profile.TargetServer + ".";
                StatusText = "Модели импортированы из CSV." + serverHint;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка импорта CSV");
            }
        }

        private void ExportCsv()
        {
            // Имя файла по умолчанию — из текущего профиля
            string safeName = CopyFromRsnProfileStorage.SanitizeFileName(ProfileName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "RSN_Copy_Models";

            var dialog = new SaveFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv",
                FileName = safeName + ".csv",
                Title = "Экспорт списка моделей"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                CopyFromRsnCsvIO.ExportModelsCsv(dialog.FileName, BuildProfileFromUi());
                StatusText = "Модели экспортированы в CSV.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка экспорта CSV");
            }
        }

        private void AddRunTime()
        {
            TimeSpan? parsed = CopyFromRsnScheduleService.ParseTime(NewRunTime);
            if (!parsed.HasValue)
            {
                MessageBox.Show("Введите время в формате ЧЧ:ММ.", "SP CopyFromRSN");
                return;
            }

            string formatted = parsed.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            if (!RunTimes.Contains(formatted))
                RunTimes.Add(formatted);

            OnPropertyChanged(nameof(CalendarHint));
        }

        private void RemoveRunTime()
        {
            if (SelectedRunTime != null && RunTimes.Contains(SelectedRunTime))
                RunTimes.Remove(SelectedRunTime);

            OnPropertyChanged(nameof(CalendarHint));
        }

        private void RunNow()
        {
            try
            {
                var profile = BuildProfileFromUi();
                CopyFromRsnService.ValidateProfile(profile);
                SaveProfile();

                IsBusy = true;
                StatusText = "Запуск копирования...";

                // ExternalEvent работает независимо от окна (окно безмодальное)
                CopyFromRsnApp.RaiseCopy(profile);
            }
            catch (Exception ex)
            {
                IsBusy = false;
                StatusText = ex.Message;
            }
        }

        private void CopySelectedModel()
        {
            if (SelectedModel == null)
                return;

            CopyTextToClipboard($"{SelectedModel.SourcePath}\t{SelectedModel.TargetPath}");
        }

        private void CopySelectedLink()
        {
            if (SelectedLink == null)
                return;

            CopyTextToClipboard($"{SelectedLink.ModelName}\t{SelectedLink.OldPath}\t{SelectedLink.NewPath}");
        }

        private void CopyStatus()
        {
            if (string.IsNullOrWhiteSpace(StatusText))
                return;

            CopyTextToClipboard(StatusText);
        }

        private void CopyTextToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                string previous = StatusText;
                StatusText = "Скопировано в буфер обмена.";
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.5)
                };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    if (StatusText == "Скопировано в буфер обмена.")
                        StatusText = previous;
                };
                timer.Start();
            }
            catch
            {
                StatusText = "Не удалось скопировать в буфер обмена.";
            }
        }

        private void BrowseFolder()
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Папка для локальных копий моделей",
                SelectedPath = LocalDownloadFolder
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                LocalDownloadFolder = dialog.SelectedPath.EndsWith("\\")
                    ? dialog.SelectedPath
                    : dialog.SelectedPath + "\\";
        }

        private void BrowseToolPath()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "RevitServerTool.exe|RevitServerTool.exe|Все файлы (*.*)|*.*",
                Title = "Выберите RevitServerTool.exe"
            };

            if (dialog.ShowDialog() == true)
                RevitServerToolPath = dialog.FileName;
        }

        private void OnRuntimeStatusChanged(string message) =>
            DispatchUi(() => StatusText = message);

        private void OnRunCompleted(string result, bool isError) =>
            DispatchUi(() =>
            {
                IsBusy = false;
                StatusText = result;
            });

        private void DispatchUi(Action action)
        {
            Dispatcher? dispatcher = _uiDispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private void NotifyCommandsCanExecuteChanged()
        {
            SaveProfileCommand.NotifyCanExecuteChanged();
            NewProfileCommand.NotifyCanExecuteChanged();
            DeleteProfileCommand.NotifyCanExecuteChanged();
            ImportCsvCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();
            AddModelCommand.NotifyCanExecuteChanged();
            RemoveModelCommand.NotifyCanExecuteChanged();
            CopySelectedModelCommand.NotifyCanExecuteChanged();
            AddLinkCommand.NotifyCanExecuteChanged();
            RemoveLinkCommand.NotifyCanExecuteChanged();
            CopySelectedLinkCommand.NotifyCanExecuteChanged();
            AddRunTimeCommand.NotifyCanExecuteChanged();
            RemoveRunTimeCommand.NotifyCanExecuteChanged();
            RunNowCommand.NotifyCanExecuteChanged();
            BrowseFolderCommand.NotifyCanExecuteChanged();
            BrowseToolPathCommand.NotifyCanExecuteChanged();
            CopyStatusCommand.NotifyCanExecuteChanged();
        }
    }
}
