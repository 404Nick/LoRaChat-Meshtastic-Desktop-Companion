using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Desktop BLE provider. The BLE <em>protocol</em> (<see cref="BleMeshBackend"/>) is complete and
/// verified; this is the concrete GATT transport. Cross-desktop GATT from Uno's single Skia binary
/// needs an OS-flavored BLE stack, so a full desktop implementation is a follow-up — on desktop, USB
/// serial is the primary transport, while BLE's native home is the Android head (Phase 4). This
/// surfaces a demo device and a clear message on connect so the UI path is exercisable end-to-end.
/// </summary>
public sealed class DesktopBleProvider : IBleProvider
{
    public Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        IReadOnlyList<BleDeviceInfo> demo = new List<BleDeviceInfo>
        {
            new("DE:MO:00:00:00:01", "Meshtastic_Demo (desktop BLE transport pending)"),
        };
        return Task.FromResult(demo);
    }

    public Task<IBleMeshConnection> ConnectAsync(string address, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Desktop BLE transport isn't wired yet — use a USB serial connection on desktop. " +
            "BLE runs natively on the Android head.");
}
