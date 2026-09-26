// 职责：对白面板动效参数的校验后快照（立绘入场 / 退场 / 交叉淡化 / 说话者压暗、名牌切换），由 Controller 交给 DialogueView。
// 新建原因：复用——DialogueView 不注入服务、拿不到 DialogueConfig，参数只能沿 Config → PlaybackSettings → Policy → Controller 这条已有链路下发；
//   扩展——直接把这 9 个参数平铺进 DialoguePlaybackSettings 会让它的构造函数膨胀到 15 个参数，且这组参数只有 View 用、与策略类无关，
//   所以单独成一个纯 C# 值类型挂在 PlaybackSettings 上。压暗色用三个 float 而非 Color，保持不依赖 UnityEngine（策略与 EditMode 测试可直接构造）。
using System;

namespace Game.Dialogue
{
    public readonly struct DialogueMotionSettings
    {
        public DialogueMotionSettings(float portraitSlideDistance, float portraitSlideSeconds, float portraitCrossfadeSeconds,
            float portraitDimSeconds, float portraitDimScale, float dimRed, float dimGreen, float dimBlue,
            float nameTagPunchSeconds, float nameTagPunchScale)
        {
            if (!(portraitSlideDistance >= 0f)) throw new ArgumentException("立绘滑入距离不可为负", nameof(portraitSlideDistance));
            if (!(portraitSlideSeconds >= 0f)) throw new ArgumentException("立绘滑入时长不可为负", nameof(portraitSlideSeconds));
            if (!(portraitCrossfadeSeconds >= 0f)) throw new ArgumentException("表情交叉淡化时长不可为负", nameof(portraitCrossfadeSeconds));
            if (!(portraitDimSeconds >= 0f)) throw new ArgumentException("压暗过渡时长不可为负", nameof(portraitDimSeconds));
            if (!(portraitDimScale > 0f)) throw new ArgumentException("压暗缩放必须大于 0", nameof(portraitDimScale));
            if (!InUnitRange(dimRed) || !InUnitRange(dimGreen) || !InUnitRange(dimBlue))
                throw new ArgumentException("压暗颜色分量必须在 0～1 之间", nameof(dimRed));
            if (!(nameTagPunchSeconds >= 0f)) throw new ArgumentException("名牌切换时长不可为负", nameof(nameTagPunchSeconds));
            if (!(nameTagPunchScale > 0f)) throw new ArgumentException("名牌起始缩放必须大于 0", nameof(nameTagPunchScale));
            PortraitSlideDistance = portraitSlideDistance;
            PortraitSlideSeconds = portraitSlideSeconds;
            PortraitCrossfadeSeconds = portraitCrossfadeSeconds;
            PortraitDimSeconds = portraitDimSeconds;
            PortraitDimScale = portraitDimScale;
            DimRed = dimRed;
            DimGreen = dimGreen;
            DimBlue = dimBlue;
            NameTagPunchSeconds = nameTagPunchSeconds;
            NameTagPunchScale = nameTagPunchScale;
            IsValid = true;
        }

        /// <summary>代码默认值，与 <see cref="DialogueConfig"/> 字段默认一致；测试或未传动效参数的播放设置用它。</summary>
        public static DialogueMotionSettings Default =>
            new DialogueMotionSettings(80f, 0.25f, 0.15f, 0.15f, 0.96f, 0.55f, 0.55f, 0.6f, 0.15f, 1.15f);

        /// <summary>立绘入场 / 退场的水平滑动距离（参考分辨率像素）；左槽从左侧进出、右槽从右侧进出。</summary>
        public float PortraitSlideDistance { get; }
        /// <summary>立绘入场 / 退场时长（秒，unscaled）；0 = 直接到位。</summary>
        public float PortraitSlideSeconds { get; }
        /// <summary>同槽换表情的交叉淡化时长（秒）；0 = 直接换图。</summary>
        public float PortraitCrossfadeSeconds { get; }
        /// <summary>说话者高亮 / 非说话者压暗的过渡时长（秒）；0 = 直接切换。</summary>
        public float PortraitDimSeconds { get; }
        /// <summary>非说话者的缩放（说话者为 1）。</summary>
        public float PortraitDimScale { get; }
        public float DimRed { get; }
        public float DimGreen { get; }
        public float DimBlue { get; }
        /// <summary>说话者名字变化时名牌 punch 的时长（秒）；0 = 不做动效。</summary>
        public float NameTagPunchSeconds { get; }
        /// <summary>名牌 punch 的起始缩放（回落到 1）。</summary>
        public float NameTagPunchScale { get; }
        /// <summary>经构造函数校验过；default 实例为 false，使用方应回退 <see cref="Default"/>。</summary>
        public bool IsValid { get; }

        private static bool InUnitRange(float value) => value >= 0f && value <= 1f;
    }
}
