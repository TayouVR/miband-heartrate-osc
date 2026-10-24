using System.ComponentModel;
using System.Text;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;

namespace MiBandHR.Core.Extras {
    public class DeviceHeartrateFileOutput : IDisposable, IAsyncDisposable {
        private readonly Device _device;
        private readonly string _filename;
        private bool _disposed;

        public DeviceHeartrateFileOutput(string filename, Device device) {
            _filename = filename;

            _device = device;

            _device.PropertyChanged += OnDeviceChanged;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _device.PropertyChanged -= OnDeviceChanged;
        }

        public ValueTask DisposeAsync() {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != "Heartrate" ||
                !ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled) return;
            try {
                using var f = File.Open(_filename, FileMode.Create, FileAccess.Write, FileShare.Read);
                byte[] data = Encoding.UTF8.GetBytes(_device.Heartrate.ToString());
                f.Write(data, 0, data.Length);
            } catch (Exception err) {
                Console.WriteLine(err.ToString());
            }
        }
    }
}
