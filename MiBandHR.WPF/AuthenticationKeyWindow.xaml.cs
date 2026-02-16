using System.Windows;

namespace MiBand_Heartrate
{
    /// <summary>
    /// Logique d'interaction pour AuthenticationKeyWindow.xaml
    /// </summary>
    public partial class AuthenticationKeyWindow : Window
    {
        public string AuthenticationKeyResult { get; set; } = "";

        public AuthenticationKeyWindow()
        {
            InitializeComponent();

            AuthenticationKeyViewModel model = (AuthenticationKeyViewModel)DataContext;
            model.View = this;
        }
    }
}
