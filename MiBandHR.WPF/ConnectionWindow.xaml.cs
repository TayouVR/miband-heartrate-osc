using System.Windows;

namespace MiBand_Heartrate
{
    /// <summary>
    /// Logique d'interaction pour ConnectionWindow.xaml
    /// </summary>
    public partial class ConnectionWindow : Window
    {
        ConnectionWindowViewModel _model;

        public ConnectionWindow(MainWindowViewModel main)
        {
            InitializeComponent();

            _model = (ConnectionWindowViewModel)DataContext;

            if (_model != null)
            {
                _model.View = this;
                _model.Main = main;
            }
        }
    }
}
