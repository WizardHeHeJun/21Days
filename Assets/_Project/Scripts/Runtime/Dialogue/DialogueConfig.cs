// 职责：对白表现参数；通用 UIConfig 不包含阅读速度，内容仍由内容表提供。
using System.Collections.Generic;
using UnityEngine;

namespace Game.Dialogue
{
    [CreateAssetMenu(menuName = "21Days/Dialogue/Config")]
    public sealed class DialogueConfig : ScriptableObject
    {
        [SerializeField, Min(1)] private float charactersPerSecond = 35f;
        [SerializeField, Min(1)] private int historyLimit = 500;
        [SerializeField] private float[] speedSteps = { 1f, 2f, 4f };
        [SerializeField, Min(1)] private int revealTapCount = 3;
        [SerializeField, Min(0.05f)] private float tapWindowSeconds = 0.5f;
        [SerializeField, Min(0)] private float autoAdvanceSeconds = 1.5f;

        [Header("文字节奏")]
        [Tooltip("打出标点后停顿多少秒（x1 档，倍速下同比缩短）；连续标点只在最后一个后停一次，句末标点不停。0 = 不停顿。")]
        [SerializeField, Min(0)] private float punctuationPauseSeconds = 0.12f;
        [Tooltip("哪些字符算标点（逐字符匹配）；留空 = 不停顿。")]
        [SerializeField] private string punctuationChars = "，。！？…；：、,.!?";

        [Header("立绘动效")]
        [Tooltip("立绘入场 / 退场的水平滑动距离（参考分辨率像素）；左槽从左侧进出，右槽从右侧进出。")]
        [SerializeField, Min(0)] private float portraitSlideDistance = 80f;
        [Tooltip("立绘入场 / 退场时长（秒，不受时停影响）；0 = 直接出现 / 消失。")]
        [SerializeField, Min(0)] private float portraitSlideSeconds = 0.25f;
        [Tooltip("同一槽换表情时新旧立绘交叉淡化的时长（秒）；0 = 直接换图。")]
        [SerializeField, Min(0)] private float portraitCrossfadeSeconds = 0.15f;
        [Tooltip("说话者高亮 / 非说话者压暗的过渡时长（秒）；0 = 直接切换。")]
        [SerializeField, Min(0)] private float portraitDimSeconds = 0.15f;
        [Tooltip("非说话者（含旁白时的两侧）立绘的颜色，只用 RGB，透明度忽略。")]
        [SerializeField] private Color portraitDimColor = new Color(0.55f, 0.55f, 0.6f, 1f);
        [Tooltip("非说话者立绘的缩放（说话者为 1）。")]
        [SerializeField, Min(0.01f)] private float portraitDimScale = 0.96f;

        [Header("名牌")]
        [Tooltip("说话者名字变化时名牌从放大 + 透明回落到原样的时长（秒）；同名连续两句不动。0 = 不做动效。")]
        [SerializeField, Min(0)] private float nameTagPunchSeconds = 0.15f;
        [Tooltip("名牌切换动效的起始缩放（回落到 1）。")]
        [SerializeField, Min(0.01f)] private float nameTagPunchScale = 1.15f;

        public float CharactersPerSecond => charactersPerSecond;
        public int HistoryLimit => historyLimit;
        public IReadOnlyList<float> SpeedSteps => speedSteps;
        public int RevealTapCount => revealTapCount;
        public float TapWindowSeconds => tapWindowSeconds;
        public float AutoAdvanceSeconds => autoAdvanceSeconds;
        public float PunctuationPauseSeconds => punctuationPauseSeconds;
        public string PunctuationChars => punctuationChars;
        public float PortraitSlideDistance => portraitSlideDistance;
        public float PortraitSlideSeconds => portraitSlideSeconds;
        public float PortraitCrossfadeSeconds => portraitCrossfadeSeconds;
        public float PortraitDimSeconds => portraitDimSeconds;
        public Color PortraitDimColor => portraitDimColor;
        public float PortraitDimScale => portraitDimScale;
        public float NameTagPunchSeconds => nameTagPunchSeconds;
        public float NameTagPunchScale => nameTagPunchScale;

        // 非法配置（如空倍速表）在这里抛 ArgumentException，由调用方在对话开始前暴露。
        public DialoguePlaybackSettings ToPlaybackSettings() =>
            new DialoguePlaybackSettings(charactersPerSecond, speedSteps, revealTapCount, tapWindowSeconds, autoAdvanceSeconds,
                punctuationPauseSeconds, punctuationChars,
                new DialogueMotionSettings(portraitSlideDistance, portraitSlideSeconds, portraitCrossfadeSeconds,
                    portraitDimSeconds, portraitDimScale, portraitDimColor.r, portraitDimColor.g, portraitDimColor.b,
                    nameTagPunchSeconds, nameTagPunchScale));
    }
}
