using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class ApplicationRuntimeTests
{
    [Fact]
    public void NormalAndBackgroundLaunchOptionsAreDistinct()
    {
        var normal = AgentLaunchOptions.Parse([]);
        var startup = AgentLaunchOptions.Parse(["--startup"]);

        Assert.False(normal.StartInBackground);
        Assert.True(startup.StartInBackground);
        Assert.False(startup.ConfigureOnly);
    }

    [Fact]
    public void ConfigurationOptionsRequireExplicitConfigureMode()
    {
        Assert.Throws<ArgumentException>(() => AgentLaunchOptions.Parse(["--enable-sync"]));
        var options = AgentLaunchOptions.Parse([
            "--configure",
            "--enable-sync",
            "--server-url",
            "https://monitor.example.invalid",
            "--enable-startup"
        ]);

        Assert.True(options.ConfigureOnly);
        Assert.True(options.ServerSyncEnabled);
        Assert.True(options.StartupEnabled);
        Assert.Equal("https://monitor.example.invalid", options.ServerBaseUrl);
    }

    [Fact]
    public void CloseHidesWindowWhileExplicitExitEndsTheLifecycle()
    {
        var lifecycle = new AgentLifecycleState();
        Assert.True(lifecycle.TryStartRuntime(startInBackground: false));
        Assert.True(lifecycle.RuntimeStarted);
        Assert.True(lifecycle.WindowVisible);

        lifecycle.HideWindow();
        Assert.True(lifecycle.RuntimeStarted);
        Assert.False(lifecycle.WindowVisible);
        Assert.False(lifecycle.ExitRequested);

        lifecycle.ShowWindow();
        Assert.True(lifecycle.WindowVisible);
        lifecycle.RequestExit();
        Assert.True(lifecycle.ExitRequested);
        Assert.False(lifecycle.WindowVisible);
    }

    [Fact]
    public void BackgroundModeSuppressesInitialWindowButStartsOnlyOneRuntime()
    {
        var lifecycle = new AgentLifecycleState();

        Assert.True(lifecycle.TryStartRuntime(startInBackground: true));
        Assert.True(lifecycle.RuntimeStarted);
        Assert.False(lifecycle.WindowVisible);
        Assert.False(lifecycle.TryStartRuntime(startInBackground: false));
    }

    [Fact]
    public void StartupRegistrationQuotesPathAndIsIdempotent()
    {
        var registry = new MemoryStartupRegistry();
        var manager = new StartupRegistrationManager(registry);
        var executable = Path.Combine(Path.GetTempPath(), "Xugar Pilot", "Xugar.Endpoint.Agent.exe");

        Assert.True(manager.SetEnabled(true, executable));
        Assert.False(manager.SetEnabled(true, executable));
        Assert.Equal($"\"{Path.GetFullPath(executable)}\" --startup", registry.Value);
        Assert.True(manager.IsEnabled(executable));
        Assert.Equal(1, registry.SetCount);

        Assert.True(manager.SetEnabled(false, executable));
        Assert.False(manager.SetEnabled(false, executable));
        Assert.Null(registry.Value);
        Assert.Equal(1, registry.DeleteCount);
    }

    [Fact]
    public void StartupRegistrationRejectsUnsafeRelativeOrQuotedPaths()
    {
        Assert.Throws<ArgumentException>(() => StartupRegistrationManager.BuildCommand("Agent.exe"));
        Assert.Throws<ArgumentException>(() => StartupRegistrationManager.BuildCommand("C:\\Invalid\"Path\\Agent.exe"));
    }

    [Fact]
    public void PerUserInstanceNameIsStableWithoutExposingTheUserName()
    {
        var first = SingleInstanceCoordinator.CreatePerUserName("Xugar.Agent", "DOMAIN", "employee");
        var same = SingleInstanceCoordinator.CreatePerUserName("Xugar.Agent", "domain", "EMPLOYEE");
        var other = SingleInstanceCoordinator.CreatePerUserName("Xugar.Agent", "DOMAIN", "other");

        Assert.Equal(first, same);
        Assert.NotEqual(first, other);
        Assert.DoesNotContain("employee", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecondInstanceIsRejectedAndCanSignalThePrimaryWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = $"Local\\Xugar.Endpoint.Tests.{Guid.NewGuid():N}";
        using var primary = SingleInstanceCoordinator.TryAcquire(name);
        using var duplicate = SingleInstanceCoordinator.TryAcquire(name);

        Assert.True(primary.Acquired);
        Assert.False(duplicate.Acquired);
        Assert.True(duplicate.SignalExistingInstance());
        Assert.True(primary.ActivationRequested.WaitOne(TimeSpan.FromSeconds(1)));
    }

    private sealed class MemoryStartupRegistry : IStartupRegistry
    {
        public string? Value { get; private set; }

        public int SetCount { get; private set; }

        public int DeleteCount { get; private set; }

        public string? GetValue(string valueName) => Value;

        public void SetValue(string valueName, string command)
        {
            Value = command;
            SetCount++;
        }

        public void DeleteValue(string valueName)
        {
            Value = null;
            DeleteCount++;
        }
    }
}
