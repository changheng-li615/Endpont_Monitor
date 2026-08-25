using System.Runtime.InteropServices;
using System.Text;

namespace Xugar.Endpoint.Agent.Services;

internal static class DesktopSessionState
{
    private const uint DesktopReadObjects = 0x0001;
    private const int UserObjectName = 2;

    public static bool IsNormalInteractiveDesktop()
    {
        if (!Environment.UserInteractive)
        {
            return false;
        }

        var desktop = OpenInputDesktop(
            flags: 0,
            inherit: false,
            desiredAccess: DesktopReadObjects);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var desktopName = new StringBuilder(capacity: 256);
            return GetUserObjectInformation(
                       desktop,
                       UserObjectName,
                       desktopName,
                       desktopName.Capacity * sizeof(char),
                       out _)
                   && desktopName.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        IntPtr handle,
        int index,
        StringBuilder information,
        int length,
        out int lengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
