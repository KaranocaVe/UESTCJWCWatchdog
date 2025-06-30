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
            .Select(_ => Unit.Default); // 只关心“发生了变化”这一事件

        // 监听 Courses 集合
        var coursesChangedStream = Courses
            .ToObservableChangeSet() // 需要 NuGet: DynamicData
            .Select(_ => Unit.Default);

        //  合并并节流 1 s 后保存
        propertyChangedStream
            .Merge(coursesChangedStream)
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


            if (EnableAutoUpdate)
            {
                UpdateTimerState();
            }
        });
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
        public List<Course> Courses { get; set; } = new(); // List 比 ObservableCollection 易序列化
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

    public ObservableCollection<string> availableSemesterYear { get; } = new ObservableCollection<string>
    {
        "2022-2023",
        "2023-2024",
        "2024-2025",
        "2025-2026",
        "2026-2027",
        "2027-2028",
    };

    private string _semesterYear = string.Empty;

    public string SemesterYear
    {
        get => _semesterYear;
        set => this.RaiseAndSetIfChanged(ref _semesterYear, value);
    }

    public ObservableCollection<string> availableSemesterNumber { get; } = new ObservableCollection<string>
    {
        "第一学期",
        "第二学期"
    };

    private string _semesterNumber = string.Empty;

    public string SemesterNumber
    {
        get => _semesterNumber;
        set => this.RaiseAndSetIfChanged(ref _semesterNumber, value);
    }

    public class Course
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


    private ObservableCollection<Course> _courses = new();
    public ObservableCollection<Course> Courses
    {
        get => _courses;
        set => this.RaiseAndSetIfChanged(ref _courses, value); // ReactiveUI 的推荐方式
    }

    public static string CalculateSemesterId(string yearRange, string semesterName)
    {
        // 解析起始年份，例如 "2022-2023" -> 2022
        string[] years = yearRange.Split('-');
        if (years.Length != 2 || !int.TryParse(years[0], out int startYear))
        {
            throw new ArgumentException("学年范围格式应为 'YYYY-YYYY'");
        }

        // 基础值计算
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
            var status = doc.RootElement.GetProperty("status").GetString();

            if (status == "success")
            {
                var newCoursesJson = doc.RootElement.GetProperty("new_courses").ToString();

                var parsedCourses = JsonSerializer.Deserialize<List<Course>>(newCoursesJson);

                // ✅ 更新 ObservableCollection
                Courses.Clear();
                foreach (var course in parsedCourses)
                    Courses.Add(course);
            }
            else
            {
                // 可选：处理 "no_change" 状态
                Courses.Clear(); // 或者保留原内容
            }

            LastUpdateTime = "上次查询时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
            var status = doc.RootElement.GetProperty("status").GetString();

            if (status == "success")
            {
                var newCoursesJson = doc.RootElement.GetProperty("new_courses").ToString();
                var parsedCourses = JsonSerializer.Deserialize<List<Course>>(newCoursesJson);

                // 比较新旧数据
                var oldCourses = new List<Course>(Courses);
                var diffCourses = new List<Course>();
                foreach (var newCourse in parsedCourses)
                {
                    // 以课程代码和课程序号为唯一标识
                    var match = oldCourses.Find(c => c.CourseCode == newCourse.CourseCode && c.CourseId == newCourse.CourseId);
                    if (match == null || JsonSerializer.Serialize(match) != JsonSerializer.Serialize(newCourse))
                    {
                        diffCourses.Add(newCourse);
                    }
                }

                // 发送通知
                if (diffCourses.Count > 0)
                {
                    foreach (var course in diffCourses)
                    {
                        SendNotification($"课程更新: {course.CourseName}", $"成绩: {course.FinalScore ?? course.OverallScore ?? "无"}");
                    }
                }

                // ✅ 更新 ObservableCollection
                Courses.Clear();
                foreach (var course in parsedCourses)
                    Courses.Add(course);
            }
            else
            {
                // 可选：处理 "no_change" 状态
                Courses.Clear(); // 或者保留原内容
            }

            LastUpdateTime = "上次查询时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("JSON 解析失败: " + ex.Message);
        }
        IsLoading = false;
    }

    private DispatcherTimer _autoUpdateTimer;


    private async Task<string> RunPythonAsync(string exePath, string args = "")
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

            Courses.Clear();
            foreach (var c in state.Courses)
                Courses.Add(c);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("读取持久化文件失败: " + ex.Message);
        }
    }

    /// <summary>任何属性、Courses 变化后 1 秒内触发</summary>
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
                Courses = new List<Course>(Courses) // 拷贝一份
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
