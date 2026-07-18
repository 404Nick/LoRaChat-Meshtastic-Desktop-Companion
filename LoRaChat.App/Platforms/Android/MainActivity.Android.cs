using System.Collections.Generic;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace LoRaChat.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);

        RequestBluetoothPermissions();
    }

    // Android 12+ requires the BLUETOOTH_SCAN/CONNECT permissions to be granted at runtime (manifest
    // declaration alone isn't enough); older versions use ACCESS_FINE_LOCATION for BLE scanning.
    private void RequestBluetoothPermissions()
    {
        var needed = new List<string>();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            needed.Add(Android.Manifest.Permission.BluetoothScan);
            needed.Add(Android.Manifest.Permission.BluetoothConnect);
        }
        else
        {
            needed.Add(Android.Manifest.Permission.AccessFineLocation);
        }

        var toRequest = needed.FindAll(p => CheckSelfPermission(p) != Permission.Granted);
        if (toRequest.Count > 0)
            RequestPermissions(toRequest.ToArray(), 1001);
    }
}
