#if !WINDOWS
using Tmds.DBus;

namespace MiBandHR.Core.Bluetooth.BlueZ;

static class BlueZErrors {
    public static bool IsInProgress(Exception ex) {
        for (var current = ex; current != null; current = current.InnerException)
            if (current is DBusException { ErrorName: "org.bluez.Error.InProgress" })
                return true;
        return false;
    }
}
#endif