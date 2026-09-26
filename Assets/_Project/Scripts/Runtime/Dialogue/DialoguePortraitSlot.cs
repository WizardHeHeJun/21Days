// 职责：对白面板一个立绘槽的表现状态机——入场 / 退场滑动 + 淡入淡出、同槽换表情交叉淡化（运行时残影 Image）、说话者高亮与非说话者压暗。
//   只改自己那张 Image 与它的残影，不碰剧情状态；由 DialogueView 持有（每槽一个），不是组件。
// 新建原因：复用——没有现成的立绘动效组件；扩展——每槽要独立持有 3 条补间句柄、起讫值与残影，
//   平铺进 DialogueView 会变成一组按槽下标的平行数组，且 View 的职责（显示 + 抛事件）被动效细节淹没，所以抽成 View 私用的普通类。
using System;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Dialogue
{
    internal sealed class DialoguePortraitSlot
    {
        private readonly Image image;
        private readonly RectTransform rect;
        // -1 = 左槽（从屏幕左侧进出），+1 = 右槽。
        private readonly float side;
        // 预制体里的原位：只在构造时取一次，动画中途开关面板也不会越跑越偏。
        private readonly Vector2 rest;
        private readonly Action moveCompleted;
        private readonly Action fadeCompleted;
        private DialogueMotionSettings motion = DialogueMotionSettings.Default;
        // 残影：交叉淡化时显示旧表情，懒建、每槽一份，挂在主图同级、层级在主图之下。
        private Image ghost;
        private MotionHandle move;
        private MotionHandle fade;
        private MotionHandle dim;
        // 逻辑目标（动画终态）：null = 该槽为空。
        private Sprite sprite;
        private bool speaking;
        private Vector2 moveFrom;
        private Vector2 moveTo;
        private float alphaFrom;
        private float alphaTo;
        private bool hideAfterMove;
        private float ghostAlphaFrom;
        private Color dimFrom;
        private Color dimTo;
        private float scaleFrom;
        private float scaleTo;

        public DialoguePortraitSlot(Image image, bool rightSide)
        {
            this.image = image;
            rect = image.rectTransform;
            side = rightSide ? 1f : -1f;
            rest = rect.anchoredPosition;
            moveCompleted = OnMoveCompleted;
            fadeCompleted = HideGhost;
        }

        /// <summary>当前逻辑上显示的立绘（动画终态）；null = 空槽（含正在退场）。</summary>
        public Sprite Sprite => sprite;

        public void Configure(in DialogueMotionSettings settings) => motion = settings;

        /// <summary>
        /// 设置本槽立绘。空 → 有：滑入 + 淡入；有 → 空：滑出 + 淡出后隐藏；换图：交叉淡化；说话状态变化：压暗 / 高亮过渡。
        /// <paramref name="instant"/> 或物体未激活时直接置终态，不播动画（存档恢复路径）。
        /// </summary>
        public void Set(Sprite next, bool speakingNext, bool instant)
        {
            Sprite previous = sprite;
            sprite = next;
            speaking = speakingNext;
            if (instant || !image.gameObject.activeInHierarchy)
            {
                Finish();
                return;
            }
            if (next == null)
            {
                if (previous != null) Exit();
                return;
            }
            if (previous == null)
            {
                Enter(next);
                return;
            }
            if (previous != next) Crossfade(previous, next);
            Dim();
        }

        /// <summary>清空本槽并直接置终态（打开面板时用，免得上一段对白的立绘残留）。</summary>
        public void Clear()
        {
            sprite = null;
            speaking = false;
            Finish();
        }

        /// <summary>掐断所有补间并把表现直接摆到逻辑终态（面板停用 / 瞬间关闭时用，避免残影留着）。</summary>
        public void Finish()
        {
            CancelAll();
            HideGhost();
            rect.anchoredPosition = rest;
            image.sprite = sprite;
            image.enabled = sprite != null;
            SetAlpha(image, 1f);
            dimTo = TargetColor();
            scaleTo = TargetScale();
            ApplyDim(dimTo, scaleTo);
        }

        private void Enter(Sprite next)
        {
            if (fade.IsActive()) fade.Cancel();
            HideGhost();
            if (move.IsActive()) move.Cancel();
            if (dim.IsActive()) dim.Cancel();
            // 正在退场的又被叫回：从当前位置 / 透明度接着滑回原位，而不是跳到屏幕外重来。
            bool midExit = image.enabled && image.sprite != null;
            image.sprite = next;
            image.enabled = true;
            dimTo = TargetColor();
            scaleTo = TargetScale();
            ApplyDim(dimTo, scaleTo);
            moveFrom = midExit ? rect.anchoredPosition : Hidden();
            alphaFrom = midExit ? image.color.a : 0f;
            moveTo = rest;
            alphaTo = 1f;
            hideAfterMove = false;
            StartMove(Ease.OutCubic);
        }

        private void Exit()
        {
            if (fade.IsActive()) fade.Cancel();
            HideGhost();
            if (move.IsActive()) move.Cancel();
            moveFrom = rect.anchoredPosition;
            alphaFrom = image.color.a;
            moveTo = Hidden();
            alphaTo = 0f;
            hideAfterMove = true;
            StartMove(Ease.InCubic);
        }

        private void StartMove(Ease ease)
        {
            float seconds = motion.PortraitSlideSeconds;
            if (seconds <= 0f)
            {
                ApplyMove(1f);
                OnMoveCompleted();
                return;
            }
            ApplyMove(0f);
            // UpdateIgnoreTimeScale：对白期间世界时停（timeScale = 0），scaled 补间会冻住。
            move = LMotion.Create(0f, 1f, seconds)
                .WithEase(ease)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(moveCompleted)
                .Bind(this, (t, slot) => slot.ApplyMove(t))
                .AddTo(image.gameObject);
        }

        private void ApplyMove(float t)
        {
            rect.anchoredPosition = Vector2.LerpUnclamped(moveFrom, moveTo, t);
            SetAlpha(image, Mathf.LerpUnclamped(alphaFrom, alphaTo, t)); // lint-ok: 立绘动效是纯表现，不参与判定与快照
        }

        private void OnMoveCompleted()
        {
            if (!hideAfterMove) return;
            hideAfterMove = false;
            image.enabled = false;
            image.sprite = null;
            rect.anchoredPosition = rest;
        }

        private void Crossfade(Sprite previous, Sprite next)
        {
            // 入场还没播完就换表情：先把入场补到终点，再从满透明度开始交叉淡化。
            if (move.IsActive()) move.Complete();
            if (fade.IsActive()) fade.Cancel();
            EnsureGhost();
            RectTransform ghostRect = ghost.rectTransform;
            ghostRect.anchoredPosition = rect.anchoredPosition;
            ghostRect.localScale = rect.localScale;
            ghost.color = image.color;
            ghost.sprite = previous;
            ghost.enabled = true;
            ghostAlphaFrom = image.color.a;
            image.sprite = next;
            float seconds = motion.PortraitCrossfadeSeconds;
            if (seconds <= 0f)
            {
                ApplyFade(1f);
                HideGhost();
                return;
            }
            ApplyFade(0f);
            fade = LMotion.Create(0f, 1f, seconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(fadeCompleted)
                .Bind(this, (t, slot) => slot.ApplyFade(t))
                .AddTo(image.gameObject);
        }

        private void ApplyFade(float t)
        {
            SetAlpha(image, t);
            SetAlpha(ghost, ghostAlphaFrom * (1f - t));
        }

        private void Dim()
        {
            Color target = TargetColor();
            float targetScale = TargetScale();
            if (dim.IsActive())
            {
                if (SameRgb(dimTo, target) && Mathf.Approximately(scaleTo, targetScale)) return; // lint-ok: 立绘动效是纯表现，不参与判定与快照
                dim.Cancel();
            }
            Color current = image.color;
            float currentScale = rect.localScale.x;
            if (SameRgb(current, target) && Mathf.Approximately(currentScale, targetScale)) return; // lint-ok: 立绘动效是纯表现，不参与判定与快照
            dimFrom = current;
            dimTo = target;
            scaleFrom = currentScale;
            scaleTo = targetScale;
            float seconds = motion.PortraitDimSeconds;
            if (seconds <= 0f)
            {
                ApplyDim(dimTo, scaleTo);
                return;
            }
            dim = LMotion.Create(0f, 1f, seconds)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, (t, slot) => slot.ApplyDimProgress(t))
                .AddTo(image.gameObject);
        }

        private void ApplyDimProgress(float t) =>
            ApplyDim(Color.LerpUnclamped(dimFrom, dimTo, t), Mathf.LerpUnclamped(scaleFrom, scaleTo, t)); // lint-ok: 立绘动效是纯表现，不参与判定与快照

        // 只改 RGB 与缩放，透明度归入场 / 退场 / 交叉淡化管。
        private void ApplyDim(Color color, float scale)
        {
            color.a = image.color.a;
            image.color = color;
            rect.localScale = new Vector3(scale, scale, 1f);
        }

        private Color TargetColor() =>
            speaking ? Color.white : new Color(motion.DimRed, motion.DimGreen, motion.DimBlue, 1f);

        private float TargetScale() => speaking ? 1f : motion.PortraitDimScale;

        private Vector2 Hidden() => rest + new Vector2(side * motion.PortraitSlideDistance, 0f);

        private void EnsureGhost()
        {
            // Image 是 UnityEngine.Object，判空只用 == null。
            if (ghost != null) return;
            // 复制主图物体（同 RectTransform 锚点 / 尺寸 / 保持比例等参数），放在主图同级、紧挨其下先画；不改预制体资产。
            ghost = Object.Instantiate(image, rect.parent, false);
            ghost.name = image.name + "Ghost";
            ghost.raycastTarget = false;
            ghost.transform.SetSiblingIndex(rect.GetSiblingIndex());
            ghost.enabled = false;
        }

        private void HideGhost()
        {
            if (ghost == null) return;
            ghost.enabled = false;
            ghost.sprite = null;
        }

        private void CancelAll()
        {
            if (move.IsActive()) move.Cancel();
            if (fade.IsActive()) fade.Cancel();
            if (dim.IsActive()) dim.Cancel();
            hideAfterMove = false;
        }

        private static bool SameRgb(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b); // lint-ok: 立绘动效是纯表现，不参与判定与快照

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
