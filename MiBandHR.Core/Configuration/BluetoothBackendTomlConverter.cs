using MiBandHR.Core.Bluetooth;
using Tomlyn;
using Tomlyn.Serialization;

namespace MiBandHR.Core.Configuration;

public sealed class BluetoothBackendTomlConverter : TomlConverter<BluetoothBackend>
{
    public override BluetoothBackend Read(TomlReader reader)
        => Enum.Parse<BluetoothBackend>(reader.GetString());

    public override void Write(TomlWriter writer, BluetoothBackend value)
        => writer.WriteStringValue(value.ToString());
}
