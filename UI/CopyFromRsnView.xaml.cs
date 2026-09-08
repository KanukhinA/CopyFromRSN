using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace SP_CopyFromRSN.UI
{
    public partial class CopyFromRsnView : Window
    {
        private const int WmNcHitTest = 0x0084;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const double ResizeBorderThickness = 6;

        private readonly CopyFromRsnViewModel _viewModel;

        public CopyFromRsnView(CopyFromRsnViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            Closed += (_, __) => viewModel.Detach();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WndProc);
        }

        // Рамка для изменения размера у окна без стандартной рамки Windows
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmNcHitTest)
                return IntPtr.Zero;

            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            Point clientPoint = PointFromScreen(new Point(x, y));

            if (clientPoint.X < 0 || clientPoint.Y < 0
                || clientPoint.X > ActualWidth || clientPoint.Y > ActualHeight)
                return IntPtr.Zero;

            bool onLeft = clientPoint.X <= ResizeBorderThickness;
            bool onRight = clientPoint.X >= ActualWidth - ResizeBorderThickness;
            bool onBottom = clientPoint.Y >= ActualHeight - ResizeBorderThickness;

            if (onBottom && onLeft) { handled = true; return (IntPtr)HtBottomLeft; }
            if (onBottom && onRight) { handled = true; return (IntPtr)HtBottomRight; }
            if (onBottom) { handled = true; return (IntPtr)HtBottom; }
            if (onLeft) { handled = true; return (IntPtr)HtLeft; }
            if (onRight) { handled = true; return (IntPtr)HtRight; }

            return IntPtr.Zero;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Загружаем профиль только при выборе из списка, а не при ручном вводе имени
            if (_viewModel.SuppressProfileSelectionLoad)
                return;

            if (sender is not ComboBox combo || combo.SelectedItem is not string selectedName)
                return;

            if (string.Equals(selectedName, _viewModel.LoadedProfileName, StringComparison.Ordinal))
                return;

            _viewModel.LoadProfile(selectedName);
        }
    }
}
