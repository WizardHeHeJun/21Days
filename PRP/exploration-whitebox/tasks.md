# tasks：探索层白盒示例（第二版，按 prp.md 第二版契约）

规则：每项完成打勾并记一句证据；执行中发现 prp.md 有误先改 prp.md 再继续。派单档位按 `.claude/rules/model-routing.md`。
**前置**：等对方会话（21days-13）发来「编译零错误 + EditMode 全绿」稳定点消息；开工前 `git status` + `ListAgents` 复核。

## 已完成（第一版波 1 留下、归本 PRP）

- [x] `QuestService.ResetProgress()` + `Tests/EditMode/Quest/QuestServiceTests.cs`（2 条）。
- [x] `Tables/Data/quest/2002.json` + 重生成 `quest_tbquest.bytes`；`QuestCatalogTests` 同步为 4 个任务。
- [x] `MonsterEncounterState` 去掉触屏控件创建（对方采纳）。

## 波 2（opus）——Loot 模块

- [x] T1 `Runtime/Loot/`：`LootConfig`、`LootInstaller`、`LootService`、`LootSaveData`、`LootRules`、`SupplyCrate`、`SupplyCrateMarker`、`LootSceneBinder`、`SupplyCrateFocus`（prp 3.1）；`Tests/EditMode/Loot/LootRulesTests.cs`。
  - 证据：11 个运行时文件（含 `CrateCollectedEvent` / `LootResetEvent`）+ 3 个测试文件（`LootRulesTests` 10 条、`LootServiceTests` 5 条、`SupplyCrateFocusTests` 3 条，共 18 条）；契约偏离已回写 prp 3.1（Items 键 int、通知标题/正文拆开、标记经 Binder 切沉浸、确认键轮询）。
- [x] T2 `Data/Loot/LootConfig.asset`；Boot 挂 `LootInstaller`（MCP）；`git rm` `EncounterTouchControls.cs(.meta)`。
  - 证据：MCP 建资产（guid 26ffc315…）；Boot `GameBootstrap` 组件顺序 `…DialogueInstaller, QuestInstaller, LootInstaller, ExplorationInstaller`（GameLifetimeScope 按 GetComponents 顺序调用），config 已赋值，Boot diff 纯追加 0 删除；grep 确认 `EncounterTouchControls` 无代码 / guid 引用后 `git rm`（已暂存删除）。
- [x] T3 lint 零违规、编译零错误、EditMode 全绿。
  - 证据：14 个新 `.cs` 逐个 `lint.py` 退出码 0；`refresh_unity` 后 `read_console(error)` 0 条；EditMode 367/367 通过（基线 349 + 新增 18）。

## 波 3（opus）——探索 HUD 扩展与场景接线

- [x] T4 `ExplorationHudView` 追加字段与访问器；预制体追加 `RunToggle / Stick / TouchButtons / ResetButton / InteractPrompt / CompassRoot / CompassMarkerTemplate`（MCP，prp 3.2）。
  - 证据：View 相对对方基线 +85 / -0；预制体经 `PrefabUtility.LoadPrefabContents` 追加（+2005 / -1，唯一「-」是 RunSlot 的 `m_Children: []` 变成列表）；9 个字段引用在 YAML 里全部非空；Play 中 `ControlsRoot/Stick/Knob`、`CompassRoot`、`RunSlot/RunToggle/Label` 均在。
- [x] T5 新建 `ExplorationControlsPresenter`、`ExplorationPointOfInterest`、`ExplorationCompassRules`（+ 测试）、`ExplorationCompassPresenter`、`ExplorationConfirmView`（+ 预制体 + 地址）；`ExplorationConfig` 追加字段；入口点注册进对方的 `ExplorationInstaller`（只追加）。
  - 证据：7 个新 `.cs`（含 `PoiKind`）+ `ExplorationCompassRulesTests`（8 条）；Installer +39 / -0；`ExplorationConfirmView.prefab` 由 DialogueSkipConfirmView 结构复制，Addressables UI 组地址 `ExplorationConfirmView`；Boot 的 `ExplorationInstaller.config` 已拖 `IsometricExplorationConfig.asset`（Boot diff 纯增）。
- [x] T6 SampleScene：`Crates` 三只箱子、五个兴趣点（prp 3.3）；保存场景。
  - 证据：`Crate_A (7.5,4.89,5.8)` / `Crate_B (18,4.89,12)` / `Crate_C (16,4.89,-8)`，y 由 Ground 层射线取得；删除他人遗留在场景里的散件 `ExplorationHudView` 根物体；SampleScene 对 HEAD `diff --histogram` 为 +1225 / -0，新增物体名只有 Crates / Crate_* / Closed / Opened / Marker。
- [x] T7 从 Boot 进场景冒烟：摇杆、走跑、开箱通知、万向标、重置各走一遍（Play 限时，超时先 stop 再汇报）；lint / 编译 / EditMode 复核。
  - 证据：10 个 `.cs` 的 lint 退出码均为 0；编译错误 0；EditMode 375/375。冒烟：Boot → 开始 → SampleScene，HUD 控件在，走跑标签「散步」，桌面平台三键隐藏、摇杆显示；万向标登记 8 个兴趣点，屏外显示 7 个；玩家挪到 Crate_A 旁 → 焦点 Crate_A、提示「打开物资箱」、`TryCollect` 返回 true、箱子变开、标记消失；重置 → 确认弹窗 → `progress_reset` → 场景重进，玩家回出生点，箱子合上；控制台 0 error。摇杆拖动与点按 OnScreenButton 走跑未实测，交波 4 Showcase。

## 波 4（opus）——回放验证

- [x] T8 `Tests/Showcase/Exploration/ExplorationShowcase.cs`（走 Boot 真实流程进 SampleScene）：覆盖 V1–V5，3～5 条。
  - 证据：4 条用例——`Controls_RunToggleAndStick`（V1/V2，散步 1.80 m vs 奔跑 3.00 m / 0.6 s，标签散步↔奔跑）、`Compass_ShowsOffscreenPoiAndHidesWhenImmersive`（V3，出生点 7 个万向标 / 8 个兴趣点，沉浸清零、退出恢复）、`Crate_CollectGivesRewardAndQuestProgress`（V4）、`Reset_RestoresQuestsAndCrates`（V5）。摇杆走测试手柄 `InputSystem.AddDevice<Gamepad>` + `QueueStateEvent` 驱动 `Gameplay/Move`；走跑走 `LiveInputSource.HeldButtons` 脉冲。收尾先 `GoToAsync<TitleState>` 让遭遇状态卸场景、释放 Addressables 场景句柄，再销毁根作用域（直接销毁会让下一条用例点「开始」后场景加载不出来）；点「开始」前等 `IGameFlow.Current is TitleState`（`ui.Get<TitleView>()` 非空时 TitleState 还没订阅按钮，实测会丢点击）。lint 退出码 0。
- [x] T9 `/verify-module Exploration` PASS；复跑 IsometricExploration / Quest / Dialogue 回放仍 PASS（V6）。
  - 证据：`Logs/verify/exploration/20260926-032704/report.md` PASS（4/4，31 个检查点全绿，0 异常，12 张截图）；同批复跑 `Logs/verify/{isometricexploration,quest,dialogue}/20260926-032748` 均 PASS（2 + 3 + 6 条）；EditMode 375/375；编译错误 0；`ProjectSettings/` 无副作用。遗留：箱子 itemId 1/2/4 不在 tbitem（表里是 1001 起），通知正文显示「#1 ×1」，待定。

## 波 5（sonnet）——文档与审查

- [x] T10 `/generate-doc loot` 三件套、`modules.json`、catalog 补行；isometricexploration / quest / player 文档同步。
  - 证据：新建 `ai-docs/docs/modules/loot/`（guide 106 行 + external-api 72 行 + extension-guide 57 行，`path:line` 逐条核对）；`modules.json` 追加 `loot: ready`；`catalog.md` 模块表追加一行。`isometricexploration-*` 三件套同步「当前组成」「场景结构」「探索控件与万向标」新节、依赖方向图、配置资产表、验证入口、扩展指南（新兴趣点种类 / 新 HUD 控件）。`quest-module-guide.md` / `quest-external-api.md` 补 `ResetProgress()` 语义。`monster-module-guide.md` 更新触屏控件已迁至探索 HUD（原行引用的 `EncounterTouchControls.cs` 已删）。`player-module-guide.md` 核对 `RunSpeed`/`IsRunning` 已完整记录，未改动。`gc_scan.py` 报的 1 处失效引用（字体资产 Dynamic Data 6236 KB）与本次改动无关，未修。
- [x] T11 `invariants.py` / `gc_scan.py` 零报错；code-reviewer 无 BLOCK（V7）。
  - 证据：`invariants.py` / `gc_scan.py` 唯一报错是他人遗留的字体 SDF 资产 Dynamic Data（6238 KB），与本 PRP 无关；code-reviewer 结论 PASS、0 BLOCK、1 WARN（Boot 安装器顺序按 fileID 误判，按 m_Component 列表核对实际为 Quest → Loot → Exploration，与文档一致，不改）；EditMode 427/427（2026-09-26 主窗口复跑）。

## 波 8（sonnet）——HUD 布局修正：重置按钮挪位 + 万向标同边错开（2026-09-26）

- [x] T16 `ExplorationHudView.prefab` 的 `ControlsRoot/ResetButton` 从左上角（任务栏下方，压住 `QuestHudView`）
  挪到左下角，锚点/pivot 对齐 `ImmersiveButton`（均为 (0,0)），压在其上方不重叠。
  - 证据：MCP `open_prefab_stage` → `manage_components.set_property` 改 `RectTransform` → `save_prefab_stage`；
    YAML 复核 `anchorMin/Max {0,0}`、`pivot {0,0}`、`anchoredPosition {48,136}`、`sizeDelta {220,56}`；未在任何场景留下预制体实例。
- [x] T17 `ExplorationCompassRules` 新增纯函数 `SpreadAlongEdges`（`CompassPlacement` 结构 + 私有
  `CompassEdge` 分类）：同边标记按沿边坐标排序、最小间距正向推挤、越界整体回退+反向夹紧钳制在
  `[-h, h]`；栈上 `Span`/`stackalloc` 分下标与坐标缓冲区，不产生堆分配。`ExplorationCompassPresenter`
  新增 `CompassMinSpacing = 56f` 常量（注释「待并入 IsometricExplorationConfig」）与两个预分配 List
  `framePlacements` / `framePlacementLabels`，`Tick` 改为两遍：先收集全部摆位再 `SpreadAlongEdges`
  错开，最后落到标记池。`ExplorationCompassRulesTests` 补 3 条：同边重叠推开、不同边互不影响、
  推到边角整体前移不越界。
  - 证据：3 个 `.cs`（`ExplorationCompassRules`、`ExplorationCompassPresenter`、
    `ExplorationCompassRulesTests`）lint 退出码均 0（`GameMath.Max/Abs` 替代 `Mathf`，通过重放确定性检查）；
    `refresh_unity(compile=request)` 后 `read_console(error)` 无 CS 编译错误（仅 Editor 内部
    Inspector/TestRunner 噪音，与本次改动无关）；EditMode 476/476；PlayMode `ExplorationShowcase` 四条
    全部 PASS（首次因编辑器域重载未起步判失败，重跑成功）。
- [x] T18 文档：guide 的「探索控件与万向标」补重置按钮新位置与万向标同边错开一句；「验证入口」
  测试条数 8→11。
  - 证据：`isometricexploration-module-guide.md` 两处编辑，`path:line` 已核对。

## 只能人做的

- 视觉验收（回放截图 + 实机走一遍）；`/review-change` 授权提交（按文件挑，不整份暂存）。

## 波 6（opus）——收口小修（2026-09-26）

- [x] T12 PC 优先：触屏控件（摇杆 / 三键 / 走跑按钮）默认只在触屏显示，预制体与代码保留。
  - 证据：`IsometricExplorationConfig.showStickOnDesktop` 默认 `false`（Tooltip 改为开发开关）；`ExplorationHudView` 追加 `SetRunToggleVisible(bool)`（切 `RunToggle` 物体，可空，不动沉浸 alpha）；`ExplorationControlsPresenter` 统一 `touchControls = IsTouchPrimary || ShowStickOnDesktop` 驱动三者；资产经 MCP 重存 `showStickOnDesktop: 0`。回放用例改名 `Controls_RunToggleViaKeyAndTouchControlsHiddenOnDesktop`：桌面三类控件全部隐藏，走跑改断言 `PlayerModel.IsRunning`（按钮已隐藏，不再断言标签），散步 1.85 m vs 奔跑 3.08 m / 0.6 s。
- [x] T13 箱子 itemId 对齐道具表：`Crate_A` 1001（铁剑）、`Crate_B` 1002（精钢长剑）、`Crate_C` 1004（治疗药水），count 不变。
  - 证据：`execute_code` 读 `tbitem.bytes` 得 1001 铁剑 / 1002 精钢长剑 / 1003 龙鳞护甲 / 1004 治疗药水；场景经 `SerializedObject` 改值后保存，`git diff ... | grep itemId` 只有这三行；回放开箱通知正文断言「铁剑 ×1」通过。
- [x] T14 删 `IsometricExplorationConfig.moveSpeed` / `sortingScale` 与属性（21days-bc 请求）；`IsometricPlayerController3D` 改本地常量 `PrototypeMoveSpeed = 3f`。
  - 证据：资产重存后 `moveSpeed: 3` / `sortingScale: 100` 两行消失；lint 5 个 `.cs` 退出码 0；编译错误 0；EditMode 375/375；`Logs/verify/exploration/20260926-033852` PASS 4/4；`Logs/verify/isometricexploration/20260926-034342` PASS 2/2。

## 波 7（sonnet）——物资箱交互键对齐 Gameplay/Interact（2026-09-26）

- [x] T15 `SupplyCrateFocus` 开箱触发键从 `Gameplay/Confirm` 改为 `Gameplay/Interact`（同 `DialogueInteractionFocus` 读法）；`LootConfig.PromptText` 与 `LootConfig.asset` 默认值改「E 打开物资箱」；`ExplorationHudView.prefab` 的 `InteractPrompt` 位置/尺寸对齐对白提示 `(0, 72)` 320×48；`ExplorationConfirmView.prefab` 根组件 `defaultSelected` 拖 `CancelButton`；`ExplorationShowcase.cs` 的 `PromptText` 常量与检查点描述同步；`loot-module-guide.md` / `loot-external-api.md` / `isometricexploration-module-guide.md` 文案同步。
  - 证据：3 个 `.cs`（`SupplyCrateFocus`、`LootConfig`、`ExplorationShowcase`）lint 退出码均 0；`LootConfig.asset` 经 `manage_scriptable_object` 改 `promptText` 为「E 打开物资箱」；`ExplorationHudView.prefab` 经 `manage_prefabs.modify_contents` 改 `InteractPrompt` 的 `m_AnchoredPosition {0,72}` / `m_SizeDelta {320,48}`；`ExplorationConfirmView.prefab` 经 `open_prefab_stage` + `manage_components.set_property` 把根组件 `defaultSelected` 指向 `CancelButton`（`DefaultSelected.instanceID` 核对非空）后 `save_prefab_stage`；`refresh_unity(compile=request)` 后 `read_console(error)` 0 条；EditMode 427/427；`run_tests(PlayMode)` 跑 `ExplorationShowcase` 四条全部 PASS（`Logs/verify/exploration/20260926-040225/report.md`，31 个检查点全绿，0 异常），首次 `run_tests` 因编辑器刚完成域重载 120s 内未起步自动判失败，重跑一次即成功。

## 波 9（opus）——多层灰盒地图 + 遮挡碰撞 + 遮挡半透明 + 调试文字挪位（2026-09-26）

设计与取舍见 `prp.md` 3.5。

- [x] T19 多层地图：`SampleScene` 的 `Environment_Graybox/MultiLevel` 下新建 `Ground_East`、`Deck_Upper`、`Ramp_South`、`Stairs_West/Stairs_West_Step_1..9`、`Bridge_West`、`Railings/Rail_Bridge_S|N`、`Rail_Deck_N|E|S1|S2|W1|W2`（原生 Cube，Layer `Ground`，复用 Graybox 材质）；`QuestLocation_Lookout` → (25, 7.89, 12)，`Crate_B` → (28, 7.89, 15)。
  - 证据：MCP `execute_code` 建物体并在同一调用里 `SaveScene`；与改前备份 `diff`：删 2 行（只有 Lookout / Crate_B 两个 `m_LocalPosition`）、增 2471 行，新增 `m_Name` 全是上述 24 个物体；射线核对坡面 z −0.9 / 3 / 6.9 → y 4.928 / 6.390 / 7.853，z 7.5 落在甲板 7.890；沿坡道每 0.1 m 贴地后 `CheckCapsule`(0.35..1.5) 零重叠；潜行走廊 z 3.4、x −4..18 胶囊扫掠零命中；桥下 x 15..19 零命中。
- [x] T20 遮挡碰撞（表现层白盒）：新建 `Runtime/Monster/EncounterCollision.cs`（先 X 后 Z `CapsuleCast`）；`EncounterSceneView` 追加 `obstacleMask`（默认 0）/ `obstacleBottomOffset` / `obstacleTopOffset` / `obstacleRadius` / `obstacleTeleportDistance`（偏离：新增瞬移阈值 1.5 m）与 `OnPlayerBlocked`，贴地抽成私有 `ResolveGroundY`；`EncounterStep.CorrectPlayerPosition`；`MonsterEncounterState` / `StandaloneEncounterController` 订阅与退订；SampleScene `obstacleMask` = `Ground`（256），`grep` 确认 `obstacleMask` 只出现在 SampleScene，Disguise / Taming 走默认 0。
  - 证据：EditMode 478/478（476 + `CorrectPlayerPosition_WhenActive_OverridesPlayerPosition` / `_WhenInactive_IsIgnored`）；回放 `Collision_FenceBlocksPlayer`：z 最小 −0.64（围栏横杆 −0.96 + 半径 0.3 + skin），逻辑位置同步回写。
- [x] T21 遮挡半透明：`Art/Materials/Graybox/M_Graybox_Faded.mat`（M_Wall 复制，URP Lit Transparent，Alpha 0.35）；`SceneOccluder` 挂 `Bridge_West`、`Deck_Upper` 与 8 段栏杆（偏离：任务书写栏杆不挂，首轮截图里深色南栏杆仍把人压住）；`OccluderFadePresenter` 由 `ExplorationInstaller` 追加注册。
  - 证据：回放 `Occluder_FadesBridgeWhenPlayerBeneath` 站位改为 (17.25, 12.3)（偏离：桥正中下方相机看得见人，编辑器里 `RaycastAll` 核对只有 12.3 这一侧命中 `Bridge_West` + `Rail_Bridge_S`），淡出 / 恢复两检查点通过。
- [x] T22 调试文字：`EncounterSceneView.OnGUI` 两行 + 警戒条挪到右上「返回标题」下方（y 64 / 92 / 124，右对齐），按钮不动。
  - 证据：回放截图右上可见「怪物生命 3 状态 …」与警戒条，左上只剩任务栏。
- [x] T23 验证与文档：6 个 `.cs` + 2 个测试文件 lint 退出码均 0；`refresh_unity` 后 `read_console(error)` 无 CS 错误（只有 TestRunner 内部 `PlaymodeLauncher` 空引用噪音）；`Logs/verify/exploration/20260926-050244/report.md` PASS 7/7（49 个检查点全绿，0 异常）；`Logs/verify/isometricexploration/20260926-050349/report.md` PASS 2/2（潜行走廊未被挡）。`isometricexploration-module-guide.md`（场景结构 / 遮挡半透明 / 已知限制 / 验证入口）、`monster-module-guide.md`（白盒遮挡碰撞小节）、`pitfalls.md` 两条（MCP 改场景要当场保存；等距相机下桥挡的是北侧的人）。
- 待人看：`Logs/verify/exploration/20260926-050244/12-站上甲板·多层.png`、`11-坡道脚下.png`、`13-桥挡视线·半透明.png`、`01-围栏挡住玩家.png`。

## 波 10（opus）——遮挡淡出通用化（粗射线 + 过渡 + 追挂遮挡物，2026-09-26）

设计与偏离见 `prp.md` 3.5 末尾「波 10 追加」。

- [x] T24 粗射线探测：`OccluderFadePresenter` 改 `SphereCastNonAlloc`（半径 `OccluderProbeRadius` 默认 1.0，距离扣半径，`MaxHits` 16，纯规则 `TryBuildProbe`）；`IsometricExplorationConfig` 追加字段，资产经 `manage_scriptable_object` 写入 `occluderProbeRadius: 1`；`ExplorationInstaller` 多传配置（偏离：任务书未列此文件）。
  - 证据：新 EditMode `OccluderFadePresenterTests` 5 条；EditMode 498/498。
- [x] T25 淡入淡出过渡：`SceneOccluder` 追加 `fadeSeconds`（0.15）+ `Advance(dt)` / `IsTransitioning`，`MaterialPropertyBlock` 插 `_BaseColor`；呈现器只推进 `animating` 列表，终态清属性块。
  - 证据：回放「塔体恢复原材质（过渡结束后换回）」通过。
- [x] T26 追挂 `SceneOccluder`（`fadedMaterial` = `M_Graybox_Faded.mat`）：`Tower`、`Ramp_South`、`Stairs_West_Step_1..9`、`Wall_Left`、`Wall_Back`；SampleScene 经 `execute_code` 挂组件后同一调用内 `SaveScene`，场景内 `SceneOccluder` 共 23 个。
  - 证据：`git diff -U0 --histogram -- Assets/Scenes/SampleScene.unity | grep "m_Name:"` 只有 13 行空的 `+  m_Name: `（新 MonoBehaviour 组件块自带的空名字段，不是新物体）。
- [x] T27 回放：新增 `Occluder_FadesTowerWhenPlayerOnStairs`（(13.8, 15.5) 塔淡出 → (2, 3.4) 恢复），桥用例保留。
  - 证据：`Logs/verify/exploration/20260926-054051/report.md` 8 条里 7 PASS；唯一失败 `Crate_CollectGivesRewardAndQuestProgress`「正文为铁剑 ×1」——另一会话把 SampleScene 里 `Crate_A.itemId` 从 1001 改成 1005，不是本波改动；`Logs/verify/isometricexploration/20260926-054212` PASS 2/2。截图 `15-塔挡视线·半透明.png`、`16-离开·塔体恢复.png`、`13-桥挡视线·半透明.png`。
- [x] T28 文档：`isometricexploration-module-guide.md`（场景结构、遮挡半透明小节、已知限制下「关卡设计约束（遮挡）」）。
- [x] T29 开箱通知断言改数据驱动：`ExplorationShowcase` 删 `CrateABody` 常量，新增 `ExpectedRewardBody(crate)`——名字查 `IConfigService.Tables.TbItem.GetOrDefault(itemId).Name`（本程序集不引用 Luban.Runtime，取表行与 `Name` 字段走反射；查不到用「#id」同 LootService），正文 = `LootService.ComposeBody(LootConfig.RewardBodyFormat, 名字, count)`；SampleScene 的 1005 不动。
  - 证据：lint 退出码 0；编译绿；`Logs/verify/exploration/20260926-055102/report.md` PASS（正文「破旧信笺 ×1」，Crate_A = tbitem 1005）。

## 波 11（sonnet）——万向标默认关闭（2026-09-26，用户决定）

用户决定：全部兴趣点都标识会显得屏幕乱，任务追踪的指引已由 Quest 模块负责，万向标默认关闭。设计与取舍见 `prp.md` 3.2 `ExplorationCompassPresenter` 行。

- [x] T30 `IsometricExplorationConfig` 新增 `[SerializeField] bool showCompass = false` + 只读属性 `ShowCompass`；`IsometricExplorationConfig.asset` 经 `manage_scriptable_object` 显式写 `showCompass: false`。`ExplorationCompassPresenter` 新增读写属性 `Enabled`（构造时取 `config.ShowCompass`），`Tick` 在 `!Enabled` 时跳过扫描 / 摆位（只在关闭当帧收起已显示标记）；`ExplorationInstaller` 给万向标那条 `RegisterEntryPoint` 追加 `.AsSelf()`，回放按具体类型解析。`ExplorationShowcase.Compass_ShowsOffscreenPoiAndHidesWhenImmersive` 开头加检查点「默认关闭：出生点没有激活万向标克隆」，随后 `Step` 显式 `ResolveService<ExplorationCompassPresenter>().Enabled = true` 打开再走原有断言；排查确认其余用例（`Crate_*`、`Reset_*`、`Collision_*`、`MultiLevel_*`、`Occluder_*`）都不依赖万向标激活状态，未改动。
  - 证据：4 个 `.cs`（`IsometricExplorationConfig`、`ExplorationCompassPresenter`、`ExplorationInstaller`、`ExplorationShowcase`）lint 退出码均 0；`refresh_unity(compile=request)` 后 `read_console(error)` 0 条（此后另一并发会话改 `Core/Boot/GameLifetimeScope.cs` 引入 `CS0246 TitleLoadClickedEvent` 未定义，与本波无关，不在本波允许改动的文件范围内，未处理）；EditMode 645/645；PlayMode `ExplorationShowcase` 全部 8 条（`Controls_RunToggleViaKeyAndTouchControlsHiddenOnDesktop`、`Compass_ShowsOffscreenPoiAndHidesWhenImmersive`、`Crate_CollectGivesRewardAndQuestProgress`、`Reset_RestoresQuestsAndCrates`、`Collision_FenceBlocksPlayer`、`MultiLevel_RampLeadsToDeck`、`Occluder_FadesBridgeWhenPlayerBeneath`、`Occluder_FadesTowerWhenPlayerOnStairs`）PASS（job `0628f58b32254a54a2cd2eb831ef6d3b`，8/8，54.3s）。`isometricexploration-module-guide.md`、`prp.md` 3.2 已同步默认关闭说明。
