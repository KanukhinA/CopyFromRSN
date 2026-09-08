using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SP_CopyFromRSN.Models;

namespace SP_CopyFromRSN.Services
{
    /// <summary>Хранение JSON-профилей переноса.</summary>
    public static class CopyFromRsnProfileStorage
    {
        private const string ConfigFolderName = "SP_CopyFromRSN";
        private const string ConfigsSubfolder = "Configs";
        private const string ActiveProfileFileName = "active-profile.txt";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string GetConfigRoot(string revitVersion) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "Revit", "Addins", revitVersion, ConfigFolderName);

        public static string GetConfigsFolder(string revitVersion) =>
            Path.Combine(GetConfigRoot(revitVersion), ConfigsSubfolder);

        public static string GetProfileFilePath(string revitVersion, string profileName) =>
            Path.Combine(GetConfigsFolder(revitVersion), SanitizeFileName(profileName) + ".json");

        public static string GetStateFilePath(CopyFromRsnProfile profile) =>
            Path.Combine(
                string.IsNullOrWhiteSpace(profile.LocalDownloadFolder) ? @"C:\Temp\" : profile.LocalDownloadFolder.Trim(),
                "RSNCopyMacro.state");

        public static void EnsureFolders(string revitVersion)
        {
            Directory.CreateDirectory(GetConfigsFolder(revitVersion));
        }

        public static CopyFromRsnProfile CreateDefaultProfile(string revitVersion)
        {
            var profile = new CopyFromRsnProfile
            {
                Name = "По умолчанию",
                RevitServerToolPath = GetDefaultRevitServerToolPath(revitVersion)
            };
            return Normalize(profile);
        }

        public static string GetDefaultRevitServerToolPath(string revitVersion)
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string toolPath = Path.Combine(
                programFiles,
                "Autodesk",
                "Revit " + revitVersion,
                "RevitServerToolCommand",
                "RevitServerTool.exe");

            if (File.Exists(toolPath))
                return toolPath;

            // Запасной путь из макроса
            return Path.Combine(
                @"X:\Autodesk\Revit\" + revitVersion,
                "Revit " + revitVersion,
                "RevitServerToolCommand",
                "RevitServerTool.exe");
        }

        public static List<string> ListProfileNames(string revitVersion)
        {
            EnsureFolders(revitVersion);
            return Directory.GetFiles(GetConfigsFolder(revitVersion), "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Cast<string>()
                .ToList();
        }

        public static CopyFromRsnProfile LoadProfile(string revitVersion, string profileName)
        {
            string path = GetProfileFilePath(revitVersion, profileName);
            if (!File.Exists(path))
                return CreateDefaultProfile(revitVersion);

            try
            {
                string json = File.ReadAllText(path);
                var profile = JsonSerializer.Deserialize<CopyFromRsnProfile>(json, JsonOptions);
                if (profile == null)
                    return CreateDefaultProfile(revitVersion);

                profile.Name = profileName;

                // Старые профили без поля — по умолчанию включено
                if (json.IndexOf("suppressOpenWarnings", StringComparison.OrdinalIgnoreCase) < 0)
                    profile.SuppressOpenWarnings = true;

                // Старые профили без этих флагов считаем RSN-профилями для сохранения прежнего поведения
                if (json.IndexOf("useSourceRevitServer", StringComparison.OrdinalIgnoreCase) < 0)
                    profile.UseSourceRevitServer = true;
                if (json.IndexOf("useTargetRevitServer", StringComparison.OrdinalIgnoreCase) < 0)
                    profile.UseTargetRevitServer = true;

                return Normalize(profile);
            }
            catch
            {
                return CreateDefaultProfile(revitVersion);
            }
        }

        public static CopyFromRsnProfile LoadActiveProfile(string revitVersion)
        {
            EnsureFolders(revitVersion);
            string activePath = Path.Combine(GetConfigRoot(revitVersion), ActiveProfileFileName);
            string activeName = File.Exists(activePath)
                ? File.ReadAllText(activePath).Trim()
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(activeName) && ListProfileNames(revitVersion).Contains(activeName))
                return LoadProfile(revitVersion, activeName);

            var names = ListProfileNames(revitVersion);
            if (names.Count > 0)
                return LoadProfile(revitVersion, names[0]);

            var defaultProfile = CreateDefaultProfile(revitVersion);
            SaveProfile(revitVersion, defaultProfile);
            SetActiveProfile(revitVersion, defaultProfile.Name);
            return defaultProfile;
        }

        public static void SaveProfile(string revitVersion, CopyFromRsnProfile profile)
        {
            EnsureFolders(revitVersion);
            var normalized = Normalize(profile);
            string path = GetProfileFilePath(revitVersion, normalized.Name);
            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static void DeleteProfile(string revitVersion, string profileName)
        {
            string path = GetProfileFilePath(revitVersion, profileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        public static void SetActiveProfile(string revitVersion, string profileName)
        {
            EnsureFolders(revitVersion);
            string activePath = Path.Combine(GetConfigRoot(revitVersion), ActiveProfileFileName);
            File.WriteAllText(activePath, profileName);
        }

        public static CopyFromRsnProfile Normalize(CopyFromRsnProfile profile)
        {
            profile.RunTimes = profile.RunTimes?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct()
                .ToList() ?? new List<string>();

            if (profile.RunTimes.Count == 0)
                profile.RunTimes.Add("08:00");

            profile.WeekDays = profile.WeekDays?.Distinct().ToList() ?? new List<int>();
            if (profile.WeekDays.Count == 0)
                profile.WeekDays.AddRange(new[] { 1, 2, 3, 4, 5 });

            profile.Models = profile.Models ?? new List<ModelCopyEntryData>();
            profile.Links = profile.Links ?? new List<LinkCopyEntryData>();

            if (string.IsNullOrWhiteSpace(profile.LocalDownloadFolder))
                profile.LocalDownloadFolder = @"C:\Temp\";

            if (!profile.LocalDownloadFolder.EndsWith("\\", StringComparison.Ordinal))
                profile.LocalDownloadFolder += "\\";

            if (string.IsNullOrWhiteSpace(profile.TargetServer))
                profile.TargetServer = profile.SourceServer ?? string.Empty;

            return profile;
        }

        public static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
