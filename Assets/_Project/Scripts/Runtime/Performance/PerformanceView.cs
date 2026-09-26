// 职责：演出面板——上下黑边、字幕（说话者 + 正文）、停顿提示符、跳过提示与长按进度环、进场黑场淡出；
//   实现字幕输出端供时间轴字幕轨道调用。只显示，不注入服务、不读输入、不持有播放进度，全部由 PerformanceService 调方法。
// 为什么新建（复用 → 扩展 → 新建）：DialogueView 是对白主面板（Popup 层、带选项与控件），演出要的是 Panel 层全屏、
//   Esc 关不掉、没有交互控件的覆盖层；塞进 DialogueView 会让 Performance 依赖 Dialogue（方向禁止）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using Game.Performance.Timeline;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Performance
{
    /// <summary>
    /// 演出面板。预制体 Addressables 地址须为 <c>PerformanceView</c>（UI 组）。
    /// <para>
    /// 接线提示：<c>letterboxTop</c> / <c>letterboxBottom</c> 分别锚在屏幕上 / 下边缘、横向拉伸，高度由本类改 sizeDelta.y；
    /// <c>fade</c> 是全屏黑色 Image（不挡射线）；<c>skipFill</c> 的 Image Type 须为 Filled。
    /// </para>
    /// </summary>
    public sealed class PerformanceView : UIView, IPerformanceSubtitleSink
    {
        [Tooltip("上黑边（锚在顶边、横向拉伸）。")]
        [SerializeField] private Image letterboxTop;

        [Tooltip("下黑边（锚在底边、横向拉伸）。")]
        [SerializeField] private Image letterboxBottom;

        [Tooltip("全屏黑场，进场时从不透明淡到透明。")]
        [SerializeField] private Image fade;

        [Tooltip("字幕根节点；没有字幕时隐藏。")]
        [SerializeField] private GameObject subtitleRoot;

        [Tooltip("说话者名字；旁白（空名字）时隐藏。")]
        [SerializeField] private TMP_Text speaker;

        [Tooltip("字幕正文。")]
        [SerializeField] private TMP_Text body;

        [Tooltip("停顿提示符（▼），时间轴停在 HoldMarker 时显示。")]
        [SerializeField] private TMP_Text holdPrompt;

        [Tooltip("跳过提示根节点；不可跳过的演出整个隐藏。")]
        [SerializeField] private GameObject skipRoot;

        [Tooltip("跳过提示文字（「按住 X 跳过」）。")]
        [SerializeField] private TMP_Text skipLabel;

        [Tooltip("长按进度环（Image Type = Filled）；进度为 0 时隐藏。")]
        [SerializeField] private Image skipFill;

        private MotionHandle topHandle;
        private MotionHandle bottomHandle;
        private MotionHandle fadeHandle;
        private float shownSkipProgress = -1f;

        public override UILayer Layer => UILayer.Panel;
        public override bool IsFullScreen => true;
        /// <summary>Esc / 手柄 B 不能关演出：关了服务的播放循环会失去面板；跳过走长按。</summary>
        public override bool CloseOnCancel => false;
        /// <summary>演出本身会整层藏 HUD；面板在 Panel 层，这里为 true 只是声明「沉浸模式下也要显示」。</summary>
        public override bool VisibleWhenHudHidden => true;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            if (!(arg is PerformanceViewArgs args))
                throw new ArgumentException("PerformanceView 需要 PerformanceViewArgs 参数", nameof(arg));

            CancelMotions();
            HideSubtitle();
            holdPrompt.text = args.HoldPrompt;
            holdPrompt.gameObject.SetActive(false);
            skipRoot.SetActive(args.Policy.Skippable);
            skipLabel.text = args.SkipHint;
            shownSkipProgress = -1f;
            SetSkipProgress(0f);

            float seconds = args.FadeSeconds > 0f ? args.FadeSeconds : 0f;
            float height = args.Policy.Letterbox && args.LetterboxHeight > 0f ? args.LetterboxHeight : 0f;
            letterboxTop.gameObject.SetActive(height > 0f);
            letterboxBottom.gameObject.SetActive(height > 0f);
            SetLetterbox(0f);
            if (height > 0f)
            {
                if (seconds > 0f)
                {
                    // UpdateIgnoreTimeScale：演出期间世界时停（timeScale = 0），黑边照样推入。
                    // AddTo(this)：面板被直接销毁时随之掐断；OnCloseAsync 里也手动 Cancel，双保险。
                    topHandle = LMotion.Create(0f, height, seconds)
                        .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                        .BindToSizeDeltaY(letterboxTop.rectTransform)
                        .AddTo(this);
                    bottomHandle = LMotion.Create(0f, height, seconds)
                        .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                        .BindToSizeDeltaY(letterboxBottom.rectTransform)
                        .AddTo(this);
                }
                else
                {
                    SetLetterbox(height);
                }
            }

            // 进场黑场：从全黑淡到透明，舞台相机的画面随之显出来。
            fade.raycastTarget = false;
            if (seconds > 0f)
            {
                SetFadeAlpha(1f);
                fadeHandle = LMotion.Create(1f, 0f, seconds)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToColorA(fade)
                    .AddTo(this);
            }
            else
            {
                SetFadeAlpha(0f);
            }
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            // 淡出过渡已播完才走到这里：收黑边、清字幕，让下次打开从干净状态开始。
            CancelMotions();
            if (letterboxTop != null && letterboxBottom != null) SetLetterbox(0f);
            if (subtitleRoot != null) HideSubtitle();
            if (holdPrompt != null) holdPrompt.gameObject.SetActive(false);
            return UniTask.CompletedTask;
        }

        /// <summary>显示一句字幕；说话者为空时隐藏名字栏（旁白）。</summary>
        public void ShowSubtitle(string speakerName, string text)
        {
            bool hasSpeaker = !string.IsNullOrEmpty(speakerName);
            speaker.gameObject.SetActive(hasSpeaker);
            speaker.text = hasSpeaker ? speakerName : string.Empty;
            body.text = text ?? string.Empty;
            subtitleRoot.SetActive(true);
        }

        /// <summary>收起字幕。</summary>
        public void HideSubtitle()
        {
            if (subtitleRoot == null) return;
            subtitleRoot.SetActive(false);
            if (speaker != null) speaker.text = string.Empty;
            if (body != null) body.text = string.Empty;
        }

        /// <summary>设置长按跳过进度（0–1）；0 时隐藏进度环。值没变不重写。</summary>
        public void SetSkipProgress(float progress)
        {
            float clamped = progress <= 0f ? 0f : (progress >= 1f ? 1f : progress);
            // 进度值来自同一处计算，精确比较足够。
            if (clamped == shownSkipProgress) return;
            shownSkipProgress = clamped;
            skipFill.fillAmount = clamped;
            skipFill.enabled = clamped > 0f;
        }

        /// <summary>显隐停顿提示符（▼）。</summary>
        public void SetHoldPromptVisible(bool visible)
        {
            if (holdPrompt.gameObject.activeSelf != visible) holdPrompt.gameObject.SetActive(visible);
        }

        private void SetLetterbox(float height)
        {
            RectTransform top = letterboxTop.rectTransform;
            RectTransform bottom = letterboxBottom.rectTransform;
            top.sizeDelta = new Vector2(top.sizeDelta.x, height);
            bottom.sizeDelta = new Vector2(bottom.sizeDelta.x, height);
        }

        private void SetFadeAlpha(float alpha)
        {
            Color color = fade.color;
            color.a = alpha;
            fade.color = color;
        }

        private void CancelMotions()
        {
            if (topHandle.IsActive()) topHandle.Cancel();
            if (bottomHandle.IsActive()) bottomHandle.Cancel();
            if (fadeHandle.IsActive()) fadeHandle.Cancel();
        }

        // 逐个点名缺失字段，预制体按名字接线时一眼看出漏了哪个。
        private void Validate()
        {
            var missing = new List<string>();
            if (letterboxTop == null) missing.Add(nameof(letterboxTop));
            if (letterboxBottom == null) missing.Add(nameof(letterboxBottom));
            if (fade == null) missing.Add(nameof(fade));
            if (subtitleRoot == null) missing.Add(nameof(subtitleRoot));
            if (speaker == null) missing.Add(nameof(speaker));
            if (body == null) missing.Add(nameof(body));
            if (holdPrompt == null) missing.Add(nameof(holdPrompt));
            if (skipRoot == null) missing.Add(nameof(skipRoot));
            if (skipLabel == null) missing.Add(nameof(skipLabel));
            if (skipFill == null) missing.Add(nameof(skipFill));
            if (missing.Count > 0)
                throw new InvalidOperationException("PerformanceView 引用未接线：" + string.Join("、", missing));
        }
    }
}
