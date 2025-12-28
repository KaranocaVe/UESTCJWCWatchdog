using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using DynamicData.Binding;
using ReactiveUI;
using SukiUI.Toasts;


namespace KnowYourGrades.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        LoadState();

        // 监听属性
        var propertyChangedStream = this
            .WhenAnyValue(
                x => x.EnableAutoUpdate,
                x => x.UpdateInterval,
                x => x.LastUpdateTime,
                x => x.StdNum,
                x => x.StdPwd,
                x => x.SemesterYear,
                x => x.SemesterNumber)
            .Select(_ => Unit.Default);


        // 监听 FinalGrades 和 UsualGrades 集合
        var finalGradesChangedStream = FinalGrades
            .ToObservableChangeSet()
            .Select(_ => Unit.Default);

        var usualGradesChangedStream = UsualGrades
            .ToObservableChangeSet()
            .Select(_ => Unit.Default);

        propertyChangedStream
            .Merge(finalGradesChangedStream)
            .Merge(usualGradesChangedStream)
            .Throttle(TimeSpan.FromSeconds(1))
            .Subscribe(async _ => await SaveStateAsync());




        _autoUpdateTimer = new DispatcherTimer
        {
            IsEnabled = false
        };
        _autoUpdateTimer.Tick += (_, _) => TimerCheck();


        Dispatcher.UIThread.Post(() =>
        {
            SendNotification("欢迎使用 KnowYourGrades",
                "请先填写学号、密码和学年学期，然后点击查询按钮。");

            _window = Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (EnableAutoUpdate)
            {
                UpdateTimerState();
            }
        });
    }

    private  Window? _window = null;

    public static void BringToFront(Window window)
    {
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
    }

    private bool _needHighlight;

    public bool NeedHighlight
    {
        get => _needHighlight;
        set => this.RaiseAndSetIfChanged(ref _needHighlight, value);
    }

    private class AppState
    {
        public bool EnableAutoUpdate { get; set; }
        public int UpdateInterval { get; set; }
        public string LastUpdateTime { get; set; } = "上次查询时间：未查询";
        public string StdNum { get; set; } = string.Empty;
        public string StdPwd { get; set; } = string.Empty;
        public string SemesterYear { get; set; } = string.Empty;
        public string SemesterNumber { get; set; } = string.Empty;
        public List<FinalGrade> FinalGrades { get; set; } = new();
        public List<UsualGrade> UsualGrades { get; set; } = new();
        public bool NeedHighlight { get; set; }
    }

    private bool _isLoading = false;

    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private bool _enableAutoUpdate;
    public bool EnableAutoUpdate
    {
        get => _enableAutoUpdate;
        set
        {
            if (_enableAutoUpdate != value)
            {
                _enableAutoUpdate = value;
                UpdateTimerState();
            }
        }
    }

    private int _updateInterval = 60; // 默认 60 分钟
    public int UpdateInterval
    {
        get => _updateInterval;
        set
        {
            if (_updateInterval != value)
            {
                _updateInterval = value;
                UpdateTimerState();
            }
        }
    }

    private void UpdateTimerState()
    {
        if (_autoUpdateTimer == null)
            return;

        if (EnableAutoUpdate && UpdateInterval > 0)
        {
            _autoUpdateTimer.Interval = TimeSpan.FromMinutes(UpdateInterval);
            _autoUpdateTimer.Start();
        }
        else
        {
            _autoUpdateTimer.Stop();
        }
    }


    private string _lastUpdateTime = "上次查询时间：未查询";

    public string LastUpdateTime
    {
        get => _lastUpdateTime;
        set => this.RaiseAndSetIfChanged(ref _lastUpdateTime, value);
    }

    private string _stdnum = string.Empty;

    public string StdNum
    {
        get => _stdnum;
        set => this.RaiseAndSetIfChanged(ref _stdnum, value);
    }

    private string _stdpwd = string.Empty;

    public string StdPwd
    {
        get => _stdpwd;
        set => this.RaiseAndSetIfChanged(ref _stdpwd, value);
    }

    private string _semesterId = string.Empty;

    public ObservableCollection<string> AvailableSemesterYear { get; } =
    [
        "2022-2023",
        "2023-2024",
        "2024-2025",
        "2025-2026",
        "2026-2027",
        "2027-2028",
        "2028-2029",
        "2029-2030"

    ];

    private string _semesterYear = string.Empty;

    public string SemesterYear
    {
        get => _semesterYear;
        set => this.RaiseAndSetIfChanged(ref _semesterYear, value);
    }

    public ObservableCollection<string> AvailableSemesterNumber { get; } =
    [
        "第一学期",
        "第二学期"
    ];

    private string _semesterNumber = string.Empty;

    public string SemesterNumber
    {
        get => _semesterNumber;
        set => this.RaiseAndSetIfChanged(ref _semesterNumber, value);
    }

    private static string CalculateSemesterId(string yearRange, string semesterName)
    {
        string[] years = yearRange.Split('-');
        if (years.Length != 2 || !int.TryParse(years[0], out int startYear))
        {
            throw new ArgumentException("学年范围格式应为 'YYYY-YYYY'");
        }

        int baseId = (startYear - 2013) * 40 + 3;

        int result;
        if (semesterName == "第一学期")
        {
            result = baseId;
        }
        else if (semesterName == "第二学期")
        {
            result = baseId + 20;
        }
        else
        {
            throw new ArgumentException("学期名称应为“第一学期”或“第二学期”");
        }

        return result.ToString();
    }

    public class FinalGrade
    {
        [JsonPropertyName("学年学期")]
        public string Semester { get; set; }

        [JsonPropertyName("课程代码")]
        public string CourseCode { get; set; }

        [JsonPropertyName("课程序号")]
        public string CourseId { get; set; }

        [JsonPropertyName("课程名称")]
        public string CourseName { get; set; }

        [JsonPropertyName("课程类别")]
        public string CourseType { get; set; }

        [JsonPropertyName("学分")]
        public string Credit { get; set; }

        [JsonPropertyName("期末成绩")]
        public string FinalExamScore { get; set; }

        [JsonPropertyName("总评成绩")]
        public string OverallScore { get; set; }

        [JsonPropertyName("补考总评")]
        public string MakeupScore { get; set; }

        [JsonPropertyName("最终")]
        public string FinalScore { get; set; }

        [JsonPropertyName("绩点")]
        public string GPA { get; set; }
    }

    private ObservableCollection<FinalGrade> _finalGrades = new();
    public ObservableCollection<FinalGrade> FinalGrades
    {
        get => _finalGrades;
        set => this.RaiseAndSetIfChanged(ref _finalGrades, value);
    }

    public class UsualGrade
    {
        [JsonPropertyName("学年学期")]
        public string Semester { get; set; }

        [JsonPropertyName("课程代码")]
        public string CourseCode { get; set; }

        [JsonPropertyName("课程序号")]
        public string CourseId { get; set; }

        [JsonPropertyName("课程名称")]
        public string CourseName { get; set; }

        [JsonPropertyName("课程类别")]
        public string CourseType { get; set; }

        [JsonPropertyName("学分")]
        public string Credit { get; set; }

        [JsonPropertyName("平时成绩")]
        public string UsualScore { get; set; }
    }

    private ObservableCollection<UsualGrade> _usualGrades = new();
    public ObservableCollection<UsualGrade> UsualGrades
    {
        get => _usualGrades;
        set => this.RaiseAndSetIfChanged(ref _usualGrades, value);
    }


    public async Task ManuallyLoadCoursesFromPythonAsync()
    {
        IsLoading = true;
        // 更新 SemesterId
        _semesterId = CalculateSemesterId(SemesterYear, SemesterNumber);

        string json = await RunPythonAsync("cli.exe",
            $"{StdNum} {StdPwd} {_semesterId}");

        try
        {
            using var doc = JsonDocument.Parse(json);

            var usualGradesJson = doc.RootElement.GetProperty("usual_grades").ToString();
            var finalGradesJson = doc.RootElement.GetProperty("final_grades").ToString();

            var parsedUsualGrades = JsonSerializer.Deserialize<List<UsualGrade>>(usualGradesJson);
            var parsedFinalGrades = JsonSerializer.Deserialize<List<FinalGrade>>(finalGradesJson);

            UsualGrades.Clear();
            if (parsedUsualGrades != null)
            {
                foreach (var course in parsedUsualGrades)
                    UsualGrades.Add(course);
            }

            FinalGrades.Clear();
            if (parsedFinalGrades != null)
            {
                foreach (var course in parsedFinalGrades)
                    FinalGrades.Add(course);
            }

            LastUpdateTime = "上次手动查询时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            ToastManager.CreateToast()
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .OfType(NotificationType.Error)
                .WithTitle("解析失败")
                .WithContent(ex.Message)
                .Queue();
        }
        IsLoading = false;

        // 发送通知提示查询完毕
        SendNotification("查询完成", "已成功加载成绩数据。请查看成绩列表。");

        // 聚焦窗口
            if (NeedHighlight)
            {
                if (_window != null)
                    BringToFront(_window);
            }

    }

    public async Task TimerCheck()
    {
        IsLoading = true;
        // 更新 SemesterId
        _semesterId = CalculateSemesterId(SemesterYear, SemesterNumber);

        string json = await RunPythonAsync("cli.exe",
            $"{StdNum} {StdPwd} {_semesterId}");

        try
        {
            using var doc = JsonDocument.Parse(json);

            var usualGradesJson = doc.RootElement.GetProperty("usual_grades").ToString();
            var finalGradesJson = doc.RootElement.GetProperty("final_grades").ToString();

            var parsedUsualGrades = JsonSerializer.Deserialize<List<UsualGrade>>(usualGradesJson);
            var parsedFinalGrades = JsonSerializer.Deserialize<List<FinalGrade>>(finalGradesJson);

            var oldUsualGrades = new List<UsualGrade>(UsualGrades);
            var diffCoursesUsual = new List<UsualGrade>();
            foreach (var newCourse in parsedUsualGrades)
            {
                var match = oldUsualGrades.Find(c => c.CourseCode == newCourse.CourseCode && c.CourseId == newCourse.CourseId);
                if (match == null || JsonSerializer.Serialize(match) != JsonSerializer.Serialize(newCourse))
                {
                    diffCoursesUsual.Add(newCourse);
                }
            }
            // 发送通知
            if (diffCoursesUsual.Count > 0)
            {
                foreach (var course in diffCoursesUsual)
                {
                    SendNotification($"平时成绩更新: {course.CourseName}", $"成绩: {course.UsualScore ?? "无"}");
                }
            }
            UsualGrades.Clear();
            foreach (var course in parsedUsualGrades)
                UsualGrades.Add(course);

            var oldFinalGrades = new List<FinalGrade>(FinalGrades);
            var diffCoursesFinal = new List<FinalGrade>();
            foreach (var newCourse in parsedFinalGrades)
            {
                var match = oldFinalGrades.Find(c => c.CourseCode == newCourse.CourseCode && c.CourseId == newCourse.CourseId);
                if (match == null || JsonSerializer.Serialize(match) != JsonSerializer.Serialize(newCourse))
                {
                    diffCoursesFinal.Add(newCourse);
                }
            }
            // 发送通知
            if (diffCoursesFinal.Count > 0)
            {
                foreach (var course in diffCoursesFinal)
                {
                    SendNotification($"总评成绩更新: {course.CourseName}", $"成绩: {course.FinalScore ?? course.OverallScore ?? "无"}");
                }
            }

            // 聚焦窗口
            if(diffCoursesFinal.Count > 0||diffCoursesUsual.Count>0)
            {
                if (NeedHighlight)
                {
                    if (_window != null)
                        BringToFront(_window);
                }
            }

            FinalGrades.Clear();
            foreach (var course in parsedFinalGrades)
                FinalGrades.Add(course);

            LastUpdateTime = "上次自动查询时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("解析失败: " + ex.Message);
        }
        IsLoading = false;
    }

    private DispatcherTimer _autoUpdateTimer;


    private async Task<string> RunPythonAsync(string exePath, string args)
    {
        return await Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath, // 如果是 main.py，用 python 作为 FileName，cli.py 作为 Arguments
                Arguments = args, // 若需要参数则填 json 字符串
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
                Debug.WriteLine("Python Error: " + error);

            return output;
        });
    }

    public SukiToastManager ToastManager { get; } = new();

    void SendNotification(string title, string message)
    {
        ToastManager.CreateToast()
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .OfType(NotificationType.Information)
            .WithTitle(title)
            .WithContent(message)
            .Queue();

        SystemSounds.Asterisk.Play();
    }

    #region Save / Load

    private static readonly string StateFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KnowYourGrades", "state.json");

    /// <summary>启动时调用：读取本地 state.json 反序列化并填充属性</summary>
    private void LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return;

            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<AppState>(json);
            if (state is null) return;

            EnableAutoUpdate = state.EnableAutoUpdate;
            UpdateInterval = state.UpdateInterval;
            LastUpdateTime = state.LastUpdateTime;
            StdNum = state.StdNum;
            StdPwd = state.StdPwd;
            SemesterYear = state.SemesterYear;
            SemesterNumber = state.SemesterNumber;

            FinalGrades.Clear();
            foreach (var c in state.FinalGrades)
                FinalGrades.Add(c);

            UsualGrades.Clear();
            foreach (var c in state.UsualGrades)
                UsualGrades.Add(c);

            NeedHighlight = state.NeedHighlight;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("读取持久化文件失败: " + ex.Message);
        }
    }

    /// <summary>任何属性、FinalGrades 变化后 1 秒内触发</summary>
    private async Task SaveStateAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var state = new AppState
            {
                EnableAutoUpdate = EnableAutoUpdate,
                UpdateInterval = UpdateInterval,
                LastUpdateTime = LastUpdateTime,
                StdNum = StdNum,
                StdPwd = StdPwd,
                SemesterYear = SemesterYear,
                SemesterNumber = SemesterNumber,
                FinalGrades = new List<FinalGrade>(FinalGrades),
                UsualGrades = new List<UsualGrade>(UsualGrades),
                NeedHighlight = NeedHighlight
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 防止中文转 \uXXXX
            };

            var json = JsonSerializer.Serialize(state, options);
            await File.WriteAllTextAsync(StateFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("保存持久化文件失败: " + ex.Message);
        }
    }
    #endregion
}
