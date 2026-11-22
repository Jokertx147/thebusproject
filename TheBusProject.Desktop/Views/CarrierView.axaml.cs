using System;
using Avalonia.Controls;
using Avalonia.Threading;
using TheBusProject.Presentation;

namespace TheBusProject.Desktop.Views
{
    public partial class CarrierView : UserControl
    {
        private readonly DispatcherTimer _timer;

        public CarrierView()
        {
            InitializeComponent();

            // the game clock runs on another thread, so the screen polls for changes
            _timer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(500)};
            _timer.Tick += (s, e) => (DataContext as CarrierViewModel)?.Refresh();
            AttachedToVisualTree += (s, e) => _timer.Start();
            DetachedFromVisualTree += (s, e) => _timer.Stop();
        }
    }
}
