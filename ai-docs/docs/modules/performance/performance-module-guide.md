---
type: module-guide
module: performance
layer: runtime
maturity: stable
---

# Performance 模块指南

> 改 `Assets/_Project/Scripts/Runtime/Performance/`、`Runtime/Live2D/`、`Editor/Performance/` 之前读这份。
> 对外怎么调看 [`performance-external-api.md`](performance-external-api.md)，要加东西看
> [`performance-extension-guide.md`](performance-extension-guide.md)。
> 设计定稿见 [`PRP/performance-pipeline/prp.md`](../../../../PRP/performance-pipeline/prp.md) 第 2 节；与源码不符时以源码为准。

## 职责边界

**做**：按 Addressables 地址（演出 id）拉起一段「预制体 + 时间轴」编排的演出——暂停世界、关 Gameplay 输入图、
开黑边与字幕面板、把舞台相机叠到主相机上、时间轴走到「等待输入」标记就停下等确认、长按跳过、
播完 / 跳过 / 取消 / 失败都按「进来前的状态」逐项恢复并广播事件；「只播一次」记存档。
三类挂载点：场景触发区（进入即播）、场景加载即播、对白节点前插播。
编辑器给动画师：新建演出（一步建齐预制体 + 时间轴 + 登记地址）、打开时间轴拖动预览、校验缺项、Play 模式试播。
Live2D 适配层：SDK 不在时整个程序集不参与编译，在时表情走 `CubismExpressionController`、动作走导入的 `AnimationClip`。

**不做**（明确留给人做或后续）：

| 不做 | 归属 |
| --- | --- |
| 下载 / 导入 Live2D Cubism SDK 与任何模型（授权协议要人点） | 人做，SDK 到 `Assets/Live2D/`，模型到 `Art/Live2D/<角色>/` |
| 相机运镜系统、镜头语言预设 | 用时间轴自带 Animation 轨即可满足首版 |
| 配音、口型、语音 | 设计支柱明确不做配音 |
| 演出中的分支选项、多段演出并行、演出内嵌对白 | 未来若需要另设计 |
| 演出中途存档 / 从中途恢复 | 演出期间不存档，重进从头播 |
| 手机端触屏专属操作 | PC 优先，移植阶段再补（`pc-first` 决定） |

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `PerformanceRules` | **纯 C#** 阶段机：`Idle → Playing ⇄ Holding → Finished`；停顿确认、长按跳过计时、结果归类、埋点（`PerformanceRules.cs:19`） | `PerformanceService` 持有单例，`Start/Tick/EnterHold/Confirm/TickSkip/Complete/Skip/Cancel/Fail` |
| `PerformancePolicy` | `readonly struct`：校验后的播放策略快照（可跳过 / 长按秒数 / 时停 / 藏 HUD / 黑边），构造即校验 | 由 `PerformanceStage.BuildPolicy(config)` 产出 |
| `PerformanceSaveData` | 存档分区：`PlayedIds`，版本 1 | `ISaveService.Get<PerformanceSaveData>()` 产出，`HasPlayed`/`MarkPlayed` |
| `PerformanceService` | **对外门面** + `IGameService`：见「数据流」；持时停令牌、输入图、相机叠加、面板、埋点、事件（`PerformanceService.cs:35`） | 根作用域单例；其余模块注入 `IPerformanceService` |
| `PerformanceStage` | 预制体根组件：持 `PlayableDirector` 与舞台相机；`Play/Resume/Stop`；收 `HoldMarker` 通知 → 暂停并 `OnHold`；`director.stopped` → `OnFinished`（`PerformanceStage.cs:19`） | 演出预制体根；服务播放时调用 |
| `PerformanceActor` | 抽象基类：`ExpressionNames`（校验用）、`SetExpression`、`SetVisible`、`GatherPreviewProperties`（编辑器预览属性登记） | 表情轨道绑定它 |
| `SpritePerformanceActor` | 占位演员：`SpriteRenderer` + 「表情名 → Sprite」列表 | 模板工厂默认建的演员 |
| `PerformanceView` | `UIView`（**Panel 层全屏**，`CloseOnCancel = false`），实现 `IPerformanceSubtitleSink`：黑边 / 字幕 / ▼ / 跳过进度环 / 黑场，全 LitMotion unscaled（`PerformanceView.cs:26`） | `IUIService` 按地址 `PerformanceView` 实例化 |
| `PerformanceViewArgs` | 打开面板的参数快照（策略、跳过提示文案、黑边高度、黑场时长、停顿提示符） | 服务组装后传给 `ui.OpenAsync<PerformanceView>` |
| `PerformanceTrigger` | 场景挂载点：`performanceId`（`[PerformanceId]`）、`mode`（`OnEnter`/`OnSceneStart`）、`once`；`OnTriggerEnter(2D)` 只认带 `PerformanceTriggerActor` 的对象（`PerformanceTrigger.cs:17`） | 场景物体；服务由 `PerformanceSceneBinder` 注入 |
| `PerformanceTriggerActor` | 空标记，挂玩家根（同 `DialogueInteractionActor` 的做法，但不依赖 Dialogue） | 场景玩家物体 |
| `PerformanceTriggerRules` | **纯 C#** 静态判定：`ShouldFire(once, hasPlayed, serviceRunning, out reason)`，已播过优先于忙碌 | `PerformanceTrigger.TryFire` 调 |
| `PerformanceSceneBinder` | 入口点（`IStartable`）：启动与 `sceneLoaded` 扫描 `PerformanceTrigger`（含未激活）并 `Bind`；`BootCompletedEvent` 后触发 `OnSceneStart` 的（`PerformanceSceneBinder.cs:21`） | 根作用域入口点 |
| `PerformanceInstaller` | `GameplayInstaller`：事件 broker、配置、规则（工厂式注入 `ITelemetryScope`）、服务、场景绑定（`PerformanceInstaller.cs:26`） | Boot 场景 `GameBootstrap` 物体 |
| `PerformanceConfig` | SO：长按跳过秒数、黑边高度、黑场时长、停顿 / 跳过提示文案、默认策略三开关 | `Data/Performance/PerformanceConfig.asset` |
| Timeline 子命名空间 `Game.Performance.Timeline` | 见下「时间轴轨道」 | — |

### 时间轴轨道（`Runtime/Performance/Timeline/`）

| 类 | 做什么 |
| --- | --- |
| `SubtitleTrack` / `SubtitleClip` / `SubtitleBehaviour` / `SubtitleMixerBehaviour` | 字幕轨。**无绑定**：输出端从 `PlayableDirector` 所在物体上的 `PerformanceStage.SubtitleSink` 取（所以 Director 必须和 `PerformanceStage` 挂同一物体）；混合器只在权重最大片段变化时调 `ShowSubtitle`/`HideSubtitle`（`SubtitleMixerBehaviour.cs:11`） |
| `ExpressionTrack` / `ExpressionClip` / `ExpressionBehaviour` / `ExpressionMixerBehaviour` | 表情轨，`[TrackBindingType(typeof(PerformanceActor))]`；片段变化的边沿调一次 `SetExpression`，片段之间空白保持上一个表情不回默认 | 
| `HoldMarker` | 「等待输入」标记：`Marker, INotification`，`NotificationFlags.TriggerOnce`，不设 `TriggerInEditMode`（拖时间线预览不触发） |
| `IPerformanceSubtitleSink` | 字幕轨道到 UI 的契约接口，运行时由 `PerformanceView` 实现 |

动作轨不走自定义类型：`AnimationTrack` 直接绑演员的 `Animator`；Live2D 模型导入后 `.motion3.json` 会变成 `AnimationClip`，同一条轨道直接播。

## 数据流

```text
PerformanceService.PlayAsync(id, ct)：
  IsRunning → 抛 InvalidOperationException（同 DialogueService），埋 play_rejected(busy)
  instance = assets.InstantiateAsync(id, DontDestroyOnLoad 根)     失败 → 埋 load_failed，抛出
  stage = instance.GetComponent<PerformanceStage>()                缺 → 埋 stage_missing，抛出
  policy = stage.BuildPolicy(config)；rules.Start(id, policy)      埋 started
  记录 Gameplay / Dialogue 图进来前的状态；DisableMap(Gameplay)；EnableMap(Dialogue)
  policy.PauseWorld → worldPause.Acquire(this)
  view = ui.OpenAsync<PerformanceView>(args)；policy.HideHud → SetLayerVisible(Hud/Popup, false)
  AttachCamera（叠到主相机 / Base 退路，见「渲染」）；stage.SetSubtitleSink(view)；订阅 OnHold / OnFinished
  发布 PerformanceStartedEvent；stage.Play()
  每帧（unscaled）：
    finishedPending（时间轴自然停）→ rules.Complete()，跳出循环
    pendingSkip（代码 Skip()）→ rules.Skip() → 停时间轴、清进度环
    confirmRequested（Dialogue/Advance 按下 或 代码 Confirm()）且 Holding → rules.Confirm() → 收 ▼、Resume()
    holdPending（收到 HoldMarker 通知）→ rules.EnterHold() → 显示 ▼
    policy.Skippable → rules.TickSkip(Dialogue/Skip 按住, dt) → view.SetSkipProgress；满 → 结束
  直到 rules.Phase == Finished
  收尾（finally）：摘回调 → stage.Stop() → 拆相机 → 关面板 → 恢复 Hud/Popup 层 → 输入图恢复到进来前 → pause.Dispose()
  Outcome ∈ {Completed, Skipped} → save.Get<PerformanceSaveData>().MarkPlayed(id)
  归还实例；发布 PerformanceEndedEvent；埋 ended(outcome, duration)

挂载点 1（场景触发）：PerformanceTrigger.OnTriggerEnter(2D) 认到 PerformanceTriggerActor → TryFire()
  → PerformanceTriggerRules.ShouldFire(once, hasPlayed, service.IsRunning) → 埋 trigger_fired / trigger_skipped(reason)
  → service.PlayAsync(id).Forget()（不传本物体的销毁令牌：演出挂在 DontDestroyOnLoad 根上，触发器所在场景卸载不打断演出）

挂载点 2（场景开始）：PerformanceSceneBinder 扫描 sceneLoaded 时登记的 OnSceneStart 触发器，
  在 BootCompletedEvent 之后统一触发一次（之前已加载场景）或场景加载时触发（启动完成之后才加载的场景）

挂载点 3（对白节点前插播）：DialogueController.PrepareAsync 前，Visit 变化时先调 PerformBeforeNodeAsync（DialogueController.cs:325）
  → node.PerformanceId 非空 且 Phase == Preparing 且非跳过快进 → Performing = true（HandleKey 与主循环全部让位）
  → performance.PlayAsync(id, ct)（IPerformanceService 经 DialogueInstaller.TryResolve 注入，可为 null）
  → 服务缺席记 Warn + 埋 dialogue 模块的 performance_unavailable；演出抛非取消异常记 Error + 埋 performance_failed，都不阻断对白
  → 完成后 Performing = false，比对 Generation/Visit 后继续 PrepareAsync 摆台词
```

## 依赖方向

| 依赖 | 用来做什么 |
| --- | --- |
| `Game.Core.UI` / `Assets` / `Input` / `Timing` / `Save` / `Telemetry` / `Events` | 面板、实例化、输入图、世界时停、存档、埋点、`BootCompletedEvent` |
| `Unity.Timeline`、`Unity.RenderPipelines.Universal.Runtime`（`Game.Runtime` 新增引用） | 时间轴播放、URP 相机叠加（`UniversalAdditionalCameraData`） |
| `Game.Live2D`（独立 asmdef，`references` 含 `Game.Core`/`Game.Runtime`/`Live2D.Cubism`，见「Live2D 适配」） | 可选的 Live2D 演员实现；`Game.Performance` 不引用它（方向是 Live2D → Performance） |

**`Game.Dialogue → Game.Performance`，反向禁止**：Dialogue 经 `resolver.TryResolve<IPerformanceService>()` 拿服务，
Performance 不认识任何对白名词，输入图常量 `"Dialogue"` 写死在 `PerformanceService.cs:42` 而不是引用 `DialogueService.InputMap`。
`Game.Core` 不认识演出名词。`Game.Editor.Performance` 只引用 `Game.Performance` / `Game.Performance.Timeline`，不反向。

## 世界时停与输入图：责任归属

复用 Dialogue 的思路（`DialogueService.cs` 同款收尾），资源全在 `PerformanceService` 一处：

| 资源 | 归属 | 为什么 |
| --- | --- | --- |
| 世界暂停令牌 | `PerformanceService.RunAsync`（`policy.PauseWorld` 时才 `Acquire`） | 会话级资源，可单段关掉（策略开关） |
| 动作图 | 复用 **Dialogue** 图（常量 `InputMap = "Dialogue"`），不新开 Performance 图 | `GameInput.inputactions` 正被另一会话改动，且确认 / 跳过键位语义与对白一致；只恢复进来前的状态，对白里插播时 Dialogue 图本来就开着 |
| Gameplay 图 | 只在进来前是开的才恢复 | 调用方本来关着（如对白插播、过场）就不擅自打开 |
| HUD / Popup 层 | `HideHud` 时两层一起关（对白框 `DialogueView` 在 Popup 层，Panel 层的演出面板盖不住它） | 目前工程没有别的 `SetLayerVisible` 调用方，收尾直接恢复为可见，不记录进来前状态 |
| 重入保护 | `running` 标志 + `rules` 的 `IsActive` | 同一时刻只允许一段演出（同 DialogueService） |

`PerformanceRules` / `PerformanceStage` / `PerformanceView` 一律不碰 `Time.timeScale` 或输入图，只在 `PerformanceService`。
读动作的三处（`Confirm`、`TickSkip`、`BuildSkipHint` 的键位显示）都带 `// lint-ok`：演出表现层读动作不进确定性模拟，同 `DialogueKeyboardInput` 的例外。

## 渲染：舞台相机以 URP Overlay 叠加

- 舞台内全部对象在图层 **`Performance`**（第 9 槽，`TagManager.asset`）；舞台相机 `renderType = Overlay`、剔除遮罩只含该层、正交。
- `PerformanceService.AttachCamera`（`PerformanceService.cs:403`）：能取到 `Camera.main` 的 `UniversalAdditionalCameraData` 且是
  `Base` 类型、且 `cameraStack` 非 null（渲染器支持叠加）→ 加入相机栈；结束时 `DetachCamera` 移除并改回原始 `renderType`。
- **退路**：主相机缺失 / 就是舞台相机自己 / 无 URP 数据 / 不是 Base / 渲染器不支持叠加 → 舞台相机改 `Base`、深度加 10、
  纯黑清屏，记 Warn + 埋 `camera_stack_unavailable(id, reason)`；`reason` 取值见 `PerformanceService.cs:444`。
- 主相机剔除遮罩要**排除** `Performance` 层，否则会把舞台内容再画一遍（`PerformanceValidator` 会提示 `camera_culling_extra`）。

## Live2D 适配（`Game.Live2D`，SDK 缺席也要零错误）

- `Game.Live2D.asmdef`：`references` = `Game.Core`、`Game.Runtime`、**`Live2D.Cubism`**（官方 CubismUnityComponents 唯一的运行时 asmdef，
  Core/Framework/Rendering 都在里面）；`defineConstraints = ["LIVE2D_CUBISM"]`。约束不满足时 Unity 跳过整个程序集，
  未解析引用不报错（已实测，`Live2DAssemblyInfo.cs` 只是占住目录的空占位）。
- `Game.Editor.Live2DDefineSync`（`Live2DDefineSync.cs:29`，`[InitializeOnLoad]`）：按文件名找 `Live2D.Cubism.asmdef`，
  存在则给当前 `BuildTargetGroup` 加 `LIVE2D_CUBISM`，不存在则移除；**只在状态翻转时写一次 PlayerSettings**，域重载不重复写盘。
  状态锚点：菜单 `21Days/演出/检查 Live2D 符号`，点一下打印 SDK 是否存在与当前符号状态。
- `Live2DPerformanceActor : PerformanceActor`（`#if LIVE2D_CUBISM`，**未经本地编译验证**，按官方源码核对过 API）：
  表情名 = `CubismExpressionData.name` 去掉 `.exp3` 后缀 → 匹配后设 `CurrentExpressionIndex`；`SetVisible` 切
  `CubismRenderController.Opacity`。动作不经这里：导入器把 `.motion3.json` 变成 `AnimationClip`，时间轴 Animation 轨直接绑模型 `Animator`。

## 编辑器工具（`Assets/_Project/Scripts/Editor/Performance/`，`Game.Editor.Performance`）

`Game.Editor.asmdef` 未加 URP Runtime 引用（避免多加一个仅编辑器用的重依赖）：涉及相机类型判断的地方
（`PerformanceTemplateFactory.CreateStageCamera`、`PerformanceValidator.IsOverlayCamera`）按类型名反射取
`UniversalAdditionalCameraData`，用 `SerializedObject` 读写其序列化字段 `m_CameraType`（0=Base，1=Overlay）。

| 类 | 做什么 |
| --- | --- |
| `PerformanceEditorWindow`（菜单 `21Days/演出/演出编辑器`） | 左栏列出 `Prefabs/Performance/*.prefab` 里带 `PerformanceStage` 的（id / 时长 / 字幕表情停顿计数 / 校验状态灯）；右栏「打开时间轴」「定位资产」「校验」「检查 Live2D 符号」+ 新建演出 + Play 模式下「试播」（从 `GameLifetimeScope` 解析 `IPerformanceService`，同 `ReplayWindow` 的分工） |
| `PerformanceTemplateFactory`（静态） | `Create(id, options)` 一步建齐：时间轴（字幕/表情/动作/音效四轨）+ 预制体（`PerformanceStage`+`PlayableDirector`+`StageCamera`+`SpritePerformanceActor`，轨道绑定已接好）+ 全树设 `Performance` 层（层不存在时在 TagManager 第一个空槽建）+ 登记 Addressables `Performance` 组（组不存在时照抄 `UI` 组 schema 新建）。id 只能小写字母/数字/下划线 |
| `PerformanceValidator`（静态） | 纯校验：输入舞台根物体，输出 `List<PerformanceIssue>`——缺 `PerformanceStage`/`PlayableDirector`/时间轴、时长为 0、舞台相机缺失/非 Overlay/剔除多层、物体不在 `Performance` 层、字幕正文为空、表情轨未绑定/表情名不存在、`HoldMarker` 落在 0 秒或末尾外、Addressables 地址未登记 |
| `PerformanceIssue` | 一条问题：`Severity`（Error/Warning）、`Code`（机器可读）、`Message`（中文）、`Context`（可定位对象） |
| `PerformanceStageEditor`（`PerformanceStage` 自定义 Inspector） | 默认字段 + 「打开时间轴」「校验」按钮 + 校验结果 |
| `PerformanceIdDrawer`（`[PerformanceId]` 的 PropertyDrawer） | 从 Addressables `Performance` 组的已登记地址画下拉（含「手动输入…」）；组不存在或当前值未登记时退回文本框 + 红字「未登记」 |
| `SubtitleClipEditor` / `ExpressionClipEditor`（`ClipEditor`） | 片段上直接显示「说话者：正文前 12 字」/ 表情名；正文为空、演员没有该表情时标红 |
| `HoldMarkerEditor`（`MarkerEditor`） | 悬停提示「等待玩家确认」，标记旁画「▼」；落在开头/末尾之外时标红 |
| `Live2DDefineSync` | 见「Live2D 适配」 |

## 接线要求

缺任一项都**不会编译报错**，只会运行时不动或报错：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `PerformanceInstaller`，排在 `DialogueInstaller` 之后；**Config** 拖 `Data/Performance/PerformanceConfig.asset` | 没挂：解析不到 `IPerformanceService`（对白插播记 Warn 跳过）；没拖：记 Error 用默认值顶上 |
| 面板地址 | Addressables UI 组 `PerformanceView` → `Prefabs/UI/PerformanceView.prefab`，10 个字段全接（`letterboxTop`/`letterboxBottom`/`fade`/`subtitleRoot`/`speaker`/`body`/`holdPrompt`/`skipRoot`/`skipLabel`/`skipFill`，`skipFill` 的 Image Type 须为 Filled） | `ui.OpenAsync<PerformanceView>()` 找不到预制体；漏接字段 `Validate()` 逐个点名抛出 |
| 演出资产 | Addressables **Performance** 组：地址 = 预制体名 = 演出 id；预制体 `Prefabs/Performance/<id>.prefab`；时间轴 `Data/Performance/Timelines/<id>.playable`；两者由 `PerformanceTemplateFactory` 一步建齐 | 地址查不到：`InstantiateAsync` 抛出，埋 `load_failed` |
| 图层 | `Performance`（第 9 槽，`TagManager.asset`）；主相机剔除遮罩要**排除**它 | 层不存在：舞台相机剔不到任何东西，画面全黑；主相机没排除：场景内容被舞台相机重叠渲染 |
| 玩家标记 | 玩家根挂 `PerformanceTriggerActor`（一个场景一个） | 场景触发器 `OnTriggerEnter(2D)` 永远不认玩家，不会触发 |
| 触发器 | 场景物体挂 `PerformanceTrigger` + `isTrigger` 的 `Collider`/`Collider2D`（`OnEnter` 模式必须） | 缺碰撞体：进入不触发（`OnSceneStart` 模式不需要碰撞体） |
| 对白插播 | 对白节点 JSON 的 `performance` 字段非空，且 `Tables/Defines/dialogue.xml` 已生成对应字段 | 空串：不插播；服务未注册：记 Warn 埋 `performance_unavailable` 直接显示台词 |
| 入口 | 从 Boot → 标题「开始」进场景才有演出服务与场景绑定 | 直接 Play 玩法场景：触发器 `TryFire` 记 Warn「未绑定演出服务」 |

示例：`Scenes/Verify/Performance.unity`（Main Camera 已排除第 9 层剔除遮罩、Player 挂 Kinematic Rigidbody2D 开
`useFullKinematicContacts`、Elder 挂 1003 对白、`Trigger_Intro` 挂 `PerformanceTrigger` OnEnter+once）；
示例演出 `perf_sample_greeting`（`Prefabs/Performance/perf_sample_greeting.prefab` + 同名时间轴，由模板工厂建）。

## 埋点（模块 `performance` + dialogue 侧两条）

| 事件 | 位置 | 尺子 |
| --- | --- | --- |
| `play_requested(id)`、`play_rejected(id, reason ∈ busy/empty_id)` | `PerformanceService` | 意图入口 / 失败 |
| `started(id, skippable)`、`hold_entered(id, index, at)`、`hold_confirmed(id, index)`、`skipped(id, at, source ∈ hold/code)`、`ended(id, outcome, duration)` | `PerformanceRules` | 状态迁移 |
| `load_failed(id, error)`、`stage_missing(id)`、`camera_stack_unavailable(id, reason)`、`play_failed(id, error)`、`view_close_failed(id, error)` | `PerformanceService` | 失败分支 |
| `trigger_fired(id, mode)`、`trigger_skipped(id, reason ∈ busy/played)`、`trigger_play_failed(id, error)` | `PerformanceTrigger` | 意图入口 / 失败 |
| `performance_unavailable(node, performance)`（Warn）、`performance_failed(node, performance, error)`（Error） | `DialogueController`，模块名 **`dialogue`** | 失败分支 |

不埋每帧事件（`Tick` / `TickSkip` 本身不埋）。契约见 [`docs/telemetry.md`](../../../../docs/telemetry.md)。

## 测试与验证

| 类型 | 位置 | 覆盖 |
| --- | --- | --- |
| EditMode | `Tests/EditMode/Performance/PerformanceRulesTests.cs` | 阶段迁移、停顿确认、长按计时与松手归零、不可跳过、重入、取消、结果分类、计时 |
| EditMode | `.../PerformancePolicyTests.cs` | 非法 `SkipHoldSeconds`（0/负数/NaN/无穷）抛异常、字段透传、`default` 值无效 |
| EditMode | `.../PerformanceSaveDataTests.cs` | 默认空、去重记录、大小写敏感、旧存档 `PlayedIds` 为 null 时的恢复 |
| EditMode | `.../PerformanceTriggerRulesTests.cs` | once/played/busy 判定与原因优先级（已播过优先于忙碌） |
| EditMode | `Tests/EditMode/Editor/Performance/PerformanceTemplateFactoryTests.cs`、`PerformanceValidatorTests.cs` | 模板工厂建齐资产（临时目录，`TearDown` 删干净）、校验器逐条问题码 |
| EditMode（Dialogue 侧） | `Tests/EditMode/Dialogue/DialogueCatalogTests.cs` | `performance` 字段翻译（去空白、空串 = 不插播） |
| Showcase | `Tests/Showcase/Performance/PerformanceShowcase.cs`（4 条） | 代码拉起演出（黑边/字幕/时停/停顿确认/结束恢复，PRD A2）、长按跳过（A3）、场景触发只播一次（A4）、对白节点前插播（A5） |
| 验证场景 | `Scenes/Verify/Performance.unity` | 见「接线要求」示例 |

跑 `/unity-test EditMode Performance`；视觉验收跑 `/verify-module Performance`（编辑器须打开）。

## 已知约束 / 未做

- **Live2D 演员未本地编译验证**：`Live2DPerformanceActor` 按官方源码核对 API 写成，但工程里没有导入 SDK，无法实际编译跑一遍；
  导入后若报错先按文件头注释的 API 清单核对。
- **相机叠加只实测了退路分支**：Overlay 叠加到 `Base` 相机的路径写在 prp 但两种 URP 渲染器（2D/Forward）的实测记录未在源码注释里体现，接手前建议先在验证场景跑一次 `/verify-module`。
- **演出中途不能存档 / 恢复**：`PerformanceSaveData` 只记「播完 / 跳过」这个终态，没有中途快照；异常退出（应用崩溃）会导致该演出下次重新播放。
- **对白插播固定在节点之前**：先演后说（PRD Q3 默认值），换成「之后」需要改 `DialogueController.PresentAsync` 的调用顺序。
- **没有分图**：演出复用 Dialogue 的 `"Dialogue"` 输入图，将来要给演出单独定义键位需要新开一张图并同步改 `PerformanceService.InputMap`。
- 手柄键位提示与 Dialogue 同款限制：`BuildSkipHint` 只取键盘第一条绑定，不跟随手柄。

## 禁止事项

- 不要在 `PerformanceRules` / `PerformanceStage` / `PerformanceView` 里碰 `Time.timeScale` 或输入图——只在 `PerformanceService`。
- 不要绕过 `PerformanceService.PlayAsync` 直接调 `PerformanceStage.Play()`：不会走重入保护、时停、输入图、相机叠加、存档与埋点。
- 不要在 `Game.Performance` 里出现任何对白 / 探索等其他模块的名词；对白只经 `IPerformanceService` 反向调用。
- 不要在 `Game.Runtime` 里直接引用 `Live2D.Cubism.*` 类型：Live2D 类型只能出现在可选程序集 `Game.Live2D` 里。
- 不要给 `PerformanceTrigger` 传本物体的销毁令牌：演出实例挂在 `DontDestroyOnLoad` 根上，触发器所在场景卸载不该打断已开始的演出。
- 不要在时间轴混合器（`SubtitleMixerBehaviour`/`ExpressionMixerBehaviour`）里每帧调用字幕面板 / 演员方法：只在权重最大片段变化时调一次。
