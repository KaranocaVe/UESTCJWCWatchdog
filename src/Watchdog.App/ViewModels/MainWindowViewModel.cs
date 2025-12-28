using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using Watchdog.App.Models;
using Watchdog.Core.Eams;
using Watchdog.Core.Notifications;
using Watchdog.Core.Storage;

namespace Watchdog.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string SettingsPath = Path.Combine(AppPaths.GetAppDataDir(), "settings.json");

    private bool _initialized;
    private bool _restoringSettings;
    private readonly SemaphoreSlim _persistSettingsLock = new(1, 1);
    private readonly JsonStateStore<AppSettings> _settingsStore = new(SettingsPath, WatchdogAppJsonContext.Default.AppSettings);

    [ObservableProperty]
    private bool _isBusy;

    public bool CanInteract => !IsBusy;

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private string _account = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _savePassword;

    [ObservableProperty]
    private bool _headless = true;

    [ObservableProperty]
    private bool _useAutoSemester = true;

    public bool CanChooseSemester => CanInteract && !UseAutoSemester;
    public bool CanChangeAutoRefreshInterval => CanInteract && AutoRefreshEnabled;

    public ObservableCollection<string> AvailableSemesterYears { get; } = new();

    [ObservableProperty]
    private string _semesterYear = string.Empty;

    public ObservableCollection<string> AvailableSemesterTerms { get; } =
    [
        "第一学期",
        "第二学期",
    ];

    [ObservableProperty]
    private int _semesterTermIndex;

    public string EffectiveSemesterText =>
        UseAutoSemester
            ? $"自动（当前：{SemesterIdCalculator.GetAcademicTermDisplay(DateTimeOffset.Now)}）"
            : $"{SemesterYear} {AvailableSemesterTerms[Math.Clamp(SemesterTermIndex, 0, AvailableSemesterTerms.Count - 1)]}";

    public ObservableCollection<string> AutoRefreshIntervalOptions { get; } =
    [
        "30 分钟",
        "60 分钟",
        "120 分钟",
    ];

    [ObservableProperty]
    private bool _autoRefreshEnabled;

    [ObservableProperty]
    private int _autoRefreshIntervalIndex = 1;

    [ObservableProperty]
    private string _nextAutoRefreshText = "已关闭";

    [ObservableProperty]
    private bool _ntfyEnabled = true;

    [ObservableProperty]
    private string _ntfyServerBaseUrl = "https://ntfy.sh";

    [ObservableProperty]
    private string _ntfyTopic = string.Empty;

    public string NtfySubscribeUrl =>
        string.IsNullOrWhiteSpace(NtfyTopic) ? string.Empty : $"{NtfyServerBaseUrl.TrimEnd('/')}/{NtfyTopic}";

    [ObservableProperty]
    private string _semesterId = string.Empty;

    [ObservableProperty]
    private string _channel = "chrome";

    [ObservableProperty]
    private int _browserOptionIndex;

    [ObservableProperty]
    private string _profileDir = string.Empty;

    [ObservableProperty]
    private string _lastFetchedAtText = "未查询";

    public ObservableCollection<FinalGrade> FinalGrades { get; } = new();
    public ObservableCollection<UsualGrade> UsualGrades { get; } = new();

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        _restoringSettings = true;

        AppSettings? settings = null;
        try
        {
            settings = await _settingsStore.LoadAsync();
        }
        catch (Exception ex)
        {
            Status = $"读取 settings.json 失败：{ex.Message}";
        }

        InitializeSemesterChoices();

        if (settings is not null)
        {
            Account = settings.Account ?? string.Empty;
            Password = settings.Password ?? string.Empty;
            SavePassword = settings.SavePassword;
            Headless = settings.Headless;
            AutoRefreshEnabled = settings.AutoRefreshEnabled;
            AutoRefreshIntervalIndex = NormalizeAutoRefreshIntervalIndex(settings.AutoRefreshMinutes);
            NtfyEnabled = settings.NtfyEnabled;
            NtfyServerBaseUrl = string.IsNullOrWhiteSpace(settings.NtfyServerBaseUrl) ? "https://ntfy.sh" : settings.NtfyServerBaseUrl.Trim();
            NtfyTopic = settings.NtfyTopic ?? string.Empty;

            UseAutoSemester = settings.UseAutoSemester ?? string.IsNullOrWhiteSpace(settings.SemesterId);

            SemesterYear = ResolveSemesterYear(settings);
            SemesterTermIndex = ResolveSemesterTermIndex(settings);
            SemesterId = settings.SemesterId ?? string.Empty;
            Channel = string.IsNullOrWhiteSpace(settings.Channel) ? "chrome" : settings.Channel;
        }
        else
        {
            UseAutoSemester = true;
            var (currentStartYear, currentTerm) = SemesterIdCalculator.GetAcademicTerm(DateTimeOffset.Now);
            SemesterYear = SemesterIdCalculator.ToYearRange(currentStartYear);
            SemesterTermIndex = currentTerm == SemesterTerm.Second ? 1 : 0;
        }

        _restoringSettings = false;
        SyncBrowserOptionFromChannel();
        UpdateProfileDir();
        await LoadProfileStateAsync();
        await EnsureNtfyTopicAsync(persistIfGenerated: true);
        UpdateAutoRefreshTimer();
    }

    partial void OnAccountChanged(string value)
    {
        UpdateProfileDir();
    }

    [RelayCommand]
    private async Task ImportFromEnvAsync()
    {
        var envPath = Path.Combine(Environment.CurrentDirectory, ".env");
        if (!File.Exists(envPath))
        {
            Status = "未找到 .env（当前工作目录）";
            return;
        }

        var env = Watchdog.Core.DotEnv.Load(envPath);
        Account = Watchdog.Core.DotEnv.GetRequired(env, "account");
        Password = Watchdog.Core.DotEnv.GetRequired(env, "password");
        Status = "已从 .env 导入（未自动保存）";
        UpdateProfileDir();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            await EnsureNtfyTopicAsync(persistIfGenerated: false);
            await PersistSettingsAsync(updateStatus: true);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private Task QueryGradesAsync()
    {
        return RunQueryAsync(isAutoRefresh: false);
    }

    private async Task RunQueryAsync(bool isAutoRefresh)
    {
        if (string.IsNullOrWhiteSpace(Account))
        {
            Status = "请先在“设置”里填写学号";
            return;
        }

        if (IsBusy)
            return;

        string? semesterIdOverride = null;
        if (!UseAutoSemester)
        {
            if (!SemesterIdCalculator.TryParseYearRange(SemesterYear, out var startYear, out _))
            {
                Status = "请选择正确的学年范围（例如：2024-2025）";
                return;
            }

            var term = SemesterTermIndex == 1 ? SemesterTerm.Second : SemesterTerm.First;
            semesterIdOverride = SemesterIdCalculator.Calculate(startYear, term);
        }

        var oldFinal = FinalGrades.ToList();
        var oldUsual = UsualGrades.ToList();

        IsBusy = true;
        Status = isAutoRefresh ? "自动刷新中…" : "查询中…";

        try
        {
            var snapshot = await Task.Run(async () =>
            {
                var profileDir = AppPaths.GetProfileDir(Account);
                var userDataDir = Path.Combine(profileDir, "user-data");
                Directory.CreateDirectory(userDataDir);

                var options = new EamsClientOptions
                {
                    UserDataDir = userDataDir,
                    Headless = Headless,
                    Channel = string.IsNullOrWhiteSpace(Channel) ? "chrome" : Channel.Trim(),
                };

                await using var client = new EamsClient(options);
                var snapshot = await client.GetGradesSnapshotAsync(
                    new EamsCredentials(Account.Trim(), Password ?? string.Empty),
                    semesterId: semesterIdOverride);

                // Persist snapshot for offline view / next launch.
                var statePath = Path.Combine(profileDir, "state.json");
                var storageStatePath = Path.Combine(profileDir, "storage-state.json");
                var stateStore = new JsonStateStore<ProfileState>(statePath, WatchdogCoreJsonContext.Default.ProfileState);
                var state = await stateStore.LoadAsync() ?? new ProfileState { Account = Account.Trim() };
                state.SemesterId = snapshot.SemesterId;
                state.LastFetchedAt = snapshot.FetchedAt;
                state.FinalGrades = snapshot.FinalGrades.ToList();
                state.UsualGrades = snapshot.UsualGrades.ToList();
                await stateStore.SaveAsync(state);

                await client.SaveStorageStateAsync(storageStatePath);

                return snapshot;
            });

            var diff = ComputeDiff(oldFinal, snapshot.FinalGrades, oldUsual, snapshot.UsualGrades);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FinalGrades.Clear();
                foreach (var g in snapshot.FinalGrades)
                    FinalGrades.Add(g);

                UsualGrades.Clear();
                foreach (var g in snapshot.UsualGrades)
                    UsualGrades.Add(g);

                LastFetchedAtText = snapshot.FetchedAt.ToString("yyyy-MM-dd HH:mm:ss");

                if (!isAutoRefresh)
                {
                    Status = $"查询成功：{EffectiveSemesterText}（期末 {snapshot.FinalGrades.Count}，平时 {snapshot.UsualGrades.Count}）";
                }
                else if (diff.TotalChangedOrAdded == 0)
                {
                    Status = $"自动刷新：暂无更新（期末 {snapshot.FinalGrades.Count}，平时 {snapshot.UsualGrades.Count}）";
                }
                else
                {
                    Status =
                        $"自动刷新：发现更新（期末 +{diff.FinalAdded}/~{diff.FinalChanged}，平时 +{diff.UsualAdded}/~{diff.UsualChanged}）";
                }
            });

            await PersistSettingsAsync(updateStatus: false);

            if (diff.TotalChangedOrAdded > 0 && NtfyEnabled)
            {
                await EnsureNtfyTopicAsync(persistIfGenerated: true);
                if (!string.IsNullOrWhiteSpace(NtfyTopic))
                {
                    try
                    {
                        await NtfyClient.SendAsync(
                            serverBaseUrl: NtfyServerBaseUrl,
                            topic: NtfyTopic,
                            message: BuildNtfyMessage(snapshot, diff),
                            title: BuildNtfyTitle(diff));
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => Status = $"{Status}（推送失败：{ex.Message}）");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Status = ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private string BuildNtfyMessage(GradesSnapshot snapshot, GradeDiff diff)
    {
        var sb = new StringBuilder();
        var highlights = diff.Highlights;
        if (highlights.Count == 0)
        {
            sb.AppendLine($"{EffectiveSemesterText}");
            sb.AppendLine($"时间：{snapshot.FetchedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"期末：新增 {diff.FinalAdded}，更新 {diff.FinalChanged}；平时：新增 {diff.UsualAdded}，更新 {diff.UsualChanged}");
            return sb.ToString().Trim();
        }

        sb.AppendLine($"{EffectiveSemesterText}");
        sb.AppendLine($"时间：{snapshot.FetchedAt:yyyy-MM-dd HH:mm:ss}");

        const int maxLines = 10;
        foreach (var line in highlights.Take(maxLines))
            sb.AppendLine(line);

        if (highlights.Count > maxLines)
            sb.AppendLine($"… 还有 {highlights.Count - maxLines} 项");

        return sb.ToString().Trim();
    }

    private static string BuildNtfyTitle(GradeDiff diff)
    {
        var hasAdded = false;
        var hasChanged = false;
        foreach (var line in diff.Highlights)
        {
            var trimmed = (line ?? string.Empty).TrimStart();
            if (trimmed.StartsWith("+", StringComparison.Ordinal))
                hasAdded = true;
            else if (trimmed.StartsWith("~", StringComparison.Ordinal))
                hasChanged = true;
        }

        var prefix = hasAdded && !hasChanged
            ? "新成绩"
            : hasChanged && !hasAdded
                ? "成绩更新"
                : "成绩变动";

        var courseNames = ExtractCourseNames(diff.Highlights);
        if (courseNames.Count == 0)
            return prefix;

        const int maxNames = 4;
        var shown = courseNames.Take(maxNames).ToArray();
        var title = $"{prefix}：{string.Join('、', shown)}";
        if (courseNames.Count > maxNames)
            title = $"{title}…({courseNames.Count})";

        return title;
    }

    private static IReadOnlyList<string> ExtractCourseNames(IReadOnlyList<string> highlights)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var highlight in highlights)
        {
            var name = TryExtractCourseName(highlight);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (seen.Add(name))
                result.Add(name);
        }

        return result;
    }

    private static string? TryExtractCourseName(string highlight)
    {
        if (string.IsNullOrWhiteSpace(highlight))
            return null;

        var s = highlight.Trim();
        var idx = s.IndexOf("期末 ", StringComparison.Ordinal);
        if (idx >= 0)
            idx += "期末 ".Length;
        else
        {
            idx = s.IndexOf("平时 ", StringComparison.Ordinal);
            if (idx < 0)
                return null;
            idx += "平时 ".Length;
        }

        var end = s.IndexOf('：', idx);
        if (end < 0)
            end = s.IndexOf(':', idx);

        if (end <= idx)
            return null;

        return s[idx..end].Trim();
    }

    private sealed record GradeDiff(
        int FinalAdded,
        int FinalChanged,
        int UsualAdded,
        int UsualChanged,
        IReadOnlyList<string> Highlights)
    {
        public int TotalChangedOrAdded => FinalAdded + FinalChanged + UsualAdded + UsualChanged;
    }

    private static GradeDiff ComputeDiff(
        IReadOnlyList<FinalGrade> oldFinal,
        IReadOnlyList<FinalGrade> newFinal,
        IReadOnlyList<UsualGrade> oldUsual,
        IReadOnlyList<UsualGrade> newUsual)
    {
        static string Key(string courseCode, string courseId) => $"{courseCode}#{courseId}";

        var oldFinalMap = oldFinal.ToDictionary(g => Key(g.CourseCode, g.CourseId), g => g);
        var oldUsualMap = oldUsual.ToDictionary(g => Key(g.CourseCode, g.CourseId), g => g);

        static string FinalScoreText(FinalGrade g)
        {
            if (!string.IsNullOrWhiteSpace(g.FinalScore))
                return g.FinalScore.Trim();
            if (!string.IsNullOrWhiteSpace(g.OverallScore))
                return g.OverallScore.Trim();
            if (!string.IsNullOrWhiteSpace(g.FinalExamScore))
                return g.FinalExamScore.Trim();
            return "无";
        }

        static string UsualScoreText(UsualGrade g)
        {
            return string.IsNullOrWhiteSpace(g.UsualScore) ? "无" : g.UsualScore.Trim();
        }

        var highlights = new List<string>();

        var finalAdded = 0;
        var finalChanged = 0;
        foreach (var g in newFinal)
        {
            if (!oldFinalMap.TryGetValue(Key(g.CourseCode, g.CourseId), out var old))
            {
                finalAdded++;
                highlights.Add($"+ 期末 {g.CourseName}：{FinalScoreText(g)}");
                continue;
            }

            var oldText = FinalScoreText(old);
            var newText = FinalScoreText(g);
            var changed =
                !string.Equals(oldText, newText, StringComparison.Ordinal) ||
                !string.Equals(old.Gpa, g.Gpa, StringComparison.Ordinal);

            if (changed)
            {
                finalChanged++;
                highlights.Add($"~ 期末 {g.CourseName}：{oldText} → {newText}");
            }
        }

        var usualAdded = 0;
        var usualChanged = 0;
        foreach (var g in newUsual)
        {
            if (!oldUsualMap.TryGetValue(Key(g.CourseCode, g.CourseId), out var old))
            {
                usualAdded++;
                highlights.Add($"+ 平时 {g.CourseName}：{UsualScoreText(g)}");
                continue;
            }

            var oldText = UsualScoreText(old);
            var newText = UsualScoreText(g);
            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                usualChanged++;
                highlights.Add($"~ 平时 {g.CourseName}：{oldText} → {newText}");
            }
        }

        return new GradeDiff(finalAdded, finalChanged, usualAdded, usualChanged, highlights);
    }

    private void UpdateProfileDir()
    {
        if (string.IsNullOrWhiteSpace(Account))
        {
            ProfileDir = string.Empty;
            return;
        }

        ProfileDir = AppPaths.GetProfileDir(Account);
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanChooseSemester));
        OnPropertyChanged(nameof(CanChangeAutoRefreshInterval));
    }

    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeAutoRefreshInterval));
        UpdateAutoRefreshTimer();
        _ = AutoPersistSettingsAsync();
    }

    partial void OnAutoRefreshIntervalIndexChanged(int value)
    {
        UpdateAutoRefreshTimer();
        _ = AutoPersistSettingsAsync();
    }

    partial void OnUseAutoSemesterChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChooseSemester));
        OnPropertyChanged(nameof(EffectiveSemesterText));
    }

    partial void OnSemesterYearChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveSemesterText));
    }

    partial void OnSemesterTermIndexChanged(int value)
    {
        OnPropertyChanged(nameof(EffectiveSemesterText));
    }

    private bool _syncingBrowserOption;

    partial void OnChannelChanged(string value)
    {
        SyncBrowserOptionFromChannel();
    }

    partial void OnBrowserOptionIndexChanged(int value)
    {
        if (_syncingBrowserOption)
            return;

        _syncingBrowserOption = true;
        Channel = value switch
        {
            0 => "chrome",
            _ => "chromium",
        };
        _syncingBrowserOption = false;
    }

    private void SyncBrowserOptionFromChannel()
    {
        if (_syncingBrowserOption)
            return;

        _syncingBrowserOption = true;
        BrowserOptionIndex = string.Equals(Channel?.Trim(), "chrome", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        _syncingBrowserOption = false;
    }

    private async Task LoadProfileStateAsync()
    {
        if (string.IsNullOrWhiteSpace(Account))
            return;

        try
        {
            var profileDir = AppPaths.GetProfileDir(Account);
            var statePath = Path.Combine(profileDir, "state.json");
            var store = new JsonStateStore<ProfileState>(statePath, WatchdogCoreJsonContext.Default.ProfileState);
            var state = await store.LoadAsync();
            if (state is null)
                return;

            FinalGrades.Clear();
            foreach (var g in state.FinalGrades)
                FinalGrades.Add(g);

            UsualGrades.Clear();
            foreach (var g in state.UsualGrades)
                UsualGrades.Add(g);

            LastFetchedAtText = state.LastFetchedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未查询";
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private void OpenProfileDir()
    {
        if (string.IsNullOrWhiteSpace(ProfileDir))
        {
            Status = "请先填写学号";
            return;
        }

        try
        {
            OpenFolder(ProfileDir);
        }
        catch (Exception ex)
        {
            Status = $"打开失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ResetSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(Account))
        {
            Status = "请先填写学号";
            return;
        }

        IsBusy = true;
        Status = "正在清除登录状态…";

        try
        {
            var profileDir = AppPaths.GetProfileDir(Account.Trim());
            var userDataDir = Path.Combine(profileDir, "user-data");
            var storageStatePath = Path.Combine(profileDir, "storage-state.json");

            await Task.Run(() =>
            {
                if (Directory.Exists(userDataDir))
                    Directory.Delete(userDataDir, recursive: true);

                if (File.Exists(storageStatePath))
                    File.Delete(storageStatePath);
            });

            Status = "已清除登录状态";
        }
        catch (Exception ex)
        {
            Status = $"清除失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PersistSettingsAsync(bool updateStatus)
    {
        await _persistSettingsLock.WaitAsync();
        try
        {
            if (NtfyEnabled && string.IsNullOrWhiteSpace(NtfyTopic))
                NtfyTopic = NtfyTopicGenerator.Generate();

            var semesterId = string.Empty;
            if (!UseAutoSemester)
            {
                if (!SemesterIdCalculator.TryParseYearRange(SemesterYear, out var startYear, out _))
                    throw new InvalidOperationException("请选择正确的学年范围。");

                var term = SemesterTermIndex == 1 ? SemesterTerm.Second : SemesterTerm.First;
                semesterId = SemesterIdCalculator.Calculate(startYear, term);
            }

            var settings = new AppSettings
            {
                Account = string.IsNullOrWhiteSpace(Account) ? null : Account.Trim(),
                SavePassword = SavePassword,
                Password = SavePassword ? (string.IsNullOrWhiteSpace(Password) ? null : Password) : null,
                Headless = Headless,
                AutoRefreshEnabled = AutoRefreshEnabled,
                AutoRefreshMinutes = GetAutoRefreshMinutes(),
                NtfyEnabled = NtfyEnabled,
                NtfyServerBaseUrl = string.IsNullOrWhiteSpace(NtfyServerBaseUrl) ? "https://ntfy.sh" : NtfyServerBaseUrl.Trim(),
                NtfyTopic = string.IsNullOrWhiteSpace(NtfyTopic) ? null : NtfyTopic.Trim(),
                UseAutoSemester = UseAutoSemester,
                SemesterYear = string.IsNullOrWhiteSpace(SemesterYear) ? null : SemesterYear.Trim(),
                SemesterTerm = SemesterTermIndex == 1 ? 2 : 1,
                SemesterId = UseAutoSemester ? null : semesterId,
                Channel = string.IsNullOrWhiteSpace(Channel) ? "chrome" : Channel.Trim(),
            };

            await _settingsStore.SaveAsync(settings);
            if (updateStatus)
                Status = "设置已保存";
        }
        finally
        {
            _persistSettingsLock.Release();
        }
    }

    private async Task AutoPersistSettingsAsync()
    {
        if (_restoringSettings)
            return;

        try
        {
            await PersistSettingsAsync(updateStatus: false);
        }
        catch
        {
            // ignore
        }
    }

    private DispatcherTimer? _autoRefreshTimer;

    private void UpdateAutoRefreshTimer()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = null;

        if (!AutoRefreshEnabled)
        {
            NextAutoRefreshText = "已关闭";
            return;
        }

        var minutes = GetAutoRefreshMinutes();
        var interval = TimeSpan.FromMinutes(minutes);

        NextAutoRefreshText = DateTimeOffset.Now.Add(interval).ToString("HH:mm");

        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += async (_, _) =>
        {
            if (!AutoRefreshEnabled)
            {
                timer.Stop();
                return;
            }

            NextAutoRefreshText = DateTimeOffset.Now.Add(interval).ToString("HH:mm");

            if (IsBusy)
                return;

            await RunQueryAsync(isAutoRefresh: true);
        };

        timer.Start();
        _autoRefreshTimer = timer;
    }

    private int GetAutoRefreshMinutes()
    {
        return AutoRefreshIntervalIndex switch
        {
            0 => 30,
            2 => 120,
            _ => 60,
        };
    }

    private static int NormalizeAutoRefreshIntervalIndex(int minutes)
    {
        return minutes switch
        {
            30 => 0,
            120 => 2,
            _ => 1,
        };
    }

    private async Task EnsureNtfyTopicAsync(bool persistIfGenerated)
    {
        if (!NtfyEnabled)
            return;

        if (!string.IsNullOrWhiteSpace(NtfyTopic))
            return;

        NtfyTopic = NtfyTopicGenerator.Generate();
        OnPropertyChanged(nameof(NtfySubscribeUrl));

        if (persistIfGenerated)
            await PersistSettingsAsync(updateStatus: false);
    }

    partial void OnNtfyTopicChanged(string value)
    {
        OnPropertyChanged(nameof(NtfySubscribeUrl));
    }

    partial void OnNtfyServerBaseUrlChanged(string value)
    {
        OnPropertyChanged(nameof(NtfySubscribeUrl));
    }

    partial void OnNtfyEnabledChanged(bool value)
    {
        if (value)
            _ = EnsureNtfyTopicAsync(persistIfGenerated: true);
    }

    [RelayCommand]
    private void OpenNtfySubscribePage()
    {
        if (string.IsNullOrWhiteSpace(NtfySubscribeUrl))
        {
            Status = "订阅地址为空";
            return;
        }

        try
        {
            OpenUrl(NtfySubscribeUrl);
        }
        catch (Exception ex)
        {
            Status = $"打开失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RegenerateNtfyTopicAsync()
    {
        NtfyTopic = NtfyTopicGenerator.Generate();
        try
        {
            await PersistSettingsAsync(updateStatus: false);
            Status = "已重新生成订阅通道";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SendNtfyTestMessageAsync()
    {
        if (!NtfyEnabled)
        {
            Status = "请先开启推送通知";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        Status = "正在发送测试消息…";

        try
        {
            await EnsureNtfyTopicAsync(persistIfGenerated: true);
            var result = await NtfyClient.SendAsync(
                serverBaseUrl: NtfyServerBaseUrl,
                topic: NtfyTopic,
                message: $"这是一条测试消息。\n时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}",
                title: "UESTCJWCWatchdog 测试");

            Status = $"测试消息已发送（id={result.Id}）";
        }
        catch (Exception ex)
        {
            Status = $"发送失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void InitializeSemesterChoices()
    {
        if (AvailableSemesterYears.Count > 0)
            return;

        var now = DateTimeOffset.Now;
        var maxStartYear = now.Year + 5;
        for (var y = SemesterIdCalculator.BaseStartYear; y <= maxStartYear; y++)
            AvailableSemesterYears.Add(SemesterIdCalculator.ToYearRange(y));
    }

    private static string ResolveSemesterYear(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SemesterYear))
            return settings.SemesterYear;

        if (SemesterIdCalculator.TryDecode(settings.SemesterId, out var startYear, out _))
            return SemesterIdCalculator.ToYearRange(startYear);

        var (currentStartYear, _) = SemesterIdCalculator.GetAcademicTerm(DateTimeOffset.Now);
        return SemesterIdCalculator.ToYearRange(currentStartYear);
    }

    private static int ResolveSemesterTermIndex(AppSettings settings)
    {
        if (settings.SemesterTerm is 1)
            return 0;

        if (settings.SemesterTerm is 2)
            return 1;

        if (SemesterIdCalculator.TryDecode(settings.SemesterId, out _, out var term))
            return term == SemesterTerm.Second ? 1 : 0;

        var (_, currentTerm) = SemesterIdCalculator.GetAcademicTerm(DateTimeOffset.Now);
        return currentTerm == SemesterTerm.Second ? 1 : 0;
    }

    private static void OpenFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            return;
        }

        var linuxStartInfo = new ProcessStartInfo
        {
            FileName = "xdg-open",
            UseShellExecute = false,
        };
        linuxStartInfo.ArgumentList.Add(path);
        Process.Start(linuxStartInfo);
    }

    private static void OpenUrl(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
            return;
        }

        var linuxStartInfo = new ProcessStartInfo
        {
            FileName = "xdg-open",
            UseShellExecute = false,
        };
        linuxStartInfo.ArgumentList.Add(url);
        Process.Start(linuxStartInfo);
    }
}
