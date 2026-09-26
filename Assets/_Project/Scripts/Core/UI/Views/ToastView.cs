// 职责：通用通知条（Toast）——屏幕上方居中逐条显示短文本，每条停留固定秒数后换下一条，队列空了自动收起。
// 为什么新建（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：现有 UIView 只有 TitleView（Panel 层全屏）与各玩法模块自己的 HUD（任务栏、对话按钮），
//      没有一个是「跟玩法无关、谁都能往里塞一句话」的通知条。
//   2. 扩展不行：塞进 QuestHudView / DialogueInteractHudView 会让「通知」绑死在某个玩法模块上，
//      而通知是框架级通用能力（开箱、任务、系统提示都要用），所以放 Core/UI/Views，且不改 IUIService：
//      调用方 ui.OpenAsync<ToastView>() 拿到实例后 Enqueue 即可。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Game.Core.UI.Views
{
    /// <summary>
    /// 通知条。预制体 <c>Assets/_Project/Prefabs/UI/ToastView.prefab</c>，Addressables 地址 <c>ToastView</c>。
    /// Hud 层、不全屏、不进栈；常驻打开，空闲时只隐藏 <see cref="root"/>。
    /// <para>
    /// 显示循环用 UniTask 驱动，按不受时间缩放的真实时间计时（世界暂停时通知照样走完）。
    /// 面板关闭或销毁时取消循环；取消在循环内部吞掉，不留未观察的 UniTask 异常。
    /// </para>
    /// </summary>
    public sealed class ToastView : UIView
    {
        /// <summary>单条停留秒数的下限，防止配成 0 时一帧内把整个队列刷完。</summary>
        private const float MinSeconds = 0.1f;

        [Tooltip("通知条本体（深色半透明底 + 文字）；队列空时隐藏。")]
        [SerializeField] private GameObject root;

        [Tooltip("通知文字。")]
        [SerializeField] private TMP_Text label;

        [Tooltip("每条通知停留的秒数（真实时间，不受世界暂停影响）。")]
        [SerializeField] private float seconds = 2f;

        private readonly Queue<string> pending = new Queue<string>();
        private CancellationTokenSource lifetime;
        private bool pumping;

        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;

        /// <summary>当前排队（未显示）的条数，不含正在显示的那条。</summary>
        public int PendingCount => pending.Count;

        /// <summary>是否正在显示某一条。</summary>
        public bool IsShowing => pumping;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            if (!pumping && root != null)
            {
                root.SetActive(false);
            }

            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            StopPump();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 追加一条通知。正在显示时排到队尾，空闲时立刻显示。空串忽略。
        /// </summary>
        public void Enqueue(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            pending.Enqueue(text);
            if (pumping)
            {
                return;
            }

            if (lifetime == null)
            {
                lifetime = new CancellationTokenSource();
            }

            PumpAsync(lifetime.Token).Forget();
        }

        private async UniTaskVoid PumpAsync(CancellationToken ct)
        {
            pumping = true;
            float hold = seconds < MinSeconds ? MinSeconds : seconds;
            try
            {
                while (pending.Count > 0)
                {
                    label.text = pending.Dequeue();
                    root.SetActive(true);
                    await UniTask.Delay(TimeSpan.FromSeconds(hold), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 面板关闭 / 销毁：正常收尾，不外抛（外抛就成了未观察异常，会随机砸中别的测试用例）。
            }
            finally
            {
                pumping = false;
                if (root != null)
                {
                    root.SetActive(false);
                }
            }
        }

        private void StopPump()
        {
            pending.Clear();
            if (lifetime != null)
            {
                lifetime.Cancel();
                lifetime.Dispose();
                lifetime = null;
            }
        }

        private void OnDestroy()
        {
            // UIService 退出路径（ReleaseAllViews）不走 OnCloseAsync，这里兜底取消，免得循环摸到已销毁的对象。
            StopPump();
        }

        private void Validate()
        {
            if (root == null || label == null)
            {
                throw new InvalidOperationException("ToastView 预制体缺少 root / label 引用，检查 Prefabs/UI/ToastView.prefab 的接线");
            }
        }
    }
}
