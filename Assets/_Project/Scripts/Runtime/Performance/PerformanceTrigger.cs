// 职责：场景里的演出挂载点——玩家进入触发区（或场景开始时）按 id 拉起一段演出；支持「只播一次」。
// 为什么新建（复用 → 扩展 → 新建）：DialogueInteractable 是按键交互的对白入口、属于 Dialogue；演出触发是进入即播、
//   不需要焦点与按键，语义不同且 Performance 不得依赖 Dialogue。场景物体不在容器里，服务由 PerformanceSceneBinder 注入。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Telemetry;
using UnityEngine;

namespace Game.Performance
{
    /// <summary>
    /// 演出触发器。OnEnter 模式需要同物体上有 isTrigger 的 Collider / Collider2D；只认根上（含父级）带
    /// <see cref="PerformanceTriggerActor"/> 的对象。OnSceneStart 模式由 <see cref="PerformanceSceneBinder"/> 在启动完成后调 <see cref="TryFire"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceTrigger : MonoBehaviour
    {
        [Tooltip("要播放的演出 id（Addressables Performance 组地址）。")]
        [PerformanceId]
        [SerializeField] private string performanceId = string.Empty;

        [Tooltip("触发时机：进入触发区 / 场景开始。")]
        [SerializeField] private PerformanceTriggerMode mode = PerformanceTriggerMode.OnEnter;

        [Tooltip("只播一次：存档里已播过（完整播完或被跳过）就不再触发。")]
        [SerializeField] private bool once = true;

        private IPerformanceService service;
        private ITelemetryScope telemetry = NullTelemetryScope.Instance;
        private bool warnedUnbound;
        private bool warnedEmptyId;

        public string PerformanceId => performanceId;
        public PerformanceTriggerMode Mode => mode;
        public bool Once => once;

        /// <summary>注入服务与埋点（由 <see cref="PerformanceSceneBinder"/> 调用）。</summary>
        public void Bind(IPerformanceService performanceService, ITelemetryScope telemetryScope)
        {
            service = performanceService;
            telemetry = telemetryScope ?? NullTelemetryScope.Instance;
        }

        /// <summary>按判定规则尝试触发；未绑定、id 为空、已播过、服务忙时不触发。</summary>
        public void TryFire()
        {
            if (service == null)
            {
                if (!warnedUnbound)
                {
                    warnedUnbound = true;
                    Log.Warn($"PerformanceTrigger {name}：未绑定演出服务（Boot 场景没挂 PerformanceInstaller？），不触发。", this);
                }
                return;
            }
            if (string.IsNullOrEmpty(performanceId))
            {
                if (!warnedEmptyId)
                {
                    warnedEmptyId = true;
                    Log.Warn($"PerformanceTrigger {name}：没填 performanceId，不触发。", this);
                }
                return;
            }
            bool hasPlayed = once && service.HasPlayed(performanceId);
            if (!PerformanceTriggerRules.ShouldFire(once, hasPlayed, service.IsRunning, out string reason))
            {
                telemetry.Track("trigger_skipped", ("id", performanceId), ("reason", reason));
                return;
            }
            telemetry.Track("trigger_fired", ("id", performanceId), ("mode", ModeName(mode)));
            PlayAsync(performanceId).Forget();
        }

        private void OnTriggerEnter(Collider other) // lint-ok: 触发区只用来拉起表现层演出，不进逻辑判定、不影响回放
        {
            if (mode != PerformanceTriggerMode.OnEnter || other == null) return;
            if (other.GetComponentInParent<PerformanceTriggerActor>() == null) return;
            TryFire();
        }

        private void OnTriggerEnter2D(Collider2D other) // lint-ok: 触发区只用来拉起表现层演出，不进逻辑判定、不影响回放
        {
            if (mode != PerformanceTriggerMode.OnEnter || other == null) return;
            if (other.GetComponentInParent<PerformanceTriggerActor>() == null) return;
            TryFire();
        }

        // Forget 出去的异步不能让异常悄悄消失：取消是正常结束，其他异常记 Error + 埋点。
        // 不传本物体的销毁令牌：演出挂在 DontDestroyOnLoad 的根上，触发器所在场景卸载不该打断演出。
        private async UniTask PlayAsync(string id)
        {
            try
            {
                await service.PlayAsync(id);
            }
            catch (OperationCanceledException)
            {
                // 演出被取消属于正常收尾，服务已埋 ended(cancelled)。
            }
            catch (Exception e)
            {
                telemetry.TrackError("trigger_play_failed", e, TelemetryProps.Of(("id", id)));
                Log.Error($"PerformanceTrigger：演出 {id} 播放失败：{e.Message}");
            }
        }

        private static string ModeName(PerformanceTriggerMode value)
        {
            return value == PerformanceTriggerMode.OnSceneStart ? "on_scene_start" : "on_enter";
        }
    }
}
