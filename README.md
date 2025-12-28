# UESTCJWCWatchdog

[![CI](https://github.com/karanocave/UESTCJWCWatchdog/actions/workflows/ci.yml/badge.svg)](https://github.com/karanocave/UESTCJWCWatchdog/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/karanocave/UESTCJWCWatchdog)](https://github.com/karanocave/UESTCJWCWatchdog/releases/latest)

跨平台的 UESTC 教务系统（EAMS）成绩查询/监控工具：提供桌面 GUI（AvaloniaUI）与 CLI 两种使用方式，默认无头模式运行，并支持登录态保留与成绩更新通知（ntfy.sh）。

> 声明：本项目为第三方工具，与学校官方无关；请遵守学校相关规定与平台条款，自行承担使用风险。

<img width="1082" height="748" alt="image" src="https://github.com/user-attachments/assets/04ab2acc-9cae-4a94-8838-68da07b17946" />


## 功能概览

- **成绩查询**：查询“期末/总评”和“平时”两类成绩，并用表格展示。
- **登录态保持**：使用浏览器持久化 Profile（`user-data`）保存会话，避免每次查询都走完整登录流程；会话失效时可自动重登。
- **无头模式**：默认后台运行（可切换为显示浏览器窗口）。
- **自动选择学期**：默认按本地时间推导“当前学期”，也可手动指定学年/学期。
- **自动刷新**：可选 30/60/120 分钟间隔自动重新查询。
- **成绩更新通知**：检测到新增/更新后，通过 **ntfy.sh** 推送消息（支持“发送测试消息”）。

## 快速开始（GUI）

前置要求：

- `.NET 10 SDK`
- **Chrome（推荐）**：GUI 内默认使用“系统 Chrome”，也可切换到“内置 Chromium”

启动：

```bash
dotnet run --project src/Watchdog.App
```

使用流程：

1. 打开「设置」页，填写账号与密码（密码可选，但用于会话失效时自动重登）。
2. （可选）勾选“记住密码”（仅保存在本机）。
3. （可选）开启 ntfy 推送：复制/打开订阅地址，或点“发送测试消息”验证。
4. 回到「查询」页，点击“查询成绩”。

## 快速开始（CLI）

1) 准备 `.env`（必需）：

```bash
cp .env.example .env
```

填写 `account` 与 `password` 后运行：

```bash
dotnet run --project src/Watchdog.Cli -- --headless true --channel chrome
```

常用参数（节选）：

- `--semester <id>`：手动指定学期 ID（默认自动推导当前学期）
- `--headless true|false`：无头模式
- `--channel chrome|chromium`：使用系统 Chrome 或内置 Chromium
- `--debug-dir <dir>`：出错时 dump 页面/截图/日志到目录

更完整的可选环境变量说明见 `.env.example`。

## 通知（ntfy.sh）

- 启用后会为你生成一个随机 topic，并在 GUI 中显示订阅地址（形如 `https://ntfy.sh/<topic>`）。
- **只要检测到成绩新增/更新**（自动刷新或手工查询均可），就会推送一条消息。
- 也可用命令行直接测试：

```bash
curl -d "Hi" ntfy.sh/<topic>
```

## 数据目录与隐私

本项目会在系统应用数据目录下保存设置与缓存（路径随系统不同而不同）：

- `settings.json`：GUI 设置（若勾选“记住密码”，密码会以明文保存于本机文件中）
- `profiles/<account>/state.json`：上一次查询结果（用于离线展示/对比差异）
- `profiles/<account>/storage-state.json`：Playwright StorageState 导出
- `profiles/<account>/user-data/`：浏览器持久化 Profile（Cookies/LocalStorage 等，用于登录态保持）

清除登录态：

- GUI：「设置」→"清除登录状态"，或手动删除对应账号目录下的 `user-data/` 与 `storage-state.json`。

## macOS 用户注意

如果你从 GitHub Releases 下载了 macOS 版本，首次运行时可能会看到 Gatekeeper 警告：

**"无法打开 Watchdog.App.app，因为无法验证开发者。"**

### 解决方法（任选其一）：

1. **右键打开（推荐）**：
   - 右键点击 `Watchdog.App.app` → 选择「打开」→ 点击「打开」
   - 之后就可以正常双击打开了

2. **系统设置允许**：
   - 打开「系统设置」→「隐私与安全性」
   - 找到被阻止的消息 → 点击「仍要打开」

3. **移除隔离属性**（高级）：
   ```bash
   xattr -cr /path/to/Watchdog.App.app
   ```

详细说明见：[macOS 签名文档](docs/MACOS_SIGNING.md)

## 开发与构建

- 解决方案：`UESTCJWCWatchdog.sln`
- 项目结构：
  - `src/Watchdog.Core`：核心逻辑（Patchright/Playwright 自动化、学期算法、解析与存储）
  - `src/Watchdog.App`：Avalonia GUI
  - `src/Watchdog.Cli`：CLI
- 构建：`dotnet build`
- 发布流程见：`RELEASING.md`

