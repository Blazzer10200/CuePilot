namespace CuePilot.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cuepilot-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsFile => Path.Combine(directory, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithoutBackup()
    {
        var loaded = SettingsStore.Load(SettingsFile);

        Assert.Equal("F10", loaded.StartStop.Key);
        Assert.False(File.Exists(SettingsFile + ".bak"));
    }

    [Theory]
    [InlineData("""{"formatVersion":"nine"}""")]
    [InlineData("""{"formatVersion":9,"routine":{"inputMode":"Bogus"}}""")]
    [InlineData("""{"formatVersion":0}""")]
    [InlineData("not json at all")]
    public void Load_UnusableFile_BacksUpAndReturnsDefaults(string content)
    {
        File.WriteAllText(SettingsFile, content);

        var loaded = SettingsStore.Load(SettingsFile);

        Assert.Equal("F10", loaded.StartStop.Key);
        Assert.Equal(content, File.ReadAllText(SettingsFile + ".bak"));
    }

    [Fact]
    public void Load_NewerFormatVersion_IsNotOverwritten()
    {
        const string newer = """{"formatVersion":10,"futureField":true}""";
        File.WriteAllText(SettingsFile, newer);

        var loaded = SettingsStore.Load(SettingsFile);

        Assert.Equal("F10", loaded.StartStop.Key);
        Assert.Equal(newer, File.ReadAllText(SettingsFile + ".bak"));
        Assert.Throws<InvalidOperationException>(() => SettingsStore.Save(loaded, SettingsFile));
        Assert.Equal(newer, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAndLeavesNoTemporaryFile()
    {
        var settings = AppSettings.Defaults();
        settings.StartStop = new HotkeyBinding { Key = "F9" };

        SettingsStore.Save(settings, SettingsFile);
        var loaded = SettingsStore.Load(SettingsFile);

        Assert.Equal("F9", loaded.StartStop.Key);
        Assert.False(File.Exists(SettingsFile + ".tmp"));
        Assert.False(File.Exists(SettingsFile + ".bak"));
    }
}
