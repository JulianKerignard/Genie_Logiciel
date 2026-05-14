using System.Text.Json;
using EasySave.Models;
using EasySave.Services;
using EasySave.UI.Services;
using EasySave.UI.ViewModels;

namespace EasySave.Tests.V2;

// V3.1 — Settings UI exposes the large-file threshold in MB. Validates:
//  - Save rejects out-of-range values without touching disk
//  - Save persists in-range values as KB
//  - Save preserves non-GUI fields (regression for the latent
//    Save-wipes-fields bug surfaced while implementing this feature)
//  - Save pushes the new threshold to the live BigFileGate
[Collection("AppConfigMutation")]
public sealed class SettingsViewModelLargeFileThresholdTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFile;

    public SettingsViewModelLargeFileThresholdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "settings-vm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsFile = Path.Combine(_tempDir, "settings.json");

        // Point AppConfig.SettingsFilePath at the temp file so SettingsRepository.Instance
        // reads/writes there. Same pattern as SchedulerServiceLockPropagationTests.
        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { SettingsFilePath = _settingsFile };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));
        AppConfig.Load(configPath);

        // TranslationSource is uninitialized in test runs (no GUI bootstrap),
        // so its indexer returns the key verbatim. Tests assert on the key
        // text rather than the translated string.
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteSettingsOnDisk(AppSettings s) =>
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(s));

    private AppSettings ReadSettingsFromDisk() =>
        JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile))!;

    [Fact]
    public void Save_RejectsThresholdBelow64Kb_WithoutTouchingDisk()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);
        var diskBefore = File.ReadAllText(_settingsFile);

        // 0.05 MB = 51 KB → below the 64 KB floor.
        vm.LargeFileThresholdValue = 0.05;
        vm.SaveCommand.Execute(null);

        Assert.Equal(diskBefore, File.ReadAllText(_settingsFile));
        Assert.Equal("settings.large_file.invalid", vm.SaveConfirmation);
    }

    [Fact]
    public void Save_RejectsThresholdAbove10Gb_WithoutTouchingDisk()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);
        var diskBefore = File.ReadAllText(_settingsFile);

        // 20480 MB = 20 GB → above the 10 GB ceiling.
        vm.LargeFileThresholdValue = 20480;
        vm.SaveCommand.Execute(null);

        Assert.Equal(diskBefore, File.ReadAllText(_settingsFile));
        Assert.Equal("settings.large_file.invalid", vm.SaveConfirmation);
    }

    [Fact]
    public void Save_PersistsValidThresholdInKb()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        // 8 MB → 8192 KB on disk.
        vm.LargeFileThresholdValue = 8;
        vm.SaveCommand.Execute(null);

        Assert.Equal(8192, ReadSettingsFromDisk().LargeFileThresholdKb);
    }

    [Fact]
    public void Save_PreservesMaxParallelJobsAndRemoteConsoleAndLogModeSettings()
    {
        // Regression guard: the original Save() built a fresh AppSettings with
        // only Language and CryptoSoft.TimeoutMs preserved — every other field
        // got silently reset to defaults (MaxParallelJobs → 4, RemoteConsole*
        // → defaults, LogMode → Local, PriorityExtensions → []). After this
        // feature lands, all fields not surfaced by the GUI must round-trip.
        var preserved = new AppSettings
        {
            EncryptedExtensions = new[] { ".pdf" },
            BusinessSoftware = new[] { "calc.exe" },
            Language = "fr",
            LogFormat = "xml",
            CryptoSoft = new CryptoSoftSettings { Path = "/tmp/cs", TimeoutMs = 12345 },
            LargeFileThresholdKb = 4096,
            RemoteConsoleEnabled = true,
            RemoteConsolePort = 9876,
            MaxParallelJobs = 7,
            RemoteConsoleTlsEnabled = true,
            LogMode = EasyLog.LogMode.Both,
            LogCentralizedEndpoint = "http://collector.local/logs",
            PriorityExtensions = new[] { ".docx", ".xlsx" },
        };
        WriteSettingsOnDisk(preserved);

        var vm = new SettingsViewModel(SettingsRepository.Instance);
        vm.LargeFileThresholdValue = 16; // 16 MB → 16384 KB
        vm.SaveCommand.Execute(null);

        var after = ReadSettingsFromDisk();
        Assert.Equal(16384, after.LargeFileThresholdKb);
        Assert.True(after.RemoteConsoleEnabled);
        Assert.Equal(9876, after.RemoteConsolePort);
        Assert.Equal(7, after.MaxParallelJobs);
        Assert.True(after.RemoteConsoleTlsEnabled);
        Assert.Equal(EasyLog.LogMode.Both, after.LogMode);
        Assert.Equal("http://collector.local/logs", after.LogCentralizedEndpoint);
        Assert.Equal(new[] { ".docx", ".xlsx" }, after.PriorityExtensions);
    }

    [Fact]
    public void Save_PushesNewThresholdToLiveGate()
    {
        // V3.1 hot-reload: the VM resolves IBigFileGate from DI (or via the
        // test seam) and calls SetThreshold after a successful repository
        // write so the engine picks up the new value without a restart.
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var fakeGate = new RecordingGate();
        var vm = new SettingsViewModel(SettingsRepository.Instance, fakeGate);

        vm.LargeFileThresholdValue = 2; // 2 MB = 2048 KB = 2_097_152 bytes
        vm.SaveCommand.Execute(null);

        Assert.Equal(2L * 1024 * 1024, fakeGate.LastSetThresholdBytes);
    }

    [Fact]
    public void Save_RejectedValue_DoesNotPushToGate()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var fakeGate = new RecordingGate();
        var vm = new SettingsViewModel(SettingsRepository.Instance, fakeGate);

        vm.LargeFileThresholdValue = 0.001; // ~1 KB → below floor
        vm.SaveCommand.Execute(null);

        Assert.Null(fakeGate.LastSetThresholdBytes);
    }

    [Fact]
    public void Load_PicksMb_WhenStoredKbIsAMultipleOf1024()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        Assert.Equal("MB", vm.LargeFileThresholdUnit);
        Assert.Equal(4.0, vm.LargeFileThresholdValue);
    }

    [Fact]
    public void Load_PicksGb_WhenStoredKbIsAMultipleOf1Mi()
    {
        // 2 GB = 2 * 1024 * 1024 KB = 2 097 152 KB
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 2 * 1024 * 1024 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        Assert.Equal("GB", vm.LargeFileThresholdUnit);
        Assert.Equal(2.0, vm.LargeFileThresholdValue);
    }

    [Fact]
    public void Load_PicksKb_WhenStoredKbDoesNotDivideEvenly()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 100 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        Assert.Equal("KB", vm.LargeFileThresholdUnit);
        Assert.Equal(100, vm.LargeFileThresholdValue);
    }

    [Fact]
    public void ChangingUnit_RescalesValue_PreservingUnderlyingByteCount()
    {
        // 4 MB → switch to KB → 4096 KB (same physical size, different unit).
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);
        Assert.Equal("MB", vm.LargeFileThresholdUnit);
        Assert.Equal(4.0, vm.LargeFileThresholdValue);

        vm.LargeFileThresholdUnit = "KB";
        Assert.Equal(4096.0, vm.LargeFileThresholdValue);

        vm.LargeFileThresholdUnit = "GB";
        Assert.Equal(4096.0 / (1024.0 * 1024.0), vm.LargeFileThresholdValue, precision: 10);
    }

    [Fact]
    public void Save_HonorsSelectedUnit_KbDirectMapping()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        vm.LargeFileThresholdUnit = "KB";
        vm.LargeFileThresholdValue = 512;
        vm.SaveCommand.Execute(null);

        Assert.Equal(512, ReadSettingsFromDisk().LargeFileThresholdKb);
    }

    [Fact]
    public void Save_OutOfRangeValue_FlagsSaveIsError()
    {
        // Regression guard: validation banner used to be shown in success-green
        // because SaveIsError didn't exist. The XAML now branches on it; the
        // ViewModel must set it true on every error path and false on success.
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);
        Assert.False(vm.SaveIsError);

        vm.LargeFileThresholdValue = 0.001; // 0.001 MB ≈ 1 KB → below floor
        vm.SaveCommand.Execute(null);

        Assert.True(vm.SaveIsError);
        Assert.True(vm.ShowSaveErrorMessage);
        Assert.False(vm.ShowSaveSuccessMessage);
    }

    [Fact]
    public void Save_ValidValue_ClearsSaveIsError()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        // First, trip the error state.
        vm.LargeFileThresholdValue = 0.001;
        vm.SaveCommand.Execute(null);
        Assert.True(vm.SaveIsError);

        // Then a valid save must clear it.
        vm.LargeFileThresholdValue = 8;
        vm.SaveCommand.Execute(null);

        Assert.False(vm.SaveIsError);
        Assert.True(vm.ShowSaveSuccessMessage);
        Assert.False(vm.ShowSaveErrorMessage);
    }

    [Fact]
    public void Save_HonorsSelectedUnit_GbConversion()
    {
        WriteSettingsOnDisk(new AppSettings { LargeFileThresholdKb = 4096 });
        var vm = new SettingsViewModel(SettingsRepository.Instance);

        vm.LargeFileThresholdUnit = "GB";
        vm.LargeFileThresholdValue = 1; // 1 GB = 1 048 576 KB
        vm.SaveCommand.Execute(null);

        Assert.Equal(1024 * 1024, ReadSettingsFromDisk().LargeFileThresholdKb);
    }

    private sealed class RecordingGate : EasySave.Services.IBigFileGate
    {
        public long? LastSetThresholdBytes { get; private set; }
        public long LargeFileThresholdBytes => LastSetThresholdBytes ?? 0;
        public Task<IDisposable> AcquireAsync(long fileSizeBytes, CancellationToken ct) =>
            throw new NotImplementedException();
        public void SetThreshold(long largeFileThresholdBytes) =>
            LastSetThresholdBytes = largeFileThresholdBytes;
    }
}
