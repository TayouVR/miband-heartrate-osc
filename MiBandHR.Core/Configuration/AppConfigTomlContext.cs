using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace MiBandHR.Core.Configuration;

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    Converters = [typeof(BluetoothBackendTomlConverter)])]
[TomlSerializable(typeof(AppConfig))]
internal partial class AppConfigTomlContext : TomlSerializerContext
{
}