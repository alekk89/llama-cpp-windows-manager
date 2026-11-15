using Microsoft.Win32;

namespace LocalLlmConsole.Tests;

public sealed class SystemThemeChangeMonitorTests
{
    [Fact]
    public void SystemModeReappliesWhenWindowsAppThemeChanges()
    {
        var harness = new Harness("system", systemDark: false);
        harness.Monitor.Observe(false);

        harness.SystemDark = true;
        harness.Raise(UserPreferenceCategory.General);
        Assert.Equal(1, harness.Reapplied);

        harness.SystemDark = false;
        harness.Raise(UserPreferenceCategory.Color);
        Assert.Equal(2, harness.Reapplied);
    }

    [Fact]
    public void UnchangedThemeAndUnrelatedPreferencesDoNotReapply()
    {
        var harness = new Harness("System", systemDark: true);
        harness.Monitor.Observe(true);

        harness.Raise(UserPreferenceCategory.General);
        harness.SystemDark = false;
        harness.Raise(UserPreferenceCategory.Mouse);
        harness.Raise(UserPreferenceCategory.Keyboard);

        Assert.Equal(0, harness.Reapplied);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void ExplicitThemeIgnoresWindowsAppThemeChanges(string mode)
    {
        var harness = new Harness(mode, systemDark: false);
        harness.Monitor.Observe(false);

        harness.SystemDark = true;
        harness.Raise(UserPreferenceCategory.General);

        Assert.Equal(0, harness.Reapplied);
        Assert.Equal(0, harness.Reads);
    }

    [Fact]
    public void SwitchingBackToSystemFollowsTheCurrentWindowsTheme()
    {
        var harness = new Harness("dark", systemDark: false);
        harness.Monitor.Observe(false);
        harness.SystemDark = true;
        harness.Raise(UserPreferenceCategory.General);
        Assert.Equal(0, harness.Reapplied);

        harness.Mode = "system";
        harness.Raise(UserPreferenceCategory.General);

        Assert.Equal(1, harness.Reapplied);
    }

    [Fact]
    public void DisposeUnsubscribesOnceAndStopsReapplying()
    {
        var harness = new Harness("system", systemDark: false);
        harness.Monitor.Observe(false);
        var handler = Assert.Single(harness.Subscribed);

        harness.Monitor.Dispose();
        harness.Monitor.Dispose();
        Assert.Empty(harness.Subscribed);
        Assert.Equal(1, harness.Unsubscribes);

        harness.SystemDark = true;
        handler(null!, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
        Assert.Equal(0, harness.Reapplied);
    }

    private sealed class Harness
    {
        public Harness(string mode, bool systemDark)
        {
            Mode = mode;
            SystemDark = systemDark;
            Monitor = new SystemThemeChangeMonitor(
                () => Mode,
                () =>
                {
                    Reads++;
                    return SystemDark;
                },
                () => Reapplied++,
                handler => Subscribed.Add(handler),
                handler =>
                {
                    Unsubscribes++;
                    Subscribed.Remove(handler);
                });
        }

        public string Mode { get; set; }
        public bool SystemDark { get; set; }
        public int Reapplied { get; private set; }
        public int Reads { get; private set; }
        public int Unsubscribes { get; private set; }
        public List<UserPreferenceChangedEventHandler> Subscribed { get; } = [];
        public SystemThemeChangeMonitor Monitor { get; }

        public void Raise(UserPreferenceCategory category)
        {
            foreach (var handler in Subscribed.ToArray())
                handler(null!, new UserPreferenceChangedEventArgs(category));
        }
    }
}
