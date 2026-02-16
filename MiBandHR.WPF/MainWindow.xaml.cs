using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using MiBand_Heartrate.Extras;

namespace MiBand_Heartrate
{
    public partial class MainWindow : Window
    {
        MainWindowViewModel _model;

        public MainWindow()
        {
            InitializeComponent();

            _model = (MainWindowViewModel)DataContext;

            // Restore window position
            Left = RegistrySetting.Get("WindowLeft", (int)Left);
            Top = RegistrySetting.Get("WindowTop", (int)Top);
            Width = RegistrySetting.Get("WindowWidth", (int)MinWidth);
            Height = RegistrySetting.Get("WindowHeight", (int)MinHeight);

            // Verify if window isn't out of screen
            if (!IsOnScreen())
            {
                CenterWindow();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_model != null)
            {
                _model.Command_Disconnect.Execute(null);
            }

            // Save window size and positions
            RegistrySetting.Set("WindowLeft", (int)Left);
            RegistrySetting.Set("WindowTop", (int)Top);
            RegistrySetting.Set("WindowWidth", (int)Width);
            RegistrySetting.Set("WindowHeight", (int)Height);
        }

        bool IsOnScreen()
        {
            var rect = new Rectangle((int)Left, (int)Top, (int)Width, (int)Height);
            return Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect));
        }

        void CenterWindow()
        {
            Left = (SystemParameters.PrimaryScreenWidth / 2) - (Width / 2);
            Top = (SystemParameters.PrimaryScreenHeight / 2) - (Height / 2);
        }
    }
}
