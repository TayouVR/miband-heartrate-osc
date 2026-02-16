using System.Windows;
using MiBandHR.Core.Configuration;

namespace MiBand_Heartrate
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ConfigurationManager.Initialize();
        }
    }
}
