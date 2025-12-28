# macOS 应用签名与 Gatekeeper 说明

## 用户遇到的警告

首次运行时，macOS 可能会显示：

```
无法打开 "Watchdog.App.app"，因为无法验证开发者。
macOS 无法验证此应用是否含有恶意软件。
```

## 解决方案

### 方法 1：右键打开（推荐）

1. **右键点击** `Watchdog.App.app`
2. 选择「打开」
3. 点击「打开」确认
4. 应用会正常打开，之后就不会再被拦截

### 方法 2：系统设置允许

1. 打开「系统设置」→「隐私与安全性」
2. 找到「"Watchdog.App.app" 被阻止」的消息
3. 点击「仍要打开」
4. 之后可以正常双击打开

### 方法 3：移除隔离属性（高级用户）

打开终端，运行：

```bash
xattr -cr /path/to/Watchdog.App.app
```

然后双击应用即可打开。

---

## 开发者：如何避免 Gatekeeper 拦截

### 方案 1：使用 Apple Developer 证书（最佳，需 $99/年）

1. **加入 Apple Developer Program**
   - 访问 https://developer.apple.com/programs/
   - 支付 $99/年

2. **创建证书**
   - 在 Xcode 中：Xcode → Settings → Accounts
   - 选择你的 Apple ID → Manage Certificates
   - 创建「Developer ID Application」证书

3. **导出证书为 .p12 文件**
   - 打开「钥匙串访问」
   - 找到你的证书
   - 右键 → 导出（保存为 .p12 文件，设置密码）

4. **配置 GitHub Secrets**
   - 在仓库设置中添加以下 Secrets：
     - `MACOS_CERTIFICATE`: .p12 文件的 base64 编码内容
     - `MACOS_CERTIFICATE_PASSWORD`: .p12 文件的密码
     - `MACOS_SIGNING_IDENTITY`: 证书身份（如 `Developer ID Application: Your Name (TEAM_ID)`）

5. **生成 base64 编码的证书**
   ```bash
   base64 -i YourCertificate.p12 | pbcopy
   ```

6. **GitHub Actions 会自动使用证书签名**

这样用户下载的应用就不会被 Gatekeeper 拦截！

### 方案 2：Ad-hoc 签名（当前默认）

**优点**：免费
**缺点**：用户仍会看到警告，但可以通过上述方法绕过

当前 workflow 默认使用 ad-hoc 签名，应用会被标记为来自「未知开发者」。

---

## 公证服务（Notarization）- 可选

即使有开发者证书，macOS 10.15+ 还需要公证才能完全避免警告。

### 公证步骤：

1. **在 Apple Developer 网站创建专用密码**
   - 访问 https://appleid.apple.com
   - App 专用密码 → 生成一个用于公证的密码

2. **配置 GitHub Secrets**
   - `APPLE_ID`: 你的 Apple ID 邮箱
   - `APPLE_ID_PASSWORD`: 专用密码
   - `APPLE_TEAM_ID`: 你的团队 ID（10 个字符）

3. **在 workflow 中添加公证步骤**（待实现）

公证后，应用会完全通过 Gatekeeper 检查，不会显示任何警告。

---

## 当前状态

- ✅ 代码签名：Ad-hoc 签名（自动）
- ❌ Apple Developer 签名：未配置
- ❌ 公证：未配置

## 建议

对于个人项目：
- **当前方案足够**：用户只需右键打开一次即可
- **在 README 中说明**：告知用户如何打开应用

如果预算允许：
- **购买 Apple Developer 账号**：$99/年
- **配置签名和公证**：提供最佳用户体验
