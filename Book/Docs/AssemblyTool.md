## AssemblyTool 编译与热更工具说明

- **文件位置**: `Unity/Assets/Scripts/Editor/Assembly/AssemblyTool.cs`
- **用途**: 提供 Unity 菜单与快捷键，实现一键编译热更程序集、复制热更 DLL、根据 `GlobalConfig` 自动切换代码模式与构建类型，并管理需要忽略/启用的 `.asmdef`。

### 功能总览
- **一键编译 (F6)**: 刷新资源 → 切换代码模式 → 刷新构建类型 → 编译脚本为 DLL → 复制热更 DLL/PDB → 重新生成工程文件。
- **一键热重载 (F7)**: 运行态下调用 `CodeLoader.Instance?.Reload()` 热重载代码。
- **代码模式切换**: 根据 `Resources/GlobalConfig` 中的 `CodeMode` 自动启用/禁用相关 `.asmdef`，支持 Client / Server / ClientServer 三种模式。
- **构建类型切换**: 根据 `GlobalConfig.BuildType` 设置 `EditorUserBuildSettings.development`。
- **热更 DLL 分发**: 将指定程序集（`DllNames`）的 DLL/PDB 从构建输出目录复制到运行时加载目录，并以 `.bytes` 扩展名保存。

### 快速使用
- **编译**: 菜单 `ET/Compile` 或按 `F6`。
- **热重载**: 菜单 `ET/Reload` 或按 `F7`（需处于运行态且 `CodeLoader.Instance` 非空）。

### 主要入口
- `MenuItem("ET/Compile _F6")` → `DoCompile()`
- `MenuItem("ET/Reload _F7")` → `CodeLoader.Instance?.Reload()`

### 编译流程 (DoCompile)
1. 强制刷新资源：避免关闭 Auto Refresh 或时间戳不准导致编译旧代码。
2. 刷新代码模式：读取 `GlobalConfig.CodeMode`，按模式启用/禁用对应 `.asmdef`。
3. 刷新构建类型：读取 `GlobalConfig.BuildType`，设置 `EditorUserBuildSettings.development`。
4. 编译 DLL：通过 `PlayerBuildInterface.CompilePlayerScripts(...)` 生成目标平台程序集，附加编译宏 `UNITY_COMPILE`，并在 Debug/Development 下附加开发构建选项。
5. 复制热更 DLL/PDB：清空热更代码目录后，将 `DllNames` 列表中的 DLL/PDB 复制为 `.bytes` 至热更目录。
6. 生成工程文件：`BuildHelper.ReGenerateProjectFiles()`。

### 代码模式切换 (RefreshCodeMode)
从 `Resources.Load<GlobalConfig>("GlobalConfig")` 读取 `CodeMode`，支持：
- **Client**: 仅启用客户端侧 `.asmdef`，禁用服务端相关。
- **Server**: 仅启用服务端侧 `.asmdef`，启用必要的 View/Client 侧忽略配置。
- **ClientServer**: 同时启用客户端与服务端通用生成目录，关闭单端专属生成目录。

对应方法：
- `EnableUnityClient()`
- `EnableUnityServer()`
- `EnableUnityClientServer()`

这些方法内部通过 `EnableAsmdef(...)` 与 `DisableAsmdef(...)` 来管理具体的 `.asmdef` 文件。

### 构建类型切换 (RefreshBuildType)
- 从 `GlobalConfig.BuildType` 读取当前构建类型。
- 当 `BuildType == Debug` 时：`EditorUserBuildSettings.development = true`，否则为 `false`。

### 编译实现 (CompileDlls)
- 将当前线程同步上下文切换为 Unity 主线程的 `SynchronizationContext`，以适配运行时编译流程；编译完成后恢复。
- 依据当前 `activeBuildTarget`/`BuildTargetGroup` 生成编译设置：
  - `extraScriptingDefines`: 含 `UNITY_COMPILE`
  - `options`: Debug/Release 对应 Development/None
- 调用 `PlayerBuildInterface.CompilePlayerScripts(settings, Define.BuildOutputDir)` 产出程序集。
- 返回是否成功（判断 `result.assemblies.Count > 0`）。

### 热更 DLL 复制 (CopyHotUpdateDlls)
- 先 `FileHelper.CleanDirectory(Define.CodeDir)` 清空热更代码目录。
- 遍历 `DllNames`，从 `Define.BuildOutputDir` 复制：
  - `${name}.dll` → `${Define.CodeDir}/${name}.dll.bytes`
  - `${name}.pdb` → `${Define.CodeDir}/${name}.pdb.bytes`
- 完成后 `AssetDatabase.Refresh()`。

默认 `DllNames` 包含：
- `Unity.Hotfix`
- `Unity.HotfixView`
- `Unity.Model`
- `Unity.ModelView`

如需新增热更程序集，将名称加入 `DllNames` 即可，并确保对应程序集在构建输出目录产出。

### .asmdef 管理策略
- `EnableAsmdef(string asmdefFile)`：
  - 从 `Assets/Settings/IgnoreAsmdef/` 中找到同名源文件（`.DISABLED` 作为后缀的模板），复制到 `Assets/Scripts/...` 目标位置。
  - 若目标已存在且最后写入时间一致，则跳过复制。
  - 若找不到源文件，会给出错误提示，请检查项目完整性。
- `DisableAsmdef(string asmdefFile)`：删除目标 `.asmdef` 及其 `.meta`，达到“忽略编译”的效果。

这些方法被 `EnableUnityClient/Server/ClientServer` 调用，分别对以下路径下的 `.asmdef` 进行启用/禁用（示例）：
- `Assets/Scripts/Model/Generate/(Client|Server|ClientServer)/Ignore.asmdef`
- `Assets/Scripts/Model/(Client|Server)/Ignore.asmdef`
- `Assets/Scripts/Hotfix/(Client|Server)/Ignore.asmdef`
- `Assets/Scripts/(ModelView|HotfixView)/Client/Ignore.asmdef`

### 依赖项与前置条件
- 资源配置：`Resources/GlobalConfig`（提供 `CodeMode`、`BuildType`）。
- 常量/辅助类（需在工程内存在并正确配置）：
  - `Define.BuildOutputDir`：编译输出目录。
  - `Define.CodeDir`：热更 DLL 运行时加载目录。
  - `FileHelper.CleanDirectory(string)`：清空目录。
  - `BuildHelper.ReGenerateProjectFiles()`：重新生成工程文件。
  - `CodeLoader.Instance`：运行态热重载入口。
- Unity 版本：需支持 `UnityEditor.Build.Player` 的 `PlayerBuildInterface` API。

### 常见问题 (FAQ)
- **F6 编译无反应/失败**：
  - 检查 Console 错误日志。
  - 确认 `Define.BuildOutputDir` 存在且可写。
  - 确认当前 `BuildTarget`/`BuildTargetGroup` 与工程设置匹配。
  - 关闭的 Auto Refresh 会被此工具强制刷新，但若有外部磁盘同步延迟，需稍等再重试。
- **F7 热重载无效**：
  - 需处于运行态（`Application.isPlaying == true`）。
  - 确认 `CodeLoader.Instance` 非空且实现了 `Reload()`。
- **复制 DLL 失败**：
  - 检查 `Define.CodeDir` 是否存在、权限是否足够。
  - 确认 `DllNames` 对应的 DLL/PDB 已产出在 `Define.BuildOutputDir`。
- **启用/禁用 .asmdef 不生效**：
  - 检查 `Assets/Settings/IgnoreAsmdef/` 下是否有对应源文件（`.DISABLED`）。
  - 修改后等待 Unity 刷新或手动 `Assets → Refresh`。

### 扩展与定制
- **新增热更程序集**：把名称加入 `AssemblyTool.DllNames`，确保能被 `CompilePlayerScripts` 产出。
- **扩展编译宏**：在 `CompileDlls()` 的 `extraScriptingDefines` 中追加自定义宏。
- **平台差异处理**：可基于 `activeBuildTarget` 定制差异化复制/筛选策略。
- **自定义菜单与快捷键**：调整 `MenuItem` 属性（示例：`ETMenuItemPriority.Compile` 控制顺序）。

### 变更影响与注意事项
- `.asmdef` 的启用/禁用会触发 Unity 重新编译，注意在大工程中可能带来较长的等待时间。
- 热更 DLL 复制包含 PDB，有助于运行时堆栈行号定位；若不需要，可在复制逻辑中移除 PDB 相关代码。
- 若你启用了版本控制，建议确保 `Assets/Settings/IgnoreAsmdef/` 下的模板文件纳入版本库，以免因缺失导致工具报错。

—— 完 —— 