using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SP_CopyFromRSN.Models;

namespace SP_CopyFromRSN.Services
{
    /// <summary>Импорт и экспорт CSV (совместимость с макросом / Dynamo).</summary>
    public static class CopyFromRsnCsvIO
    {
        public static void ImportModelsCsv(string csvPath, CopyFromRsnProfile profile)
        {
            profile.Models.Clear();

            string? sourceServerFromCsv = null;
            string? targetServerFromCsv = null;

            foreach (ModelCopyEntryData row in ReadModelRows(csvPath))
            {
                string sourcePath = row.SourcePath;
                string targetPath = row.TargetPath;

                // RSN://host/относительный → host в поля серверов, в таблице оставляем относительный путь
                if (TrySplitRsnPath(sourcePath, out string sourceHost, out string sourceRelative))
                {
                    if (sourceServerFromCsv == null)
                        sourceServerFromCsv = sourceHost;
                    sourcePath = sourceRelative;
                }

                if (TrySplitRsnPath(targetPath, out string targetHost, out string targetRelative))
                {
                    if (targetServerFromCsv == null)
                        targetServerFromCsv = targetHost;
                    targetPath = targetRelative;
                }

                profile.Models.Add(new ModelCopyEntryData
                {
                    SourcePath = sourcePath,
                    TargetPath = targetPath
                });
            }

            if (!string.IsNullOrWhiteSpace(sourceServerFromCsv))
                profile.SourceServer = sourceServerFromCsv!;
            if (!string.IsNullOrWhiteSpace(targetServerFromCsv))
                profile.TargetServer = targetServerFromCsv!;
        }

        public static void ImportLinksCsv(string csvPath, CopyFromRsnProfile profile)
        {
            profile.Links.Clear();
            foreach (LinkCopyEntryData row in ReadLinkRows(csvPath))
                profile.Links.Add(row);
        }

        public static void ExportModelsCsv(string csvPath, CopyFromRsnProfile profile)
        {
            var lines = new List<string> { "SourcePath;TargetPath" };
            foreach (ModelCopyEntryData model in profile.Models)
                lines.Add($"{Escape(model.SourcePath)};{Escape(model.TargetPath)}");

            File.WriteAllLines(csvPath, lines, Encoding.Default);
        }

        public static void ExportLinksCsv(string csvPath, CopyFromRsnProfile profile)
        {
            var lines = new List<string> { "ModelName;OldPath;NewPath" };
            foreach (LinkCopyEntryData link in profile.Links)
                lines.Add($"{Escape(link.ModelName)};{Escape(link.OldPath)};{Escape(link.NewPath)}");

            File.WriteAllLines(csvPath, lines, Encoding.Default);
        }

        public static List<ModelCopyEntryData> ReadModelRows(string csvPath)
        {
            string[] lines = ReadCsvLines(csvPath);
            var rows = new List<ModelCopyEntryData>();

            for (int lineNumber = 1; lineNumber < lines.Length; lineNumber++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineNumber]))
                    continue;

                string[] columns = lines[lineNumber].Split(';');
                if (columns.Length < 2)
                    throw new InvalidDataException(
                        "Строка " + (lineNumber + 1) + " должна содержать исходный и целевой пути.");

                rows.Add(new ModelCopyEntryData
                {
                    SourcePath = CleanCsvValue(columns[0]),
                    TargetPath = CleanCsvValue(columns[1])
                });
            }

            if (rows.Count == 0)
                throw new InvalidDataException("CSV не содержит моделей.");

            return rows;
        }

        public static List<LinkCopyEntryData> ReadLinkRows(string csvPath)
        {
            string[] lines = ReadCsvLines(csvPath);
            var rows = new List<LinkCopyEntryData>();

            for (int lineNumber = 1; lineNumber < lines.Length; lineNumber++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineNumber]))
                    continue;

                string[] columns = lines[lineNumber].Split(';');
                if (columns.Length < 3)
                    throw new InvalidDataException(
                        "Строка " + (lineNumber + 1) + " должна содержать имя модели и два пути.");

                string modelName = CleanCsvValue(columns[0]);
                if (!modelName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                    modelName += ".rvt";

                string oldPath = CleanCsvValue(columns[1]);
                string newPath = CleanCsvValue(columns[2]);
                if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath) ||
                    oldPath == "-" || newPath == "-")
                    continue;

                rows.Add(new LinkCopyEntryData
                {
                    ModelName = modelName,
                    OldPath = oldPath,
                    NewPath = newPath
                });
            }

            return rows;
        }

        /// <summary>
        /// Разбор RSN://server[/относительный/путь.rvt] → сервер и относительный путь.
        /// </summary>
        public static bool TrySplitRsnPath(string path, out string server, out string relativePath)
        {
            server = string.Empty;
            relativePath = path ?? string.Empty;

            string trimmed = relativePath.Trim();
            const string prefix = "RSN://";
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string rest = trimmed.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash < 0)
            {
                server = rest.Trim().TrimEnd('/');
                relativePath = string.Empty;
                return !string.IsNullOrWhiteSpace(server);
            }

            if (slash == 0)
                return false;

            server = rest.Substring(0, slash).Trim();
            relativePath = rest.Substring(slash + 1).TrimStart('/');
            return !string.IsNullOrWhiteSpace(server);
        }

        private static string[] ReadCsvLines(string csvPath)
        {
            byte[] bytes = File.ReadAllBytes(csvPath);
            Encoding encoding = DetectCsvEncoding(bytes);
            string text = encoding.GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);

            string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            // UTF-16 при неверной кодировке даёт «пустые» строки из \0 — отбрасываем
            var lines = new List<string>(rawLines.Length);
            foreach (string line in rawLines)
            {
                string cleaned = line.Replace("\0", string.Empty);
                lines.Add(cleaned);
            }

            return lines.ToArray();
        }

        private static Encoding DetectCsvEncoding(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF-16 BE
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode; // UTF-16 LE
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            // UTF-8 без BOM (типично для выгрузок с RSN:// и кириллицей в путях)
            string asUtf8 = Encoding.UTF8.GetString(bytes);
            if (asUtf8.IndexOf('\uFFFD') < 0)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            return Encoding.Default;
        }

        private static string CleanCsvValue(string value)
        {
            string result = (value ?? string.Empty).Replace("\0", string.Empty).Trim();
            if (result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"')
                result = result.Substring(1, result.Length - 2).Replace("\"\"", "\"");
            return result;
        }

        private static string Escape(string value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
