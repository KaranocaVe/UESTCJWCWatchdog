using Microsoft.Playwright;
using System.Collections.Concurrent;

namespace Watchdog.Core.Eams;

public sealed class EamsClient : IAsyncDisposable
{
    private readonly EamsClientOptions _options;

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;
    private readonly ConcurrentQueue<string> _diagnosticLines = new();
    private string? _diagnosticFilePath;

    public EamsClient(EamsClientOptions options)
    {
        _options = options;
    }

    public string? CurrentUrl => _page?.Url;

    public async Task InitializeAsync()
    {
        if (_playwright is not null)
            return;

        _playwright = await Playwright.CreateAsync();

        // 确定要使用的浏览器通道
        var channel = NormalizeChannel(_options.Channel);
        var executablePath = string.IsNullOrWhiteSpace(_options.ExecutablePath) ? null : _options.ExecutablePath;

        // 如果用户指定了 Chrome 但没有提供可执行路径，尝试自动检测
        if (string.Equals(channel, "chrome", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(executablePath) &&
            string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            // 尝试启动浏览器，如果失败则回退到 Edge
            _context = await TryLaunchBrowserWithFallbackAsync(
                _playwright,
                primaryChannel: "chrome",
                fallbackChannel: "msedge",
                userDataDir: _options.UserDataDir,
                options: new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = "chrome",
                    Headless = _options.HeadlessImplementation == HeadlessImplementation.Playwright && _options.Headless,
                    SlowMo = _options.SlowMoMs,
                    BypassCSP = _options.BypassCsp,
                });
        }
        else
        {
            // 使用指定的配置直接启动
            var launchOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                Channel = channel,
                ExecutablePath = executablePath,
                Headless = _options.HeadlessImplementation == HeadlessImplementation.Playwright && _options.Headless,
                SlowMo = _options.SlowMoMs,
                BypassCSP = _options.BypassCsp,
            };

            if (_options.NoViewport)
            {
                launchOptions.ViewportSize = ViewportSize.NoViewport;
            }
            else if (_options.WindowWidth > 0 && _options.WindowHeight > 0)
            {
                launchOptions.ViewportSize = new ViewportSize { Width = _options.WindowWidth, Height = _options.WindowHeight };
                launchOptions.ScreenSize = new ScreenSize { Width = _options.WindowWidth, Height = _options.WindowHeight };
            }

            var userAgent = ResolveUserAgent();
            if (!string.IsNullOrWhiteSpace(userAgent))
                launchOptions.UserAgent = userAgent;

            if (_options.ExtraHttpHeaders is not null && _options.ExtraHttpHeaders.Count > 0)
                launchOptions.ExtraHTTPHeaders = new Dictionary<string, string>(_options.ExtraHttpHeaders);

            var args = new List<string>();
            if (_options.Headless && _options.HeadlessImplementation == HeadlessImplementation.ChromiumArg)
                args.Add(_options.ChromiumHeadlessArg);

            if (_options.ApplyLegacySpiderArgs)
            {
                args.Add("--disable-gpu");
                args.Add("--no-sandbox");

                if (_options.WindowWidth > 0 && _options.WindowHeight > 0)
                    args.Add($"--window-size={_options.WindowWidth},{_options.WindowHeight}");

                args.Add("--disable-blink-features=AutomationControlled");
            }

            if (args.Count > 0)
                launchOptions.Args = args;

            _context = await _playwright.Chromium.LaunchPersistentContextAsync(
                userDataDir: _options.UserDataDir,
                options: launchOptions);
        }

        _context.SetDefaultTimeout(_options.DefaultTimeoutMs);
        _context.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);

        if (_options.EnableStealthScripts && _options.Headless)
            await ApplyStealthInitScriptsAsync(_context);

        _page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();
        _page.SetDefaultTimeout(_options.DefaultTimeoutMs);
        _page.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);

        if (!string.IsNullOrWhiteSpace(_options.DiagnosticsDir))
            AttachDiagnostics(_page, _options.DiagnosticsDir!);
    }

    public async Task<GradesSnapshot> GetGradesSnapshotAsync(EamsCredentials credentials, string? semesterId = null)
    {
        await InitializeAsync();

        var resolvedSemesterId = semesterId;
        if (string.IsNullOrWhiteSpace(resolvedSemesterId))
            resolvedSemesterId = await GetCurrentSemesterIdAsync(credentials);

        var finalGrades = await GetFinalGradesAsync(credentials, resolvedSemesterId);
        var usualGrades = await GetUsualGradesAsync(credentials, resolvedSemesterId);

        return new GradesSnapshot
        {
            SemesterId = resolvedSemesterId,
            FetchedAt = DateTimeOffset.Now,
            FinalGrades = finalGrades,
            UsualGrades = usualGrades,
        };
    }

    public async Task<IReadOnlyList<SemesterOption>> GetSemesterOptionsAsync(EamsCredentials credentials)
    {
        await InitializeAsync();
        await NavigateAuthedAsync(credentials, $"{_options.BaseUrl}publicSearch.action");

        var select = Page.Locator("select[name='semester.id']");
        await select.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });

        var options = await select
            .Locator("option")
            .EvaluateAllAsync<SemesterOption[]>("opts => opts.map(o => ({ id: o.value, name: (o.textContent || '').trim() }))");

        return options
            .Where(o => !string.IsNullOrWhiteSpace(o.Id))
            .ToArray();
    }

    public async Task<string> GetCurrentSemesterIdAsync(EamsCredentials credentials)
    {
        // Use the legacy semester-id formula (ported from Legacy GUI) to avoid relying on
        // the site's default option order (which can point to a future semester).
        return await Task.FromResult(SemesterIdCalculator.GetCurrentSemesterId(DateTimeOffset.Now));
    }

    public async Task<IReadOnlyList<FinalGrade>> GetFinalGradesAsync(EamsCredentials credentials, string semesterId)
    {
        await InitializeAsync();

        var url =
            $"{_options.BaseUrl}teach/grade/course/person!search.action?semesterId={Uri.EscapeDataString(semesterId)}&projectType=";
        await NavigateAuthedAsync(credentials, url);

        var table = await ResolveTableAsync("课程名称");
        var rowLocator = table.Locator("tbody tr");
        var count = await rowLocator.CountAsync();
        if (count == 0)
            return Array.Empty<FinalGrade>();

        var rows = await rowLocator.EvaluateAllAsync<string[][]>(
            "rows => rows.map(r => Array.from(r.querySelectorAll('td')).map(td => (td.innerText || '').trim()))");

        var result = new List<FinalGrade>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Length < 11)
                continue;

            result.Add(new FinalGrade
            {
                Semester = row[0],
                CourseCode = row[1],
                CourseId = row[2],
                CourseName = row[3],
                CourseType = row[4],
                Credit = row[5],
                FinalExamScore = row[6],
                OverallScore = row[7],
                MakeupScore = row[8],
                FinalScore = row[9],
                Gpa = row[10],
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<UsualGrade>> GetUsualGradesAsync(EamsCredentials credentials, string semesterId)
    {
        await InitializeAsync();

        await Context.AddCookiesAsync(
        [
            new Cookie
            {
                Url = _options.BaseUrl,
                Name = "semester.id",
                Value = semesterId,
            }
        ]);

        var url = $"{_options.BaseUrl}teach/grade/usual/usual-grade-std.action";
        await NavigateAuthedAsync(credentials, url);

        var table = Page.Locator("table.gridtable").First;
        await table.WaitForAsync();
        var rowLocator = table.Locator("tbody tr");
        var count = await rowLocator.CountAsync();
        if (count == 0)
            return Array.Empty<UsualGrade>();

        var rows = await rowLocator.EvaluateAllAsync<string[][]>(
            "rows => rows.map(r => Array.from(r.querySelectorAll('td')).slice(0, 7).map(td => (td.innerText || '').trim()))");

        var result = new List<UsualGrade>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Length < 7)
                continue;

            result.Add(new UsualGrade
            {
                Semester = row[0],
                CourseCode = row[1],
                CourseId = row[2],
                CourseName = row[3],
                CourseType = row[4],
                Credit = row[5],
                UsualScore = row[6],
            });
        }

        return result;
    }

    public async Task SaveStorageStateAsync(string path)
    {
        await InitializeAsync();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await Context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = path });
    }

    public async Task DumpDebugAsync(string dir)
    {
        await InitializeAsync();

        Directory.CreateDirectory(dir);

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        var urlPath = Path.Combine(dir, $"{timestamp}_url.txt");
        var htmlPath = Path.Combine(dir, $"{timestamp}_page.html");
        var screenshotPath = Path.Combine(dir, $"{timestamp}_screenshot.png");

        await File.WriteAllTextAsync(urlPath, Page.Url);
        await File.WriteAllTextAsync(htmlPath, await Page.ContentAsync());
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        await FlushDiagnosticsAsync();
    }

    public async Task<string> GetFingerprintJsonAsync()
    {
        await InitializeAsync();
        await Page.GotoAsync("about:blank");
        var json = await Page.EvaluateAsync<string>(StealthDiagnostics.GetFingerprintScript());
        return StealthDiagnostics.PrettyPrint(json);
    }

    public async Task<IReadOnlyList<(string Name, string Domain, string Path)>> GetCookiesAsync()
    {
        await InitializeAsync();
        var cookies = await Context.CookiesAsync();
        return cookies
            .Select(c => (c.Name, c.Domain, c.Path))
            .ToArray();
    }

    private IBrowserContext Context => _context ?? throw new InvalidOperationException("Client not initialized.");
    private IPage Page => _page ?? throw new InvalidOperationException("Client not initialized.");

    private async Task NavigateAuthedAsync(EamsCredentials credentials, string targetUrl)
    {
        var response = await Page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForAntiBotClearAsync(response);

        if (await IsDuplicateLoginPageAsync())
        {
            await HandleDuplicateLoginAsync(targetUrl);
            return;
        }

        if (await IsIdasLoginPageAsync() || await IsLoginFormVisibleAsync())
        {
            await PerformLoginAsync(credentials);
            var retryResponse = await Page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await WaitForAntiBotClearAsync(retryResponse);

            if (await IsDuplicateLoginPageAsync())
                await HandleDuplicateLoginAsync(targetUrl);
        }
    }

    private async Task<bool> IsLoginFormVisibleAsync()
    {
        var password = Page.Locator("input#password, input[name='password'], input[type='password']").First;
        if (await password.CountAsync() == 0)
            return false;

        if (!await password.IsVisibleAsync())
            return false;

        var user = Page.Locator("input#username, input[name='username'], input[name='userName'], input[autocomplete='username']").First;
        if (await user.CountAsync() == 0)
            user = Page.Locator("input[type='text'], input[type='email'], input[type='tel']").First;

        if (await user.CountAsync() == 0)
            return false;

        return await user.IsVisibleAsync();
    }

    private async Task PerformLoginAsync(EamsCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.Password))
        {
            if (!_options.Headless && _options.AllowManualLogin)
            {
                await WaitForManualLoginAsync();
                return;
            }

            throw new InvalidOperationException("会话已失效，需要密码重新登录。请在“设置”中填写密码后重试。");
        }

        // If we're already on a login page, avoid extra navigation which can be aborted by
        // the IDAS page's own redirects/subresource loads.
        if (await IsIdasLoginPageAsync())
        {
            await PerformIdasUsernamePasswordLoginAsync(credentials);
            return;
        }

        if (await IsLoginFormVisibleAsync())
        {
            await PerformLegacyLoginAsync(credentials);
            return;
        }

        // Otherwise, navigate to the base url to trigger authentication flow.
        var response = await Page.GotoAsync(_options.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForAntiBotClearAsync(response);

        if (await IsIdasLoginPageAsync())
        {
            await PerformIdasUsernamePasswordLoginAsync(credentials);
            return;
        }

        if (!await IsLoginFormVisibleAsync())
            return;

        await PerformLegacyLoginAsync(credentials);
    }

    private async Task WaitForManualLoginAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(_options.ManualLoginTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await IsIdasLoginPageAsync() && !await IsLoginFormVisibleAsync() && !await IsLikelyAntiBotPageAsync())
                return;

            await Page.WaitForTimeoutAsync(500);
        }

        throw new InvalidOperationException("登录未完成（等待超时）。");
    }

    private async Task PerformLegacyLoginAsync(EamsCredentials credentials)
    {
        if (!await IsLoginFormVisibleAsync())
            return;

        // Legacy (non-IDAS) login form fallback.
        var user = Page
            .Locator("input#username:visible, input[name='username']:visible, input[name='userName']:visible, input[autocomplete='username']:visible")
            .First;
        if (await user.CountAsync() == 0)
            user = Page.Locator("input[type='text']:visible, input[type='email']:visible, input[type='tel']:visible").First;

        var password = Page.Locator("input#password:visible, input[name='password']:visible, input[type='password']:visible").First;

        var submit = Page.Locator(
                "#login_submit, button#login_submit, button[type='submit'], input[type='submit'], button:has-text(\"登录\"), button:has-text(\"Login\")")
            .First;

        await user.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await password.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Some login pages rely on real typing events (and users want to "see" the autofill),
        // so we do click + type with a small delay.
        await user.ClickAsync();
        await user.FillAsync(string.Empty);
        await user.PressSequentiallyAsync(credentials.Account, new LocatorPressSequentiallyOptions { Delay = 30 });

        await password.ClickAsync();
        await password.FillAsync(string.Empty);
        await password.PressSequentiallyAsync(credentials.Password, new LocatorPressSequentiallyOptions { Delay = 30 });

        // Verify autofill worked. Some pages need JS-assignment + input events.
        var typedUser = await SafeInputValueAsync(user);
        if (!string.Equals(typedUser, credentials.Account, StringComparison.Ordinal))
        {
            await user.EvaluateAsync(
                "(el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); }",
                credentials.Account);
        }

        var typedPwd = await SafeInputValueAsync(password);
        if (typedPwd is not null && typedPwd.Length == 0)
        {
            await password.EvaluateAsync(
                "(el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); }",
                credentials.Password);
        }

        await submit.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (await Page.Locator("text=当前用户存在重复登录的情况").CountAsync() > 0)
        {
            var retryResponse = await Page.GotoAsync(
                $"{_options.BaseUrl}teach/grade/course/person!search.action",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await WaitForAntiBotClearAsync(retryResponse);
        }

        var reloadResponse = await Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForAntiBotClearAsync(reloadResponse);

        if (await IsLoginFormVisibleAsync())
        {
            if (!_options.Headless && _options.AllowManualLogin)
            {
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(_options.ManualLoginTimeoutMs);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    if (!await IsLoginFormVisibleAsync() && !await IsLikelyAntiBotPageAsync())
                        return;

                    await Page.WaitForTimeoutAsync(500);
                }
            }

            throw new InvalidOperationException("Login did not complete (still seeing the login form).");
        }
    }

    private Task<bool> IsIdasLoginPageAsync()
    {
        var url = Page.Url ?? string.Empty;
        return Task.FromResult(
            url.Contains("idas.uestc.edu.cn", StringComparison.OrdinalIgnoreCase)
            && url.Contains("/authserver/login", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> IsDuplicateLoginPageAsync()
    {
        try
        {
            return await Page.Locator("text=当前用户存在重复登录的情况").CountAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleDuplicateLoginAsync(string targetUrl)
    {
        // The server indicates the previous session was kicked. It requires a follow-up navigation
        // to "continue" before the grade pages become available again.
        try
        {
            var continueLink = Page.Locator("a:has-text(\"点击此处\")").First;
            if (await continueLink.CountAsync() > 0)
            {
                await continueLink.ClickAsync();
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
        }
        catch
        {
            // ignore
        }

        var retryResponse = await Page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForAntiBotClearAsync(retryResponse);
    }

    private async Task PerformIdasUsernamePasswordLoginAsync(EamsCredentials credentials)
    {
        // If IDAS "rememberMe" is active, the login page can auto-redirect. Avoid unnecessary typing.
        try
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
        catch
        {
            // ignore
        }

        if (!await IsIdasLoginPageAsync())
            return;

        // The IDAS page often defaults to QR/WeChat login and keeps the username/password form hidden.
        // Try to switch it on using the site's own JS helper.
        await Page.EvaluateAsync(
            """
            () => {
              try {
                if (typeof showTabHeadAndDiv === 'function') {
                  showTabHeadAndDiv('userNameLogin', 1);
                  return;
                }
              } catch {}
              try {
                const a = document.querySelector('#userNameLogin_a');
                if (a) a.click();
              } catch {}
            }
            """);

        var form = Page.Locator("form#pwdFromId").First;
        await form.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });

        var user = form.Locator("input[name='username'], input#username").First;
        var password = form.Locator("input#password, input[name='passwordText'], input[type='password']").First;

        // Enable "one week free" so sessions persist across runs.
        try
        {
            var rememberMe = form.Locator("input#rememberMe[type='checkbox']").First;
            if (await rememberMe.CountAsync() > 0 && await rememberMe.IsVisibleAsync())
            {
                if (!await rememberMe.IsCheckedAsync())
                    await rememberMe.CheckAsync();
            }
        }
        catch
        {
            // ignore
        }

        // Make sure the password input is editable (the site uses readonly + custom keyboard).
        await password.EvaluateAsync(
            """
            el => {
              try { el.removeAttribute('readonly'); } catch {}
              try { el.readOnly = false; } catch {}
              try { el.disabled = false; } catch {}
            }
            """);

        var submit = form.Locator("#login_submit, a[onclick*='startLogin'], button[type='submit'], input[type='submit']").First;

        await user.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await password.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await user.ClickAsync();
        await user.FillAsync(string.Empty);
        await user.PressSequentiallyAsync(credentials.Account, new LocatorPressSequentiallyOptions { Delay = 30 });

        await password.ClickAsync();
        await password.FillAsync(string.Empty);
        await password.PressSequentiallyAsync(credentials.Password, new LocatorPressSequentiallyOptions { Delay = 30 });

        // Ensure the hidden "saltPassword" gets updated by the page scripts.
        await password.EvaluateAsync(
            """
            (el, v) => {
              try { el.value = v; } catch {}
              try { el.dispatchEvent(new Event('input', { bubbles: true })); } catch {}
              try { el.dispatchEvent(new Event('change', { bubbles: true })); } catch {}
              try { el.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, key: 'Enter' })); } catch {}
            }
            """,
            credentials.Password);

        await submit.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // If a captcha is required, fail fast (we do not solve captchas in Core).
        var captchaVisible = await form.Locator("#captchaDiv:not(.hide), input#captcha").IsVisibleAsync();
        if (captchaVisible)
            throw new InvalidOperationException("Login requires a captcha on the IDAS page; unable to proceed in headless automation.");

        // If the login page is still present, treat as failure (wrong creds, captcha, or blocked).
        if (await IsIdasLoginPageAsync())
            throw new InvalidOperationException("Login did not complete (still on IDAS login page).");

        // Some deployments show a session-duplication warning after login.
        if (await Page.Locator("text=当前用户存在重复登录的情况").CountAsync() > 0)
        {
            var retryResponse = await Page.GotoAsync(
                $"{_options.BaseUrl}teach/grade/course/person!search.action",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await WaitForAntiBotClearAsync(retryResponse);
        }

        var reloadResponse = await Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForAntiBotClearAsync(reloadResponse);
    }

    private static async Task<string?> SafeInputValueAsync(ILocator locator)
    {
        try
        {
            return await locator.InputValueAsync();
        }
        catch
        {
            return null;
        }
    }

    private async Task WaitForAntiBotClearAsync(IResponse? response)
    {
        if (_options.AntiBotWaitMs <= 0)
            return;

        if (_options.AutoRecoverBadRequest && response?.Status == 400)
        {
            await ClearEamsAntiBotCookiesAsync();
            response = await Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        }

        if (response?.Status == 202 || response?.Status == 400 || await IsLikelyAntiBotPageAsync() || await IsBlankDocumentAsync())
        {
            var start = DateTimeOffset.UtcNow;
            var deadline = start.AddMilliseconds(_options.AntiBotWaitMs);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await IsLoginFormVisibleAsync())
                    return;

                if (!await IsLikelyAntiBotPageAsync() && !await IsBlankDocumentAsync())
                    return;

                await Page.WaitForTimeoutAsync(500);
            }

            if (!_options.Headless && _options.AllowManualLogin && _options.ManualLoginTimeoutMs > _options.AntiBotWaitMs)
            {
                var manualDeadline = start.AddMilliseconds(_options.ManualLoginTimeoutMs);
                while (DateTimeOffset.UtcNow < manualDeadline)
                {
                    if (await IsLoginFormVisibleAsync())
                        return;

                    if (!await IsLikelyAntiBotPageAsync() && !await IsBlankDocumentAsync())
                        return;

                    await Page.WaitForTimeoutAsync(500);
                }
            }

            throw new InvalidOperationException(
                "Blocked by an anti-bot/challenge page. If this persists in headless mode, try clearing the profile's cookies or running once with Headless=false to refresh session state.");
        }
    }

    private static readonly string[] EamsAntiBotCookieNames =
    [
        "Bk8UVSeWhgi3S",
        "Bk8UVSeWhgi3T",
        "enable_Bk8UVSeWhgi3",
        "UqZBpD3n3meQWFk4sx0_",
    ];

    private async Task ClearEamsAntiBotCookiesAsync()
    {
        foreach (var name in EamsAntiBotCookieNames)
        {
            try
            {
                await Context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Name = name });
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task<bool> IsLikelyAntiBotPageAsync()
    {
        // Heuristic for the observed 202 anti-bot page: meta/script tags with r='m'.
        if (await Page.Locator("meta[r='m']").CountAsync() > 0)
            return true;

        if (await Page.Locator("script[r='m']").CountAsync() > 0)
            return true;

        return false;
    }

    private async Task<bool> IsBlankDocumentAsync()
    {
        try
        {
            return await Page.EvaluateAsync<bool>(
                "() => !!document.body && document.body.children.length === 0 && (document.body.textContent || '').trim().length === 0");
        }
        catch
        {
            return false;
        }
    }

    private async Task<ILocator> ResolveTableAsync(string containsText)
    {
        var preferred = Page.Locator($"table:has-text(\"{containsText}\")").First;
        try
        {
            await preferred.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
            return preferred;
        }
        catch (TimeoutException)
        {
            // Fall back to the first table, matching the legacy spider logic.
            var fallback = Page.Locator("table").First;
            if (await IsLikelyAntiBotPageAsync())
                throw new InvalidOperationException("Unable to locate grade table (likely blocked by an anti-bot/challenge page).");
            if (await IsBlankDocumentAsync())
                throw new InvalidOperationException("Unable to locate grade table (blank document; likely blocked by an anti-bot/challenge page).");

            try
            {
                await fallback.WaitForAsync();
                return fallback;
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Unable to locate grade table on the page.");
            }
        }
    }

    private static bool LooksLikeAllSemesterOption(SemesterOption option)
    {
        return option.Name.Contains("全部", StringComparison.OrdinalIgnoreCase)
               || option.Name.Contains("all", StringComparison.OrdinalIgnoreCase)
               || option.Name.Contains("请选择", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        await FlushDiagnosticsAsync();
        if (_context is not null)
            await _context.DisposeAsync();

        _playwright?.Dispose();
    }

    private static string? NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return null;

        var trimmed = channel.Trim();
        if (trimmed.Equals("chromium", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("bundled", StringComparison.OrdinalIgnoreCase))
            return null;

        return trimmed;
    }

    private async Task<IBrowserContext> TryLaunchBrowserWithFallbackAsync(
        IPlaywright playwright,
        string primaryChannel,
        string fallbackChannel,
        string userDataDir,
        BrowserTypeLaunchPersistentContextOptions options)
    {
        // 首先尝试使用主通道 (Chrome)
        try
        {
            // 应用配置选项到 Chrome
            var chromeOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                Channel = primaryChannel,
                Headless = options.Headless,
                SlowMo = options.SlowMo,
                BypassCSP = options.BypassCSP,
            };
            ApplyOptionsToLaunch(chromeOptions);

            return await LaunchWithOptionsAsync(playwright, primaryChannel, userDataDir, chromeOptions);
        }
        catch (Exception ex) when (ex.Message.Contains("Executable") || ex.Message.Contains("browser") || ex.Message.Contains("Driver"))
        {
            // Chrome 未找到，尝试回退到 Edge
            Console.WriteLine($"⚠️  Chrome 未找到，自动回退到 Microsoft Edge...");

            // 创建 Edge 配置
            var edgeOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                Channel = fallbackChannel,
                Headless = options.Headless,
                SlowMo = options.SlowMo,
                BypassCSP = options.BypassCSP,
            };

            // 应用所有配置选项
            ApplyOptionsToLaunch(edgeOptions);

            return await LaunchWithOptionsAsync(playwright, fallbackChannel, userDataDir, edgeOptions);
        }
    }

    private async Task<IBrowserContext> LaunchWithOptionsAsync(
        IPlaywright playwright,
        string channel,
        string userDataDir,
        BrowserTypeLaunchPersistentContextOptions options)
    {
        // 构建启动参数
        var args = new List<string>();
        if (_options.Headless && _options.HeadlessImplementation == HeadlessImplementation.ChromiumArg)
            args.Add(_options.ChromiumHeadlessArg);

        if (_options.ApplyLegacySpiderArgs)
        {
            args.Add("--disable-gpu");
            args.Add("--no-sandbox");

            if (_options.WindowWidth > 0 && _options.WindowHeight > 0)
                args.Add($"--window-size={_options.WindowWidth},{_options.WindowHeight}");

            args.Add("--disable-blink-features=AutomationControlled");
        }

        if (args.Count > 0)
            options.Args = args;

        return await playwright.Chromium.LaunchPersistentContextAsync(
            userDataDir: userDataDir,
            options: options);
    }

    private void ApplyOptionsToLaunch(BrowserTypeLaunchPersistentContextOptions options)
    {
        if (_options.NoViewport)
        {
            options.ViewportSize = ViewportSize.NoViewport;
        }
        else if (_options.WindowWidth > 0 && _options.WindowHeight > 0)
        {
            options.ViewportSize = new ViewportSize { Width = _options.WindowWidth, Height = _options.WindowHeight };
            options.ScreenSize = new ScreenSize { Width = _options.WindowWidth, Height = _options.WindowHeight };
        }

        var userAgent = ResolveUserAgent();
        if (!string.IsNullOrWhiteSpace(userAgent))
            options.UserAgent = userAgent;

        if (_options.ExtraHttpHeaders is not null && _options.ExtraHttpHeaders.Count > 0)
            options.ExtraHTTPHeaders = new Dictionary<string, string>(_options.ExtraHttpHeaders);
    }

    private string? ResolveUserAgent()
    {
        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
            return _options.UserAgent;

        if (!_options.Headless || !_options.AutoFixHeadlessUserAgent)
            return null;

        return GetHeadlessSafeUserAgent();
    }

    private static string GetHeadlessSafeUserAgent()
    {
        // Empirically, EAMS/IDAS blocks the default HeadlessChrome UA token.
        // Use a conservative, platform-appropriate Chrome UA without "HeadlessChrome".
        const int chromeMajor = 120;

        if (OperatingSystem.IsWindows())
            return $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromeMajor}.0.0.0 Safari/537.36";

        if (OperatingSystem.IsMacOS())
            return $"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromeMajor}.0.0.0 Safari/537.36";

        return $"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromeMajor}.0.0.0 Safari/537.36";
    }

    private static Task ApplyStealthInitScriptsAsync(IBrowserContext context)
    {
        // Minimal, targeted headless patches. Avoid overriding too much to keep compatibility.
        const string script = """
// navigator.webdriver -> undefined
try {
  Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
} catch {}

// window.chrome
try {
  if (!window.chrome) window.chrome = {};
  if (!window.chrome.runtime) window.chrome.runtime = {};
} catch {}

// languages
try {
  if (!navigator.languages || navigator.languages.length === 0) {
    Object.defineProperty(navigator, 'languages', { get: () => ['zh-CN', 'zh', 'en-US', 'en'] });
  }
} catch {}

// plugins
try {
  if (!navigator.plugins || navigator.plugins.length === 0) {
    const fakePlugins = [
      { name: 'Chrome PDF Plugin', filename: 'internal-pdf-viewer', description: 'Portable Document Format' },
      { name: 'Chrome PDF Viewer', filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai', description: '' },
      { name: 'Native Client', filename: 'internal-nacl-plugin', description: '' },
    ];
    Object.defineProperty(navigator, 'plugins', { get: () => fakePlugins });
  }
} catch {}

// permissions.query for notifications
try {
  const originalQuery = navigator.permissions && navigator.permissions.query;
  if (originalQuery) {
    navigator.permissions.query = (parameters) => {
      if (parameters && parameters.name === 'notifications') {
        return Promise.resolve({ state: Notification.permission });
      }
      return originalQuery(parameters);
    };
  }
} catch {}

// window.outerWidth/outerHeight
try {
  if (window.outerWidth === 0) {
    Object.defineProperty(window, 'outerWidth', { get: () => window.innerWidth });
  }
  if (window.outerHeight === 0) {
    Object.defineProperty(window, 'outerHeight', { get: () => window.innerHeight + 85 });
  }
} catch {}
""";

        return context.AddInitScriptAsync(script);
    }

    private void AttachDiagnostics(IPage page, string dir)
    {
        Directory.CreateDirectory(dir);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        _diagnosticFilePath = Path.Combine(dir, $"{timestamp}_diagnostics.log");

        void Enqueue(string line)
        {
            _diagnosticLines.Enqueue($"{DateTimeOffset.Now:O}\t{line}");
        }

        page.Console += (_, msg) =>
        {
            Enqueue($"console[{msg.Type}]\t{msg.Text}");
        };
        page.PageError += (_, ex) =>
        {
            Enqueue($"pageerror\t{ex}");
        };
        page.Request += (_, req) =>
        {
            if (req.ResourceType != "document")
                return;

            var headers = string.Join("; ", req.Headers.Select(kv => $"{kv.Key}={kv.Value}"));
            Enqueue($"request\t{req.Method}\t{req.Url}\t{headers}");
        };
        page.RequestFailed += (_, req) =>
        {
            if (req.ResourceType != "document")
                return;

            Enqueue($"requestfailed\t{req.Method}\t{req.Url}\t{req.Failure}");
        };
        page.Response += async (_, resp) =>
        {
            if (resp.Request.ResourceType != "document")
                return;

            Enqueue($"response\t{resp.Status}\t{resp.Url}");

            if (resp.Status is 202 or 400 or 403 or 500)
            {
                try
                {
                    var body = await resp.TextAsync();
                    Enqueue($"response-body[{resp.Status}]\tlen={body.Length}");
                }
                catch (Exception ex)
                {
                    Enqueue($"response-body[{resp.Status}]\tERROR\t{ex.GetType().Name}: {ex.Message}");
                }
            }
        };
    }

    private async Task FlushDiagnosticsAsync()
    {
        var path = _diagnosticFilePath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (_diagnosticLines.IsEmpty)
            return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        // Drain queue.
        var lines = new List<string>();
        while (_diagnosticLines.TryDequeue(out var line))
            lines.Add(line);

        if (lines.Count == 0)
            return;

        await File.AppendAllLinesAsync(path, lines);
    }
}
