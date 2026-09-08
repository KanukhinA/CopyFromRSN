using CommunityToolkit.Mvvm.ComponentModel;

namespace SP_CopyFromRSN.Models
{
    /// <summary>Флаг дня недели для расписания.</summary>
    public sealed class WeekDayOption : ObservableObject
    {
        private bool _isSelected;

        public WeekDayOption(int dayOfWeek, string title)
        {
            DayOfWeek = dayOfWeek;
            Title = title;
        }

        public int DayOfWeek { get; }

        public string Title { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
