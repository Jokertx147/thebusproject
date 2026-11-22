using Avalonia.Controls;

namespace TheBusProject.Desktop
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
            UiScale.Attach(this, this.FindControl<LayoutTransformControl>("ZoomRoot"));
            PageScroll.Attach(this);
        }
    }
}
