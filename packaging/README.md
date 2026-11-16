# Starshot HDR Fork 测试包

当前测试版本 `2.6.5-hdr.1`，Windows x64。源自 loliri/Starshot；许可证及原作者署名保留。

## 构建

需要 .NET 10 SDK、Windows SDK、Visual C++ Build Tools（默认 v143，可用参数覆盖）及 Inno Setup。编译器不属于运行时依赖。

```powershell
./packaging/build.ps1 -IsccPath 'C:/path/to/Inno Setup/ISCC.exe'
```

脚本构建自包含 Release（保留上游裁剪及 ReadyToRun）、便携 ZIP 和离线安装器，输出到 `build/artifacts/`，附 SHA256。已准备相应 **Release** 还原资产时可传 `-NoRestore`；Debug 还原不包含 Crossgen2/ILLink 所需包。必要时用 `-MSBuildPath` 指定 MSBuild.exe。仅替换 `build/package/` 暂存目录；输出、工具和样本不提交 Git。

本机使用官方 Inno Setup 7.1.1 x64 编译器，SHA256 `8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0`。官方安装程序可用 `/PORTABLE=1 /CURRENTUSER /VERYSILENT /NOICONS /DIR=<工具目录>` 提取便携编译器，不注册编译器卸载项或文件关联。

## 安装与数据

- 安装名称：**Starshot HDR Fork**；默认安装到 `%LOCALAPPDATA%\Programs\Starshot HDR Fork`，使用独立卸载项，不需要管理员权限。
- 安装版配置、日志和缓存：`%LOCALAPPDATA%\Starshot HDR Fork`。便携版配置在解压根目录；默认日志/缓存仍在上述独立目录。
- 默认截图目录：`图片\Starshot HDR Fork`。原版配置不自动迁移；卸载保留配置和截图。
- 实例标识、快捷方式、自启项使用 `Starshot.HDRFork`。与原版同开时全局热键仍可能冲突，比较时请只运行一份。
- 自动更新、手动上游更新、后台统计心跳已停用。安装器包含完整应用，无在线更新源或遥测脚本。OCR/批量 CLI 等原有按需资源下载不属于更新，仍保留。

## HDR 修正与验收边界

普通 HDR→SDR 使用共享高光压缩、SDR 白归一化及 sRGB 编码；映射参数按 `500/240` 比例校准，默认白电平识别和界面数值不变。查看器导出不再混入显示器亮度或色彩矩阵。批量普通 PNG/JPEG 输出、区域预览/复制和截图提示缩略图采用该映射。原始 HDR 保存保留 HDR 帧；纯 SDR 捕获只恢复桌面白电平。

PNG 必须同时具有 BT.2020/PQ cICP 标记才按 HDR10 解码。JXR/HEIC 等系统解码格式的 HDR 支持取决于系统是否输出线性浮点位图；不是对全部 HDR 格式（例如 HLG）的支持承诺。批量同格式的跳过/覆盖规则沿用原行为，要从 HDR PNG 得到 SDR PNG 请选“重命名”输出副本。

Ultra HDR JPEG 的 SDR 底图及增益图尚未纳入本轮修正，不能据此验收所有 JPEG 路径。原有 PNG 内容亮度元数据问题另记于调查记录。样本只覆盖一个真实场景及合成梯度；自然观感仍需要更多场景。

已通过 Debug/Release 构建、GPU/编解码回归，以及完整 Release 包的隐藏启动和单实例隔离检查；原版与测试版进程可以并存。桌面自动化连接不可用，窗口交互、热键、实时 HDR 截图、安装/卸载往返尚待人工验收。本包未签名，用于本地测试，不作为已全面验收的正式发布。
