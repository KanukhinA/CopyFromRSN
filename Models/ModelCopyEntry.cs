using CommunityToolkit.Mvvm.ComponentModel;

namespace SP_CopyFromRSN.Models
{
    /// <summary>Строка таблицы моделей в UI.</summary>
    public sealed class ModelCopyEntry : ObservableObject
    {
        private string _sourcePath = string.Empty;
        private string _targetPath = string.Empty;

        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        public string TargetPath
        {
            get => _targetPath;
            set => SetProperty(ref _targetPath, value);
        }

        public ModelCopyEntryData ToData() =>
            new ModelCopyEntryData { SourcePath = SourcePath, TargetPath = TargetPath };

        public static ModelCopyEntry FromData(ModelCopyEntryData data) =>
            new ModelCopyEntry { SourcePath = data.SourcePath, TargetPath = data.TargetPath };
    }
}
