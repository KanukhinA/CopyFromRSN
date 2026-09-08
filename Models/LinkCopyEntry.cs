using CommunityToolkit.Mvvm.ComponentModel;

namespace SP_CopyFromRSN.Models
{
    /// <summary>Строка таблицы связей в UI.</summary>
    public sealed class LinkCopyEntry : ObservableObject
    {
        private string _modelName = string.Empty;
        private string _oldPath = string.Empty;
        private string _newPath = string.Empty;

        public string ModelName
        {
            get => _modelName;
            set => SetProperty(ref _modelName, value);
        }

        public string OldPath
        {
            get => _oldPath;
            set => SetProperty(ref _oldPath, value);
        }

        public string NewPath
        {
            get => _newPath;
            set => SetProperty(ref _newPath, value);
        }

        public LinkCopyEntryData ToData() =>
            new LinkCopyEntryData
            {
                ModelName = ModelName,
                OldPath = OldPath,
                NewPath = NewPath
            };

        public static LinkCopyEntry FromData(LinkCopyEntryData data) =>
            new LinkCopyEntry
            {
                ModelName = data.ModelName,
                OldPath = data.OldPath,
                NewPath = data.NewPath
            };
    }
}
