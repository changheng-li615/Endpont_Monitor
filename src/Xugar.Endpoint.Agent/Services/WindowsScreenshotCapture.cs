using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent.Services;

public sealed class WindowsScreenshotCapture : IScreenshotCapture
{
    private const int SourceCopy = 0x00CC0020;
    private const int CaptureLayeredWindows = 0x40000000;
    private static readonly IntPtr GdiError = new(-1);

    public Task<IReadOnlyList<ScreenshotMetadata>> CaptureAsync(
        string dataRoot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        var safeRoot = StoragePaths.ResolveDataRoot(dataRoot);
        return Task.Run(
            () => Capture(safeRoot, capturedAtUtc, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<ScreenshotMetadata> Capture(
        string dataRoot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!DesktopSessionState.IsNormalInteractiveDesktop())
        {
            return Array.Empty<ScreenshotMetadata>();
        }

        var monitors = EnumerateMonitors();
        var screenshotDirectory = StoragePaths.GetScreenshotDirectory(dataRoot, capturedAtUtc);
        Directory.CreateDirectory(screenshotDirectory);

        var screenshots = new List<ScreenshotMetadata>(monitors.Count);
        Exception? lastFailure = null;

        for (var index = 0; index < monitors.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var monitorNumber = index + 1;
            var bounds = monitors[index];
            var fileName = ScreenshotFileName.Create(capturedAtUtc, monitorNumber);
            var filePath = StoragePaths.EnsureUnderRoot(
                dataRoot,
                Path.Combine(screenshotDirectory, fileName));

            try
            {
                CaptureMonitorToPng(bounds, filePath);
                screenshots.Add(new ScreenshotMetadata(
                    capturedAtUtc,
                    monitorNumber,
                    filePath,
                    bounds.Width,
                    bounds.Height));
            }
            catch (Exception exception) when (exception is Win32Exception or IOException)
            {
                lastFailure = exception;
            }
        }

        if (screenshots.Count == 0 && lastFailure is not null)
        {
            throw new InvalidOperationException("No monitor screenshot could be captured.", lastFailure);
        }

        return screenshots;
    }

    private static IReadOnlyList<MonitorBounds> EnumerateMonitors()
    {
        var monitors = new List<MonitorBounds>();
        MonitorEnumProcedure callback = (
            IntPtr _,
            IntPtr _,
            ref NativeRectangle rectangle,
            IntPtr _) =>
        {
            if (rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top)
            {
                monitors.Add(new MonitorBounds(
                    rectangle.Left,
                    rectangle.Top,
                    rectangle.Right - rectangle.Left,
                    rectangle.Bottom - rectangle.Top));
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate display monitors.");
        }

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Windows reported no display monitors.");
        }

        return monitors;
    }

    private static void CaptureMonitorToPng(MonitorBounds bounds, string filePath)
    {
        var screenDeviceContext = GetDC(IntPtr.Zero);
        if (screenDeviceContext == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not access the desktop device context.");
        }

        IntPtr memoryDeviceContext = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;

        try
        {
            memoryDeviceContext = CreateCompatibleDC(screenDeviceContext);
            if (memoryDeviceContext == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a memory device context.");
            }

            bitmap = CreateCompatibleBitmap(screenDeviceContext, bounds.Width, bounds.Height);
            if (bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a screenshot bitmap.");
            }

            previousObject = SelectObject(memoryDeviceContext, bitmap);
            if (previousObject == IntPtr.Zero || previousObject == GdiError)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not select the screenshot bitmap.");
            }

            if (!BitBlt(
                    memoryDeviceContext,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDeviceContext,
                    bounds.Left,
                    bounds.Top,
                    SourceCopy | CaptureLayeredWindows))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not copy desktop pixels.");
            }

            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();

            try
            {
                using var output = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(output);
            }
            catch
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException)
                {
                    // Keep the original capture error; retention can handle an incomplete file later.
                }

                throw;
            }
        }
        finally
        {
            if (previousObject != IntPtr.Zero && previousObject != GdiError && memoryDeviceContext != IntPtr.Zero)
            {
                _ = SelectObject(memoryDeviceContext, previousObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDeviceContext != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDeviceContext);
            }

            _ = ReleaseDC(IntPtr.Zero, screenDeviceContext);
        }
    }

    private sealed record MonitorBounds(int Left, int Top, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool MonitorEnumProcedure(
        IntPtr monitor,
        IntPtr monitorDeviceContext,
        ref NativeRectangle monitorRectangle,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clippingRectangle,
        MonitorEnumProcedure callback,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDeviceContext,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDeviceContext,
        int sourceX,
        int sourceY,
        int rasterOperation);
}
