using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Windows.ApplicationModel;
using System.Runtime.InteropServices;

namespace TigangReminder_App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var majorMinorVersion = Microsoft.WindowsAppSDK.Release.MajorMinor;
        var versionTag = Microsoft.WindowsAppSDK.Release.VersionTag;
        var minVersion = new Microsoft.Windows.ApplicationModel.DynamicDependency.PackageVersion(Microsoft.WindowsAppSDK.Runtime.Version.UInt64);
        var options = Bootstrap.InitializeOptions.OnNoMatch_ShowUI;

        if (!Bootstrap.TryInitialize(majorMinorVersion, versionTag, minVersion, options, out var hr))
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
