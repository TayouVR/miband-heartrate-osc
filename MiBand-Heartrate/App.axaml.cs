using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MiBand_Heartrate.ViewModels;
using MiBand_Heartrate.Views;

namespace MiBand_Heartrate
{
    /// <summary>
    /// Logique d'interaction pour App.xaml
    /// </summary>
    public partial class App : Application {
        public static App Current { get; internal set; }
        public int MaxHeartrate { get; set; }
        public int MinHeartrate { get; set; }
        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);
        }

        public App() {
            Current = this;
        }

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new MainWindow {
                    DataContext = new MainWindowViewModel()
                };
            } else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
                singleViewPlatform.MainView = new MainWindow {
                    DataContext = new MainWindowViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
