using System.Collections.Generic;

namespace SP_CopyFromRSN.Models
{
    /// <summary>Профиль переноса моделей с RSN.</summary>
    public sealed class CopyFromRsnProfile
    {
        public string Name { get; set; } = "По умолчанию";

        public string SourceServer { get; set; } = string.Empty;

        /// <summary>Использовать поле исходного сервера для относительных путей на RSN.</summary>
        public bool UseSourceRevitServer { get; set; } = true;

        public string TargetServer { get; set; } = string.Empty;

        /// <summary>Использовать поле целевого сервера для относительных путей на RSN.</summary>
        public bool UseTargetRevitServer { get; set; } = true;

        public string LocalDownloadFolder { get; set; } = @"C:\Temp\";

        public string RevitServerToolPath { get; set; } = string.Empty;

        public bool RepathLinks { get; set; }

        /// <summary>Подавлять диалоги и предупреждения при открытии моделей (апдейтеры и др.).</summary>
        public bool SuppressOpenWarnings { get; set; } = true;

        public bool ScheduleEnabled { get; set; }

        public List<string> RunTimes { get; set; } = new List<string> { "08:00", "14:46" };

        /// <summary>Дни недели: 0=Вс, 1=Пн … 6=Сб (DayOfWeek).</summary>
        public List<int> WeekDays { get; set; } = new List<int> { 1, 2, 3, 4, 5 };

        public List<ModelCopyEntryData> Models { get; set; } = new List<ModelCopyEntryData>();

        public List<LinkCopyEntryData> Links { get; set; } = new List<LinkCopyEntryData>();

        public CopyFromRsnProfile Clone()
        {
            return new CopyFromRsnProfile
            {
                Name = Name,
                SourceServer = SourceServer,
                UseSourceRevitServer = UseSourceRevitServer,
                TargetServer = TargetServer,
                UseTargetRevitServer = UseTargetRevitServer,
                LocalDownloadFolder = LocalDownloadFolder,
                RevitServerToolPath = RevitServerToolPath,
                RepathLinks = RepathLinks,
                SuppressOpenWarnings = SuppressOpenWarnings,
                ScheduleEnabled = ScheduleEnabled,
                RunTimes = new List<string>(RunTimes),
                WeekDays = new List<int>(WeekDays),
                Models = Models.ConvertAll(m => m.Clone()),
                Links = Links.ConvertAll(l => l.Clone())
            };
        }
    }

    public sealed class ModelCopyEntryData
    {
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;

        public ModelCopyEntryData Clone() =>
            new ModelCopyEntryData { SourcePath = SourcePath, TargetPath = TargetPath };
    }

    public sealed class LinkCopyEntryData
    {
        public string ModelName { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;

        public LinkCopyEntryData Clone() =>
            new LinkCopyEntryData
            {
                ModelName = ModelName,
                OldPath = OldPath,
                NewPath = NewPath
            };
    }
}
