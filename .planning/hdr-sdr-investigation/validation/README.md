# HDR → SDR 回归与比例校准验证

Windows x64 / .NET 10。这个小型控制台程序直接加载项目的 ImageLoader、TonemapToSdr、SdrToneMapEffect 和 PNG 编码器，不启动 Starshot 窗口。内部入口通过反射访问，无须为验证修改产品可见性；没有增加测试框架或产品依赖。

从仓库根目录执行（先准备与产品相同的 NuGet 依赖）：

```powershell
dotnet restore .planning/hdr-sdr-investigation/validation/Probe.csproj -p:Platform=x64
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64 -p:MSBuildEnableWorkloadResolver=false --no-restore -m:1 -nr:false -v:minimal
dotnet build .planning/hdr-sdr-investigation/validation/Probe.csproj -c Debug -p:Platform=x64 -p:MSBuildEnableWorkloadResolver=false -p:BuildProjectReferences=false --no-restore -m:1 -nr:false -v:minimal
& '.planning/hdr-sdr-investigation/validation/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Probe.exe' (Resolve-Path 'images_for_test/HDR原图.avif').Path (Join-Path (Get-Location) 'build/hdr-sdr-validation/calibrated') (Join-Path (Get-Location) 'build/hdr-sdr-validation/calibration-reference')
```

三个参数分别为 HDR 原图路径、输出目录、同一原图的校准前参考图目录。输出 `calibrated-120.png`、`calibrated-240.png`、`calibrated-480.png`，同名输出会被覆盖；不要将输出目录设为用户原图或参考图目录。bin/obj 和 build 均已被仓库忽略。项目包版本与当前产品一致，产品依赖升级时一并核对。

本次参考图在修改比例前用实际项目转换函数生成，分别为 `fixed-250.png`、`fixed-500.png`、`fixed-1000.png`，保存在 `build/hdr-sdr-validation/calibration-reference`；此前已由用户评价的500图另保留在 `after/fixed-500.png`。这些为本机私有样本生成物，不提交仓库。换用其他HDR原图或清除build后，须用校准前版本为同一原图重建参考，不能复用本场景的文件。

本机缓存只读时，本次实际使用的离线还原方式：

```powershell
$env:NUGET_HTTP_CACHE_PATH=Join-Path (Get-Location) 'build/hdr-sdr-validation/http-cache'
$env:NUGET_SCRATCH=Join-Path (Get-Location) 'build/hdr-sdr-validation/scratch'
dotnet restore .planning/hdr-sdr-investigation/validation/Probe.csproj -p:Platform=x64 -p:RestorePackagesPath=D:/Projects/Starshot/build/hdr-sdr-validation/packages -p:RestoreFallbackFolders=C:/Users/zmj07/.nuget/packages -p:NuGetAudit=false --source C:/Users/zmj07/.nuget/packages -v:minimal
```

## 断言与证据边界

- 新120/240/480档与保存的校准前250/500/1000档参考图逐像素比较；证明按比例移位，无须以重复实现作为唯一验证依据。
- 各档调用者缓存峰值与转换时自动测量峰值的结果逐字节一致。这覆盖查看器和批量的转换调用约定，不替代两个窗口的操作测试。
- 实际 ImageSaver PNG 编码后用系统解码器回读，RGBA像素逐字节一致。
- 共享线性结果经显示白80/240/480 nits缩放并逆变换后，与直接文件像素最多差1；覆盖归一化单位和量化，不覆盖显示器色彩矩阵或屏幕观感。
- HDR映射入口的低亮中性阶梯与校准后目标白的独立标量sRGB公式最多差1。
- 原本为SDR的捕获内容使用独立白电平恢复入口：0/14.4/80/120/240 nits在实测白240下与未校准sRGB公式最多差1，白保持255，防止观感校准误伤普通桌面截图。
- 峰值1000、目标240 nits的中性阶梯严格递增、RGB相等；240/400/600/800/1000 nits不再全部截成255。

2026-11-16 首轮及比例校准后产品与Probe均构建通过，0警告0错误；以上断言均通过。新240档PNG与用户评价过的旧500档PNG文件哈希完全相同。原图测得MaxCLL=992.25、MaxFALL=228.82645。详细结论、样本哈希和局限见上一级 `findings.md`、`progress.md`。

2026-11-17 增加实际 HDR PNG/AVIF/JXL/JXR 保存→回读→SDR 映射验证（真实样本缩至 320×180），以及合成 SDR PNG/JPEG/AVIF/JXL 回读。HDR 浮点像素的归一化 RMSE 分别为 0.5447%/0.0530%/0.0291%/0%；转 SDR 后相对原 HDR 直接映射的平均字节误差分别为 0.5524/0.0546/0.0268/0。普通 SDR PNG/JXL 像素完全一致，JPEG/AVIF 平均字节误差小于 0.3。

追加检查直接调用区域截图的实际预览方法：HDR 结果与共享映射逐字节一致，8bit SDR 原对象复用，HDR 桌面捕获的 SDR 白在不同映射目标下仍为 255。独立构造 BT.2020/sRGB cICP PNG，确认它不被错误识别为 PQ HDR。

自然观感、饱和色/宽色域映射、多场景真实样本、HEIC/HLG、Ultra HDR、窗口交互、热键和实时 HDR 截图尚未完整验证。格式编解码验证不替代批量转换窗口的操作测试。不得将本程序 PASS 解释为这些范围已验收。
