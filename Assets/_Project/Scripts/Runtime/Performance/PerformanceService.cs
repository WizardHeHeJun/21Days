// 职责：演出管线的对外入口——按 id 实例化演出预制体、持世界时停令牌、切输入图、开演出面板、藏 HUD、
//   把舞台相机叠到主相机上，每帧把确认 / 长按跳过喂给规则，结束后按「进来前的状态」逐项恢复、归还实例、记存档、广播事件。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceRules 只管阶段语义、PerformanceStage 只管时间轴，二者都不该持有
//   时停 / 输入图 / UI / 相机 / 存档这类会话级资源；DialogueService 是对白专用且 Performance 不得依赖 Dialogue，只能新建。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Game.Performance
{
    /// <summary>
    /// 演出服务。其他模块注入 <see cref="IPerformanceService"/> 直接 <c>await PlayAsync(id)</c>；场景里走 <see cref="PerformanceTrigger"/>。
    /// <para>同一时刻只允许一段演出；演出期间表现层一律用 unscaled 时间。</para>
    /// <para>
    /// 策略 HideHud 为 true 时隐藏 HUD 层与弹窗层（对白框在弹窗层），结束后两层恢复可见；演出面板自身在 Panel 层不受影响。
    /// </para>
    /// <para>
    /// 取消语义：<c>ct</c> 取消时规则置 Cancelled、照常收尾并广播 Ended(Cancelled)，然后抛 <see cref="OperationCanceledException"/>
    /// （UniTask 约定，同 DialogueService）；Cancelled / Failed 不记「已播」。
    /// </para>
    /// </summary>
    public sealed class PerformanceService : IPerformanceService, IGameService
    {
        /// <summary>
        /// 演出期间启用的动作图。复用 GameInput 的 Dialogue 图（Advance = 确认继续、Skip = 长按跳过）而不新开 Performance 图：
        /// GameInput.inputactions 正被另一会话改动，且两图的键位语义一致；常量写在本模块而不引用 DialogueService.InputMap，
        /// 避免 Performance → Dialogue 的反向依赖（prp 2.5）。将来要分图只改这里。
        /// </summary>
        private const string InputMap = "Dialogue";

        private const string RootName = "PerformanceRoot";
        private const string KeyboardPathPrefix = "<Keyboard>";
        private const string FallbackSkipKey = "跳过";
        private const float FallbackCameraDepthOffset = 10f;

        private readonly PerformanceConfig config;
        private readonly PerformanceRules rules;
        private readonly IAssetService assets;
        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly IWorldPauseService worldPause;
        private readonly ISaveService save;
        private readonly IPublisher<PerformanceStartedEvent> startedPublisher;
        private readonly IPublisher<PerformanceEndedEvent> endedPublisher;
        private readonly ITelemetryScope telemetry;
        private Transform root;
        private bool running;
        // 代码请求（Confirm / Skip）只置标志，由播放循环在下一帧开头消费，与读动作走同一条处理函数，避免两处逻辑分叉。
        private bool pendingConfirm;
        private bool pendingSkip;

        public PerformanceService(PerformanceConfig config, PerformanceRules rules, IAssetService assets, IUIService ui,
            IInputService input, IWorldPauseService worldPause, ISaveService save,
            IPublisher<PerformanceStartedEvent> startedPublisher, IPublisher<PerformanceEndedEvent> endedPublisher,
            ITelemetryScope telemetry)
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.worldPause = worldPause ?? throw new ArgumentNullException(nameof(worldPause));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            this.startedPublisher = startedPublisher ?? throw new ArgumentNullException(nameof(startedPublisher));
            this.endedPublisher = endedPublisher ?? throw new ArgumentNullException(nameof(endedPublisher));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public bool IsRunning => running;

        public string CurrentId { get; private set; }

        public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        public bool HasPlayed(string id) => save.Get<PerformanceSaveData>().HasPlayed(id);

        public void Confirm()
        {
            if (running && rules.Phase == PerformancePhase.Holding) pendingConfirm = true;
        }

        public void Skip()
        {
            if (running && rules.IsActive) pendingSkip = true;
        }

        public async UniTask<PerformanceResult> PlayAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                telemetry.TrackWarn("play_rejected", TelemetryProps.Of(("id", string.Empty), ("reason", "empty_id")));
                throw new ArgumentException("演出 id 不能为空", nameof(id));
            }
            telemetry.Track("play_requested", ("id", id));
            if (running)
            {
                telemetry.TrackWarn("play_rejected", TelemetryProps.Of(("id", id), ("reason", "busy"), ("current", CurrentId)));
                throw new InvalidOperationException($"演出 {CurrentId} 正在进行，不能再拉起 {id}");
            }
            ct.ThrowIfCancellationRequested();

            // 重入保护从这里开始：加载是异步的，加载窗口内再调同样要被挡住。
            running = true;
            CurrentId = id;
            // 清掉上一段残留的请求；规则 Start 之后、开面板窗口内发来的 Skip 保留到播放循环第一帧再消费（加载期间规则未开始，Skip 无效）。
            pendingConfirm = false;
            pendingSkip = false;
            GameObject instance = null;
            PerformanceOutcome outcome = PerformanceOutcome.Failed;
            try
            {
                instance = await LoadAsync(id, ct);
                PerformanceStage stage = instance.GetComponent<PerformanceStage>();
                if (stage == null)
                {
                    telemetry.TrackError("stage_missing", $"演出预制体 {id} 根上没有 PerformanceStage", TelemetryProps.Of(("id", id)));
                    throw new InvalidOperationException($"演出预制体 {id} 根上没有 PerformanceStage");
                }
                PerformancePolicy policy = stage.BuildPolicy(config);
                rules.Start(id, policy);
                outcome = await RunAsync(id, stage, policy, ct);
                if (outcome == PerformanceOutcome.Completed || outcome == PerformanceOutcome.Skipped)
                {
                    save.Get<PerformanceSaveData>().MarkPlayed(id);
                }
                return new PerformanceResult(id, outcome, rules.ElapsedSeconds);
            }
            catch (OperationCanceledException)
            {
                outcome = PerformanceOutcome.Cancelled;
                throw;
            }
            finally
            {
                // 实例归还放最外层：加载成功后无论哪条路径（缺舞台、播放异常、取消）都要还。
                if (instance != null) assets.ReleaseInstance(instance);
                running = false;
                CurrentId = null;
                endedPublisher.Publish(new PerformanceEndedEvent(id, outcome));
            }
        }

        private async UniTask<GameObject> LoadAsync(string id, CancellationToken ct)
        {
            GameObject instance;
            try
            {
                instance = await assets.InstantiateAsync(id, EnsureRoot(), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.TrackError("load_failed", e, TelemetryProps.Of(("id", id)));
                throw;
            }
            if (instance == null)
            {
                telemetry.TrackError("load_failed", "实例化结果为空", TelemetryProps.Of(("id", id)));
                throw new InvalidOperationException($"演出 {id} 实例化失败（结果为空）");
            }
            return instance;
        }

        // rules 已 Start；本方法持有并在 finally 里按「进来前的状态」归还全部会话级资源。
        private async UniTask<PerformanceOutcome> RunAsync(string id, PerformanceStage stage, PerformancePolicy policy,
            CancellationToken ct)
        {
            // Actions 为 null（InputService 尚未初始化、或 EditMode）时没有输入图可管，记录 / 切换 / 恢复 / 读键一并跳过。
            GameInput actions = input.Actions; // lint-ok: 只取动作集引用以记录图状态与读演出键位，演出表现层不进确定性模拟
            bool hasInput = actions != null;
            bool gameplayWasEnabled = hasInput && actions.Gameplay.enabled;
            bool inputMapWasEnabled = hasInput && actions.Dialogue.enabled;

            IDisposable pause = null;
            PerformanceView view = null;
            bool hudHidden = false;
            var camera = new CameraStackState();
            bool holdPending = false;
            bool finishedPending = false;
            Action onHold = () => holdPending = true;
            Action onFinished = () => finishedPending = true;
            bool subscribed = false;
            try
            {
                if (hasInput)
                {
                    input.DisableMap(InputService.GameplayMap);
                    input.EnableMap(InputMap);
                }
                if (policy.PauseWorld) pause = worldPause.Acquire(this);

                var args = new PerformanceViewArgs(policy, BuildSkipHint(actions), config.LetterboxHeight,
                    config.FadeSeconds, config.HoldPromptText);
                view = await ui.OpenAsync<PerformanceView>(args, ct);
                if (policy.HideHud)
                {
                    // 隐藏 HUD 层与弹窗层：对白框（DialogueView）在弹窗层，Panel 层的演出面板盖不住它，对白里插播时要一起藏。
                    ui.SetLayerVisible(UILayer.Hud, false);
                    ui.SetLayerVisible(UILayer.Popup, false);
                    hudHidden = true;
                }
                AttachCamera(id, stage, ref camera);
                stage.SetSubtitleSink(view);
                stage.OnHold += onHold;
                stage.OnFinished += onFinished;
                subscribed = true;

                startedPublisher.Publish(new PerformanceStartedEvent(id));
                stage.Play();

                while (rules.Phase != PerformancePhase.Finished)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    float dt = Time.unscaledDeltaTime;
                    rules.Tick(dt);

                    if (finishedPending)
                    {
                        finishedPending = false;
                        rules.Complete();
                        break;
                    }
                    // 代码跳过（回放 / 试播 / 触屏）：与长按满走同一条收尾。
                    if (pendingSkip)
                    {
                        pendingSkip = false;
                        if (rules.Skip())
                        {
                            HandleSkipped(stage, view);
                            break;
                        }
                    }
                    // 先处理本帧的确认、再处理新到的停顿：停顿出现的同一帧按下的键不算确认，免得玩家看不到 ▼ 就被带过去。
                    bool confirmRequested = pendingConfirm
                        || (hasInput && actions.Dialogue.Advance.WasPressedThisFrame()); // lint-ok: 演出表现层读动作，不进确定性模拟
                    pendingConfirm = false;
                    if (confirmRequested && rules.Phase == PerformancePhase.Holding) HandleConfirm(stage, view);
                    if (holdPending)
                    {
                        holdPending = false;
                        if (rules.EnterHold()) view.SetHoldPromptVisible(true);
                    }
                    if (policy.Skippable)
                    {
                        bool held = hasInput && actions.Dialogue.Skip.IsPressed(); // lint-ok: 演出表现层读动作，不进确定性模拟
                        bool fired = rules.TickSkip(held, dt);
                        view.SetSkipProgress(rules.SkipProgress);
                        if (fired) HandleSkipped(stage, view);
                    }
                }
                return rules.Outcome ?? PerformanceOutcome.Completed;
            }
            catch (OperationCanceledException)
            {
                rules.Cancel();
                throw;
            }
            catch (Exception e)
            {
                rules.Fail();
                telemetry.TrackError("play_failed", e, TelemetryProps.Of(("id", id)));
                throw;
            }
            finally
            {
                // 正常结束、取消、异常都走这里；顺序：先摘回调再停时间轴（Stop 会触发 stopped），再拆相机、关面板、恢复 HUD / 输入 / 时停。
                if (subscribed)
                {
                    stage.OnHold -= onHold;
                    stage.OnFinished -= onFinished;
                }
                // stage 是 UnityEngine.Object，判空只用 == null。
                if (stage != null)
                {
                    stage.SetSubtitleSink(null);
                    stage.Stop();
                }
                DetachCamera(ref camera);
                if (view != null) await CloseViewAsync(view, id);
                pendingConfirm = false;
                pendingSkip = false;
                if (hudHidden)
                {
                    // 目前工程里没有别的调用方用 SetLayerVisible，直接恢复为可见，不记录进来前状态。
                    ui.SetLayerVisible(UILayer.Hud, true);
                    ui.SetLayerVisible(UILayer.Popup, true);
                }
                if (hasInput)
                {
                    // 只恢复进来前的状态：对白里插播时 Dialogue 图本来就开着、Gameplay 本来就关着，都原样保留。
                    if (!inputMapWasEnabled) input.DisableMap(InputMap);
                    if (gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
                }
                pause?.Dispose();
            }
        }

        // 玩家按确认与代码 Confirm 共用：规则回到 Playing 才继续时间轴、收起 ▼。
        private void HandleConfirm(PerformanceStage stage, PerformanceView view)
        {
            if (!rules.Confirm()) return;
            view.SetHoldPromptVisible(false);
            stage.Resume();
        }

        // 长按满与代码 Skip 共用：规则已置 Skipped + Finished，这里停时间轴、清进度环。
        private static void HandleSkipped(PerformanceStage stage, PerformanceView view)
        {
            view.SetSkipProgress(0f);
            stage.Stop();
        }

        private async UniTask CloseViewAsync(PerformanceView view, string id)
        {
            try
            {
                // 收尾不跟随调用方的 ct：取消后面板也必须关掉。
                await ui.CloseAsync(view, CancellationToken.None);
            }
            catch (Exception e)
            {
                telemetry.TrackError("view_close_failed", e, TelemetryProps.Of(("id", id)));
                Log.Error($"PerformanceService：关闭演出面板失败（{id}）：{e.Message}");
            }
        }

        private Transform EnsureRoot()
        {
            // 懒建：第一次播放时才建；DontDestroyOnLoad 让演出跨场景加载不被卸掉。Transform 判空只用 == null。
            if (root != null) return root;
            var go = new GameObject(RootName);
            Object.DontDestroyOnLoad(go);
            root = go.transform;
            return root;
        }

        private string BuildSkipHint(GameInput actions)
        {
            string key = actions == null ? string.Empty : KeyboardHint(actions.Dialogue.Skip);
            if (string.IsNullOrEmpty(key)) key = FallbackSkipKey;
            string format = config.SkipHintFormat;
            if (string.IsNullOrEmpty(format)) return key;
            try
            {
                return string.Format(format, key);
            }
            catch (FormatException)
            {
                Log.Warn($"PerformanceConfig.SkipHintFormat 格式串非法：「{format}」，已直接显示键位。");
                return key;
            }
        }

        // 取动作的第一条键盘绑定的显示文字（同 DialogueKeyboardInput.KeyboardHint；不引用它以免 Performance → Dialogue）。
        private static string KeyboardHint(InputAction action)
        {
            if (action == null) return string.Empty;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                string path = binding.effectivePath;
                if (path == null || !path.StartsWith(KeyboardPathPrefix, StringComparison.Ordinal)) continue;
                return action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            }
            return string.Empty;
        }

        /// <summary>相机叠加改动的记录，收尾时据此原样改回。私有可变记录，只在本类的 Attach / Detach 之间传递，不是序列化暴露面。</summary>
        private struct CameraStackState
        {
            internal Camera Stage;
            internal UniversalAdditionalCameraData StageData;
            internal CameraRenderType StageRenderType;
            internal UniversalAdditionalCameraData MainData;
            internal bool Stacked;
            internal bool Fallback;
            internal float StageDepth;
            internal CameraClearFlags StageClearFlags;
            internal Color StageBackground;
        }

        // 舞台相机（Overlay）叠进 Camera.main 的 URP 相机栈；主相机缺 URP 数据、本身不是 Base、或渲染器不支持叠加时
        // 退路：舞台相机改 Base、深度高于主相机、纯黑底，记 Warn + 埋 camera_stack_unavailable（prp 2.3）。
        private void AttachCamera(string id, PerformanceStage stage, ref CameraStackState state)
        {
            Camera stageCamera = stage.StageCamera;
            if (stageCamera == null)
            {
                Log.Warn($"PerformanceService：演出 {id} 的舞台没接 stageCamera，舞台内容将不可见。", stage);
                return;
            }
            UniversalAdditionalCameraData stageData = stageCamera.GetUniversalAdditionalCameraData();
            state.Stage = stageCamera;
            state.StageData = stageData;
            state.StageRenderType = stageData.renderType;
            state.StageDepth = stageCamera.depth;
            state.StageClearFlags = stageCamera.clearFlags;
            state.StageBackground = stageCamera.backgroundColor;

            Camera main = Camera.main;
            // 不用 GetUniversalAdditionalCameraData 取主相机：那个扩展在缺组件时会 AddComponent，「没有 URP 数据」就判不出来了。
            UniversalAdditionalCameraData mainData = null;
            bool canStack = main != null && main != stageCamera
                && main.TryGetComponent(out mainData)
                && mainData.renderType == CameraRenderType.Base;
            if (canStack)
            {
                stageData.renderType = CameraRenderType.Overlay;
                // cameraStack 在渲染器不支持叠加时返回 null（URP 自己会记一条 Warning）。
                var stack = mainData.cameraStack;
                if (stack != null)
                {
                    if (!stack.Contains(stageCamera)) stack.Add(stageCamera);
                    state.MainData = mainData;
                    state.Stacked = true;
                    return;
                }
            }

            stageData.renderType = CameraRenderType.Base;
            stageCamera.depth = (main == null ? 0f : main.depth) + FallbackCameraDepthOffset;
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Color.black;
            state.Fallback = true;
            string reason = main == null ? "no_main_camera"
                : main == stageCamera ? "stage_is_main"
                : mainData == null ? "no_urp_data"
                : mainData.renderType != CameraRenderType.Base ? "main_not_base"
                : "stack_unsupported";
            Log.Warn($"PerformanceService：演出 {id} 无法叠加到主相机（{reason}），舞台相机改为 Base 独立渲染。", stage);
            telemetry.TrackWarn("camera_stack_unavailable", TelemetryProps.Of(("id", id), ("reason", reason)));
        }

        private static void DetachCamera(ref CameraStackState state)
        {
            if (state.Stacked && state.MainData != null && state.Stage != null)
            {
                var stack = state.MainData.cameraStack;
                if (stack != null) stack.Remove(state.Stage);
            }
            // 舞台相机随实例归还一起销毁，这里仍改回原值：实例若被复用 / 归还失败，也不留下被改过的相机。
            if (state.Stage != null)
            {
                if (state.StageData != null) state.StageData.renderType = state.StageRenderType;
                if (state.Fallback)
                {
                    state.Stage.depth = state.StageDepth;
                    state.Stage.clearFlags = state.StageClearFlags;
                    state.Stage.backgroundColor = state.StageBackground;
                }
            }
            state = default;
        }
    }
}
