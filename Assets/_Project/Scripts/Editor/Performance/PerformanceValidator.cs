// 职责：演出预制体的纯校验——输入舞台根物体（预制体资产 / 预制体模式内容 / 内存物体都行），输出问题列表；
//   不改任何资产、不打日志，窗口、自定义 Inspector 与测试共用这一份判定。
// 为什么新建（复用 → 扩展 → 新建）：AssetAuditWindow 查的是通用资产体检（缺引用、命名），不认识时间轴轨道与演员绑定；
//   把演出规则塞进去名实不符，且那边是窗口类不便单测。演出校验需要一个独立的、可测的纯函数，只能新建。
using System;
using System.Collections.Generic;
using Game.Performance;
using Game.Performance.Timeline;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Editor.Performance
{
    /// <summary>
    /// 演出校验器。规则见 PRP/performance-pipeline/prp.md 2.8：
    /// 舞台 / Director / 时间轴 / 时长、舞台相机（Overlay、只剔 Performance 层）、图层、字幕正文、表情绑定与表情名、停顿标记位置、Addressables 地址。
    /// </summary>
    public static class PerformanceValidator
    {
        /// <summary>演出专用图层名。</summary>
        public const string PerformanceLayerName = "Performance";

        /// <summary>
        /// URP 相机附加数据的类型名。Game.Editor 程序集没有引用 URP Runtime（本波不改该 asmdef），
        /// 所以按名字取类型、用 SerializedObject 读写它的序列化字段 m_CameraType（0 = Base，1 = Overlay）。
        /// </summary>
        internal const string UrpCameraDataTypeName =
            "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime";

        internal const string UrpCameraTypeProperty = "m_CameraType";
        internal const int UrpOverlayValue = 1;

        /// <summary>校验一个舞台根物体。<paramref name="expectedAddress"/> 非空时同时检查 Addressables 登记。</summary>
        public static List<PerformanceIssue> Validate(GameObject stageRoot, string expectedAddress = null)
        {
            var issues = new List<PerformanceIssue>();
            if (stageRoot == null)
            {
                issues.Add(Error("stage_root_missing", "没有传入演出预制体。", null));
                return issues;
            }

            var stage = stageRoot.GetComponent<PerformanceStage>();
            if (stage == null)
                issues.Add(Error("stage_missing", $"「{stageRoot.name}」根物体上没有 PerformanceStage 组件，它不是一段演出。", stageRoot));

            PlayableDirector director = stage != null && stage.Director != null ? stage.Director : stageRoot.GetComponent<PlayableDirector>();
            TimelineAsset timeline = null;
            if (director == null)
            {
                issues.Add(Error("director_missing", "根物体上没有 PlayableDirector，时间轴没地方播。", stageRoot));
            }
            else
            {
                timeline = director.playableAsset as TimelineAsset;
                if (timeline == null)
                    issues.Add(Error("timeline_missing", "PlayableDirector 没有挂时间轴资产（Playable 一栏是空的或不是 Timeline）。", director));
            }

            if (timeline != null && timeline.duration <= 0d)
                issues.Add(Error("duration_zero", "时间轴总时长是 0 秒，播出来一闪就结束。", timeline));

            int layer = LayerMask.NameToLayer(PerformanceLayerName);
            if (stage != null) CheckCamera(stage, layer, issues);
            CheckLayers(stageRoot, layer, issues);

            if (timeline != null) CheckTracks(timeline, director, issues);

            if (!string.IsNullOrEmpty(expectedAddress)) CheckAddress(stageRoot, expectedAddress, issues);

            return issues;
        }

        /// <summary>该相机是否是 URP Overlay 相机（没有 URP 附加数据视为 Base）。</summary>
        internal static bool IsOverlayCamera(Camera camera)
        {
            if (camera == null) return false;
            Type dataType = Type.GetType(UrpCameraDataTypeName);
            if (dataType == null) return false;
            Component data = camera.GetComponent(dataType);
            if (data == null) return false;
            using (var so = new SerializedObject(data))
            {
                SerializedProperty prop = so.FindProperty(UrpCameraTypeProperty);
                return prop != null && prop.intValue == UrpOverlayValue;
            }
        }

        /// <summary>
        /// 找到物体对应的预制体资产路径：预制体资产本身、预制体模式里的内容、场景里的预制体实例都能认；认不出返回 null。
        /// </summary>
        internal static string ResolvePrefabAssetPath(GameObject go)
        {
            if (go == null) return null;
            string path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path)) return path;
            PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(go);
            if (prefabStage != null) return prefabStage.assetPath;
            path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private static void CheckCamera(PerformanceStage stage, int layer, List<PerformanceIssue> issues)
        {
            Camera camera = stage.StageCamera;
            if (camera == null)
            {
                issues.Add(Error("camera_missing", "PerformanceStage 的舞台相机（Stage Camera）没接，演出画面出不来。", stage));
                return;
            }

            if (!IsOverlayCamera(camera))
                issues.Add(Warning("camera_not_overlay", "舞台相机不是 URP Overlay（Render Type 应为 Overlay），会盖掉游戏画面。", camera));

            if (layer >= 0 && (camera.cullingMask & ~(1 << layer)) != 0)
                issues.Add(Warning("camera_culling_extra", "舞台相机的剔除遮罩（Culling Mask）除了 Performance 还勾了别的层，会把游戏场景再画一遍。", camera));
        }

        private static void CheckLayers(GameObject stageRoot, int layer, List<PerformanceIssue> issues)
        {
            if (layer < 0)
            {
                issues.Add(Warning("layer_missing", "工程里还没有名为 Performance 的图层（Project Settings → Tags and Layers），舞台相机没法只拍演出物体。", stageRoot));
                return;
            }

            Transform[] all = stageRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i].gameObject;
                if (go.layer != layer)
                    issues.Add(Warning("layer_mismatch", $"「{go.name}」不在 Performance 图层上，舞台相机拍不到它（或主相机会拍到它）。", go));
            }
        }

        private static void CheckTracks(TimelineAsset timeline, PlayableDirector director, List<PerformanceIssue> issues)
        {
            double duration = timeline.duration;
            var visited = new HashSet<TrackAsset>();

            if (timeline.markerTrack != null)
            {
                visited.Add(timeline.markerTrack);
                CheckHoldMarkers(timeline.markerTrack, duration, issues);
            }

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track == null || !visited.Add(track)) continue;
                CheckHoldMarkers(track, duration, issues);

                if (track is SubtitleTrack)
                    CheckSubtitleClips(track, issues);
                else if (track is ExpressionTrack)
                    CheckExpressionTrack(track, director, issues);
            }
        }

        private static void CheckSubtitleClips(TrackAsset track, List<PerformanceIssue> issues)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                var subtitle = clip.asset as SubtitleClip;
                if (subtitle == null) continue;
                if (string.IsNullOrWhiteSpace(subtitle.Text))
                    issues.Add(Warning("subtitle_empty", $"字幕轨「{track.name}」在 {clip.start:0.##} 秒处的字幕片段没有写正文。", subtitle));
            }
        }

        private static void CheckExpressionTrack(TrackAsset track, PlayableDirector director, List<PerformanceIssue> issues)
        {
            var actor = director.GetGenericBinding(track) as PerformanceActor;
            if (actor == null)
            {
                issues.Add(Error("expression_unbound", $"表情轨「{track.name}」没有绑定演员（轨道左侧的绑定槽是空的），表情不会切换。", track));
                return;
            }

            IReadOnlyList<string> names = actor.ExpressionNames;
            foreach (TimelineClip clip in track.GetClips())
            {
                var expression = clip.asset as ExpressionClip;
                if (expression == null) continue;
                if (!Contains(names, expression.ExpressionName))
                    issues.Add(Error(
                        "expression_unknown",
                        $"表情轨「{track.name}」在 {clip.start:0.##} 秒处要切「{expression.ExpressionName}」，但演员「{actor.name}」没有这个表情。",
                        expression));
            }
        }

        private static void CheckHoldMarkers(TrackAsset track, double duration, List<PerformanceIssue> issues)
        {
            foreach (IMarker marker in track.GetMarkers())
            {
                if (!(marker is HoldMarker hold)) continue;
                if (hold.time <= 0d || hold.time >= duration)
                    issues.Add(Warning(
                        "hold_out_of_range",
                        $"停顿标记在 {hold.time:0.##} 秒，落在时间轴开头或末尾之外（总长 {duration:0.##} 秒），玩家可能等不到或刚开场就卡住。",
                        hold));
            }
        }

        private static void CheckAddress(GameObject stageRoot, string expectedAddress, List<PerformanceIssue> issues)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            string path = ResolvePrefabAssetPath(stageRoot);
            AddressableAssetEntry entry = null;
            if (settings != null && !string.IsNullOrEmpty(path))
                entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));

            if (entry == null || !string.Equals(entry.address, expectedAddress, StringComparison.Ordinal))
                issues.Add(Error(
                    "address_missing",
                    $"Addressables 里没有把这个预制体登记成地址「{expectedAddress}」，游戏里按这个 id 播不出来。",
                    stageRoot));
        }

        private static bool Contains(IReadOnlyList<string> names, string value)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static PerformanceIssue Error(string code, string message, UnityEngine.Object context) =>
            new PerformanceIssue(PerformanceIssueSeverity.Error, code, message, context);

        private static PerformanceIssue Warning(string code, string message, UnityEngine.Object context) =>
            new PerformanceIssue(PerformanceIssueSeverity.Warning, code, message, context);
    }
}
