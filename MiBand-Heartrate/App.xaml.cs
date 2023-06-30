using Microsoft.Maui.Controls;

namespace MiBand_Heartrate; 

public partial class App : Application {
    public App() {
        InitializeComponent();

        MainPage = new MainWindow();
    }
}