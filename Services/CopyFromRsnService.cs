using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using SP_CopyFromRSN.Models;

namespace SP_CopyFromRSN.Services
{
    /// <summary>Основная логика копирования моделей с RSN (из макроса).</summary>
    public sealed class CopyFromRsnService
    {
        private readonly Application _application;
        private readonly CopyFromRsnProfile _profile;
        private readonly Action<string, bool> _log;

        public CopyFromRsnService(Application application, CopyFromRsnProfile profile, Action<string, bool> log)
        {
            _application = application;
            _profile = CopyFromRsnProfileStorage.Normalize(profile.Clone());
            _log = log;
        }

        public string Execute()
        {
            Directory.CreateDirectory(_profile.LocalDownloadFolder);
            DeleteOldLogs();
            ValidateSettings();

            List<ModelCopyRow> models = BuildModelRows();
            Dictionary<string, string> localPaths = PrepareLocalModelCopies(models);

            if (_profile.RepathLinks)
                RepathExternalLinks(localPaths, BuildLinkRows());
            else
                Log("Замена путей связанных файлов отключена.");

            int savedCount = 0;
            int failedCount = 0;

            foreach (ModelCopyRow model in models)
            {
                string localPath = localPaths[model.ModelName];
                try
                {
                    OpenDetachAndSave(localPath, ResolveTargetPath(model.TargetPath), true);
                    savedCount++;
                }
                catch (Exception auditException)
                {
                    Log(
                        "Не удалось обработать " + model.ModelName +
                        " с Audit=true: " + auditException.Message +
                        ". Повторяем без аудита.",
                        true);

                    try
                    {
                        OpenDetachAndSave(localPath, ResolveTargetPath(model.TargetPath), false);
                        savedCount++;
                    }
                    catch (Exception exception)
                    {
                        failedCount++;
                        Log("Ошибка обработки " + model.ModelName + ": " + exception, true);
                    }
                }
            }

            string result =
                "Обработка завершена. Сохранено: " + savedCount +
                ", ошибок: " + failedCount + ".";
            Log(result);
            return result;
        }

        public static void ValidateProfile(CopyFromRsnProfile profile)
        {
            var normalized = CopyFromRsnProfileStorage.Normalize(profile.Clone());

            if (string.IsNullOrWhiteSpace(normalized.LocalDownloadFolder))
                throw new InvalidOperationException("Укажите папку загрузки на вкладке «Общие».");

            bool hasRsnSources = normalized.Models.Any(m => UsesRsnPath(m.SourcePath));
            bool hasRelativeRsnSources = normalized.Models.Any(m => UsesRelativeRsnPath(m.SourcePath));
            bool hasRelativeRsnTargets = normalized.Models.Any(m => UsesRelativeRsnPath(m.TargetPath));

            if (hasRelativeRsnSources && !normalized.UseSourceRevitServer)
                throw new InvalidOperationException(
                    "Для относительных исходных путей на RSN включите флаг «Revit Server» или задайте полный путь RSN/файловый путь.");

            if (hasRelativeRsnSources && string.IsNullOrWhiteSpace(normalized.SourceServer))
                throw new InvalidOperationException(
                    "Укажите исходный сервер RSN на вкладке «Модели» или задайте полный путь RSN/файловый путь.");

            if (hasRelativeRsnTargets && !normalized.UseTargetRevitServer)
                throw new InvalidOperationException(
                    "Для относительных целевых путей на RSN включите флаг «Revit Server» или задайте полный путь RSN/файловый путь.");

            if (hasRelativeRsnTargets && string.IsNullOrWhiteSpace(normalized.TargetServer))
                throw new InvalidOperationException(
                    "Укажите целевой сервер RSN на вкладке «Модели» или задайте полный путь RSN/файловый путь.");

            if (hasRsnSources && string.IsNullOrWhiteSpace(normalized.RevitServerToolPath))
                throw new InvalidOperationException(
                    "Укажите путь к RevitServerTool.exe на вкладке «Общие» для загрузки моделей из RSN.");

            if (hasRsnSources && !File.Exists(normalized.RevitServerToolPath))
                throw new InvalidOperationException(
                    "Не найден RevitServerTool.exe:\n" + normalized.RevitServerToolPath);

            if (normalized.Models.Count == 0)
                throw new InvalidOperationException(
                    "Добавьте хотя бы одну модель на вкладке «Модели».");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < normalized.Models.Count; i++)
            {
                ModelCopyEntryData model = normalized.Models[i];
                int row = i + 1;

                if (string.IsNullOrWhiteSpace(model.SourcePath))
                    throw new InvalidOperationException(
                        "Строка " + row + ": не заполнен исходный путь к модели.");

                if (string.IsNullOrWhiteSpace(model.TargetPath))
                    throw new InvalidOperationException(
                        "Строка " + row + ": не заполнен целевой путь к модели.");

                string modelName;
                try
                {
                    modelName = GetModelName(model.SourcePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Строка " + row + ": " + ex.Message, ex);
                }

                if (!names.Add(modelName))
                    throw new InvalidOperationException(
                        "Строка " + row + ": модель с именем «" + modelName +
                        "» уже есть в списке (имена файлов должны быть уникальны).");
            }
        }

        private void ValidateSettings()
        {
            ValidateProfile(_profile);

            if (_profile.RepathLinks && _profile.Links.Count == 0)
                Log("Включена замена связей, но список связей пуст.", true);
        }

        private List<ModelCopyRow> BuildModelRows()
        {
            return _profile.Models
                .Select(m => CreateModelRow(m.SourcePath, m.TargetPath))
                .ToList();
        }

        private List<LinkCopyRow> BuildLinkRows()
        {
            return _profile.Links
                .Select(l => new LinkCopyRow(l.ModelName, l.OldPath, l.NewPath))
                .ToList();
        }

        /// <summary>
        /// Подготавливает локальные копии моделей для дальнейшей обработки:
        /// из RSN через RevitServerTool или прямым копированием из файловой системы.
        /// </summary>
        private Dictionary<string, string> PrepareLocalModelCopies(List<ModelCopyRow> models)
        {
            var localPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(_profile.LocalDownloadFolder);

            // Как в Dynamo: -d — папка со слэшем «/» (не полный путь к .rvt)
            string destinationFolder = NormalizeDownloadFolderForTool(_profile.LocalDownloadFolder);

            foreach (ModelCopyRow model in models)
            {
                string localPath = Path.Combine(_profile.LocalDownloadFolder, model.ModelName);
                if (localPaths.ContainsKey(model.ModelName))
                    throw new InvalidDataException(
                        "Обнаружены модели с одинаковым именем: " + model.ModelName);

                if (File.Exists(localPath))
                    File.Delete(localPath);

                if (model.IsFileSystemSource)
                {
                    Log("Копирование локальной копии " + model.ModelName + " из файловой структуры.");
                    CopyModelFromFileSystem(model.ResolvedSourcePath, localPath);
                }
                else
                {
                    Log("Загрузка локальной копии " + model.ModelName + " из RSN.");
                    RunRevitServerTool(model.ResolvedSourcePath, model.SourceServer, destinationFolder);
                }

                if (!File.Exists(localPath))
                    throw new FileNotFoundException(
                        "Не удалось подготовить локальную модель:\n" + localPath +
                        "\nПроверьте исходный путь, доступ к нему и папку загрузки.",
                        localPath);

                localPaths.Add(model.ModelName, localPath);
            }

            Log("Локальные копии моделей подготовлены в " + _profile.LocalDownloadFolder + ".");
            return localPaths;
        }

        /// <summary>Папка для -d как в Dynamo: C:\temp\revit_models/</summary>
        private static string NormalizeDownloadFolderForTool(string folder)
        {
            string normalized = folder.Trim().Replace('/', '\\').TrimEnd('\\');
            return normalized + "/";
        }

        /// <summary>Копирует модель из обычной файловой структуры во временную рабочую папку.</summary>
        private void CopyModelFromFileSystem(string sourcePath, string localPath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        "Не найден исходный файл модели:\n" + sourcePath,
                        sourcePath);
                }

                File.Copy(sourcePath, localPath, true);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Не удалось скопировать модель из файловой структуры:\n" + sourcePath,
                    exception);
            }
        }

        /// <summary>Запускает RevitServerTool для загрузки модели из RSN в локальную папку.</summary>
        private void RunRevitServerTool(string sourceModelPath, string sourceServer, string destinationFolder)
        {
            string toolPath = _profile.RevitServerToolPath;
            // Аргументы списком — как subprocess.run в Dynamo (без ручной склейки кавычек)
            string[] args =
            {
                "createLocalRVT",
                sourceModelPath.Replace('/', '\\').Trim(),
                "-s",
                sourceServer.Trim(),
                "-d",
                destinationFolder,
                "-o"
            };

            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = BuildCommandLine(args),
                WorkingDirectory = Path.GetDirectoryName(toolPath) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) error.AppendLine(e.Data);
                };

                Log("Команда RevitServerTool: " + toolPath + " " + BuildCommandLine(args));

                if (!process.Start())
                    throw new InvalidOperationException("Не удалось запустить RevitServerTool.exe.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                string standardOutput = output.ToString().Trim();
                string standardError = error.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(standardOutput))
                    Log("RevitServerTool stdout: " + standardOutput);
                if (!string.IsNullOrWhiteSpace(standardError))
                    Log("RevitServerTool stderr: " + standardError, true);

                bool failedByText =
                    ContainsIgnoreCase(standardOutput, "Cannot create the local model") ||
                    ContainsIgnoreCase(standardOutput, "Failed") ||
                    ContainsIgnoreCase(standardError, "Cannot create the local model") ||
                    ContainsIgnoreCase(standardError, "Failed");

                if (process.ExitCode != 0 || failedByText)
                {
                    string details = !string.IsNullOrWhiteSpace(standardError)
                        ? standardError
                        : standardOutput;
                    throw new InvalidOperationException(
                        "RevitServerTool не смог создать локальную копию модели." +
                        (string.IsNullOrWhiteSpace(details) ? "" : "\n" + details) +
                        "\nСервер: " + sourceServer +
                        "\nПуть на RSN: " + sourceModelPath +
                        "\nПапка: " + destinationFolder);
                }
            }
        }

        /// <summary>
        /// Экранирование аргументов как у Python subprocess / .NET ArgumentList:
        /// хвостовой \ перед " не ломает следующий аргумент.
        /// </summary>
        private static string BuildCommandLine(IEnumerable<string> arguments)
        {
            var sb = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (sb.Length > 0)
                    sb.Append(' ');

                AppendEscapedArgument(sb, argument ?? string.Empty);
            }

            return sb.ToString();
        }

        private static void AppendEscapedArgument(StringBuilder sb, string argument)
        {
            argument ??= string.Empty;

            // Уже в кавычках — не трогаем
            if (argument.Length >= 2 && argument[0] == '"' && argument[argument.Length - 1] == '"')
            {
                sb.Append(argument);
                return;
            }

            // Пути (слэши) и значения с пробелами — всегда в кавычках
            bool needsQuotes = false;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '"' ||
                    c == '\\' || c == '/')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes)
            {
                sb.Append(argument);
                return;
            }

            sb.Append('"');
            int backslashes = 0;
            for (int i = 0; i < argument.Length; i++)
            {
                char c = argument[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }

                sb.Append(c);
            }

            // Перед закрывающей кавычкой удваиваем хвостовые \
            if (backslashes > 0)
                sb.Append('\\', backslashes * 2);

            sb.Append('"');
        }

        private void RepathExternalLinks(Dictionary<string, string> localPaths, List<LinkCopyRow> links)
        {
            foreach (LinkCopyRow link in links)
            {
                if (!localPaths.TryGetValue(link.ModelName, out string? localPath) ||
                    string.IsNullOrWhiteSpace(localPath))
                {
                    Log("Для замены связи не найдена модель " + link.ModelName + ".", true);
                    continue;
                }

                try
                {
                    ModelPath modelPath = new FilePath(localPath);
                    TransmissionData? transmissionData = TransmissionData.ReadTransmissionData(modelPath);

                    if (transmissionData == null)
                    {
                        Log("В модели " + link.ModelName + " отсутствуют данные о связях.", true);
                        continue;
                    }

                    bool changed = false;
                    foreach (ElementId referenceId in transmissionData.GetAllExternalFileReferenceIds())
                    {
                        ExternalFileReference? reference =
                            transmissionData.GetLastSavedReferenceData(referenceId);
                        if (reference == null || reference.GetAbsolutePath() == null)
                            continue;

                        string currentPath =
                            ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetAbsolutePath());

                        if (!PathsEqual(currentPath, link.OldPath))
                            continue;

                        // Для RSN сохраняем серверный путь, для файловой структуры — абсолютный.
                        ModelPath newPath =
                            ModelPathUtils.ConvertUserVisiblePathToModelPath(link.NewPath);
                        transmissionData.SetDesiredReferenceData(
                            referenceId, newPath, ResolveLinkPathType(link.NewPath), true);
                        changed = true;
                    }

                    if (changed)
                    {
                        transmissionData.IsTransmitted = true;
                        TransmissionData.WriteTransmissionData(modelPath, transmissionData);
                        Log(
                            "В модели " + link.ModelName +
                            " путь связи заменён с " + link.OldPath +
                            " на " + link.NewPath + ".");
                    }
                    else
                    {
                        Log(
                            "В модели " + link.ModelName +
                            " не найдена связь " + link.OldPath + ".",
                            true);
                    }
                }
                catch (Exception exception)
                {
                    Log("Ошибка замены связи в " + link.ModelName + ": " + exception, true);
                }
            }
        }

        private void OpenDetachAndSave(string localPath, string targetPath, bool audit)
        {
            SaveAsOptions saveOptions = CreateSaveAsOptions();
            ModelPath localModelPath =
                ModelPathUtils.ConvertUserVisiblePathToModelPath(localPath);

            Document? document = null;
            try
            {
                OpenOptions detachedOptions = CreateOpenOptions(
                    DetachFromCentralOption.DetachAndPreserveWorksets, audit);
                document = _application.OpenDocumentFile(localModelPath, detachedOptions);
                document.SaveAs(localPath, saveOptions);
                Log("Модель " + Path.GetFileName(localPath) + " отсоединена.");
            }
            finally
            {
                document?.Close(false);
            }

            document = null;
            try
            {
                OpenOptions normalOptions = CreateOpenOptions(
                    DetachFromCentralOption.DoNotDetach, audit);
                document = _application.OpenDocumentFile(localModelPath, normalOptions);

                EnsureTargetDirectoryExists(targetPath);
                ModelPath targetModelPath =
                    ModelPathUtils.ConvertUserVisiblePathToModelPath(targetPath);
                document.SaveAs(targetModelPath, saveOptions);
                Log("Модель сохранена по пути " + targetPath + ".");
            }
            finally
            {
                document?.Close(false);
            }
        }

        private static OpenOptions CreateOpenOptions(DetachFromCentralOption detachOption, bool audit)
        {
            var options = new OpenOptions
            {
                DetachFromCentralOption = detachOption,
                Audit = audit
            };
            options.SetOpenWorksetsConfiguration(
                new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));
            return options;
        }

        private static SaveAsOptions CreateSaveAsOptions()
        {
            var options = new SaveAsOptions { OverwriteExistingFile = true };
            var worksharingOptions = new WorksharingSaveAsOptions { SaveAsCentral = true };
            options.SetWorksharingOptions(worksharingOptions);
            return options;
        }

        private static string GetModelName(string sourcePath)
        {
            string normalized = sourcePath.Replace('/', '\\').TrimEnd('\\');
            int separatorIndex = normalized.LastIndexOf('\\');
            string name = separatorIndex >= 0
                ? normalized.Substring(separatorIndex + 1)
                : normalized;

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException(
                    "Не удалось определить имя модели из пути " + sourcePath + ".");

            return name;
        }

        private static bool PathsEqual(string first, string second) =>
            string.Equals(
                first.Trim().TrimEnd('\\', '/'),
                second.Trim().TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Определяет, что путь указывает на обычную файловую систему, а не на RSN.</summary>
        private static bool IsFileSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string trimmed = path.Trim();
            return trimmed.StartsWith(@"\\", StringComparison.Ordinal) ||
                   trimmed.StartsWith("//", StringComparison.Ordinal) ||
                   Path.IsPathRooted(trimmed);
        }

        /// <summary>Определяет, что путь должен обрабатываться как RSN.</summary>
        private static bool UsesRsnPath(string path) =>
            !string.IsNullOrWhiteSpace(path) && !IsFileSystemPath(path);

        /// <summary>Определяет, что путь на RSN задан относительно сервера из настроек профиля.</summary>
        private static bool UsesRelativeRsnPath(string path) =>
            UsesRsnPath(path) &&
            !CopyFromRsnCsvIO.TrySplitRsnPath(path ?? string.Empty, out _, out _);

        private static bool ContainsIgnoreCase(string text, string value) =>
            !string.IsNullOrEmpty(text) &&
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Преобразует источник модели в рабочий формат для RSN или файловой системы.</summary>
        private ModelCopyRow CreateModelRow(string sourcePath, string targetPath)
        {
            string modelName = GetModelName(sourcePath);
            string resolvedSourcePath = ResolveSourcePath(
                sourcePath,
                out string sourceServer,
                out bool isFileSystemSource);

            return new ModelCopyRow(
                sourcePath,
                targetPath,
                modelName,
                resolvedSourcePath,
                sourceServer,
                isFileSystemSource);
        }

        /// <summary>Разрешает исходный путь модели с учётом RSN и обычных файловых путей.</summary>
        private string ResolveSourcePath(
            string sourcePath,
            out string sourceServer,
            out bool isFileSystemSource)
        {
            string trimmed = (sourcePath ?? string.Empty).Trim();
            sourceServer = string.Empty;

            if (IsFileSystemPath(trimmed))
            {
                isFileSystemSource = true;
                return Path.GetFullPath(trimmed);
            }

            isFileSystemSource = false;
            if (CopyFromRsnCsvIO.TrySplitRsnPath(trimmed, out string rsnServer, out string rsnRelativePath))
            {
                sourceServer = rsnServer;
                return NormalizeRelativeRsnPath(rsnRelativePath);
            }

            if (!_profile.UseSourceRevitServer)
                throw new InvalidOperationException(
                    "Для относительного исходного пути на RSN включите флаг «Revit Server».");

            sourceServer = (_profile.SourceServer ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceServer))
                throw new InvalidOperationException("Укажите исходный сервер RSN на вкладке «Модели».");

            return NormalizeRelativeRsnPath(trimmed);
        }

        /// <summary>Приводит относительный путь модели на RSN к формату с прямыми слэшами.</summary>
        private static string NormalizeRelativeRsnPath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().TrimStart('\\', '/').Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidDataException(
                    "Не удалось определить путь модели на RSN.");
            }

            return normalized;
        }

        /// <summary>Создаёт папку назначения, если целевой путь указывает на файловую систему.</summary>
        private static void EnsureTargetDirectoryExists(string targetPath)
        {
            if (!IsFileSystemPath(targetPath))
                return;

            string? directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
        }

        /// <summary>Определяет тип пути связи для сохранения в TransmissionData.</summary>
        private static PathType ResolveLinkPathType(string path) =>
            IsFileSystemPath(path) ? PathType.Absolute : PathType.Server;

        // Полный RSN-путь нормализуем, относительный собираем из целевого сервера.
        private string ResolveTargetPath(string targetPath)
        {
            string trimmed = (targetPath ?? string.Empty).Trim();
            if (CopyFromRsnCsvIO.TrySplitRsnPath(trimmed, out string rsnServer, out string rsnRelativePath))
                return "RSN://" + rsnServer + "/" + NormalizeRelativeRsnPath(rsnRelativePath);

            if (IsFileSystemPath(trimmed))
                return Path.GetFullPath(trimmed);

            if (!_profile.UseTargetRevitServer)
                throw new InvalidOperationException(
                    "Для относительного целевого пути на RSN включите флаг «Revit Server».");

            string server = (_profile.TargetServer ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(server))
                throw new InvalidOperationException("Укажите целевой сервер RSN на вкладке «Модели».");

            string relative = NormalizeRelativeRsnPath(trimmed);
            return "RSN://" + server + "/" + relative;
        }

        private void DeleteOldLogs()
        {
            try
            {
                foreach (string file in Directory.GetFiles(
                    _profile.LocalDownloadFolder, "CopyFiles2RSN_*.log"))
                {
                    if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-14))
                        File.Delete(file);
                }
            }
            catch (Exception exception)
            {
                Log("Не удалось удалить старые журналы: " + exception.Message, true);
            }
        }

        private void Log(string message, bool isError = false) => _log(message, isError);

        public static void LogStatic(string folder, string message, bool isError = false)
        {
            Directory.CreateDirectory(folder);
            string filePath = Path.Combine(
                folder, "CopyFiles2RSN_" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            string level = isError ? "ERROR" : "INFO";
            File.AppendAllText(
                filePath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " - " + level + " - " + message + Environment.NewLine,
                Encoding.UTF8);
        }

        private sealed class ModelCopyRow
        {
            public ModelCopyRow(
                string sourcePath,
                string targetPath,
                string modelName,
                string resolvedSourcePath,
                string sourceServer,
                bool isFileSystemSource)
            {
                SourcePath = sourcePath;
                TargetPath = targetPath;
                ModelName = modelName;
                ResolvedSourcePath = resolvedSourcePath;
                SourceServer = sourceServer;
                IsFileSystemSource = isFileSystemSource;
            }

            public string SourcePath { get; }
            public string TargetPath { get; }
            public string ModelName { get; }
            public string ResolvedSourcePath { get; }
            public string SourceServer { get; }
            public bool IsFileSystemSource { get; }
        }

        private sealed class LinkCopyRow
        {
            public LinkCopyRow(string modelName, string oldPath, string newPath)
            {
                ModelName = modelName;
                OldPath = oldPath;
                NewPath = newPath;
            }

            public string ModelName { get; }
            public string OldPath { get; }
            public string NewPath { get; }
        }
    }
}
