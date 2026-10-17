# AGENT.md

## 协作语言

- PR 标题、PR 正文和代码注释尽量使用中文；技术标识符、命令、协议字段和上游固定名称保留原文。

## 编码约定

- C# 源码文件：使用 UTF-8 BOM，本地工作区统一 CRLF，Git 索引统一 LF（由 `.gitattributes` 约束）。
- `Changelog.cs`：使用 UTF-8 BOM，本地工作区 CRLF。
- `.csproj`、`.manifest`、`.ico`：不要重新编码，保持原样。
- PowerShell 脚本：尽量使用 ASCII；如果必须包含中文，脚本文件使用 UTF-8 BOM。
- Python 脚本：通过 PowerShell heredoc 执行时，中文一律写成 `\uXXXX` 转义；写文件时明确使用 `utf-8` 或 `utf-8-sig`。
- GitHub API JSON：使用 `ensure_ascii=False` 并编码为 UTF-8，同时设置 `Content-Type: application/json`。

## 文件修改

- 手动改代码使用 `apply_patch`。
- 批量修改换行或 BOM 使用 Python，不要用 PowerShell heredoc 直接写入中文。
- 更新 `Changelog.cs` 时，在现有顶部插入新版本条目，使用 Python + Unicode 转义，避免中文乱码。
- 不要使用旧的 `changelog_raw.txt` 重建 `Changelog.cs`。
- 项目脚本、配置和文档必须保持环境无关，不得写入用户名、个人目录、固定工作区绝对路径或仅适用于单台设备的配置。
- 检查、测试和构建产物统一放在系统临时目录，完成或失败后都必须清理；除非用户明确要求交付成品，不得保留到项目外的固定目录。

## 版本

- 发布版本必须同时更新 `PcCompanionMonitor.csproj` 和 `CloudXiInstaller.csproj`。
- Windows 发布资产是双用途单文件 `YunXiStatistician.exe`：标准文件名或安装参数进入安装模式，安装为 `云曦PC统计.exe` 后进入主程序模式。
- `CloudXiInstaller.csproj` 仅作为主程序引用的安装支持库，不得独立自包含发布，也不得重新内嵌另一份自包含主程序。

## 发布

- Release 更新时间必须读取 GitHub API 的 `published_at` 并转换为 UTC+8，禁止估算。
- 推送发布标签前必须运行 `scripts/check-release.ps1 <标签>`。预检通过后才能推送标签。
- 发布预检必须读取 GitHub 最新 Release，待发布版本不高于线上最新版本时禁止继续。
- 发布预检必须在项目外临时目录执行还原、构建和打包，完成后清理临时目录并检查项目目录没有新增产物。
- `scripts/check-release.ps1` 只负责预检，不保留正式产物，不执行签名、推送、打标签或创建 Release。
- 仓库不使用自动发布工作流；正式 Release 由维护者在预检通过后手工创建。
- 创建或修改 Release body 使用 Python `urllib`，JSON 使用 `ensure_ascii=False`；不要用 PowerShell 的 `ConvertTo-Json` 处理中文发布说明。

## Windows 签名

- 每次发布只签名最终双用途单文件 `YunXiStatistician.exe`；不得单独签名安装支持库或再次生成第二份自包含主程序。
- Windows 签名必须在最终 EXE 不再发生任何字节修改后执行；签名完成后重新计算 SHA-256，再上传 Release。
- 自签名证书在未建立信任的机器上可能显示 `UnknownError`；校验时确认签名存在、签名证书指纹正确且文件签名后未被修改，不得把系统信任状态作为自动更新条件。
- PFX、私钥、密码、凭据内容和本机证书路径不得写入仓库、脚本、配置、提交记录、GitHub Secrets 或 Release。
- 签名工具和证书必须由发布者从项目外安全位置提供；项目内检查和构建流程不得依赖某台电脑的证书存储、固定 SDK 路径或凭据名称。
