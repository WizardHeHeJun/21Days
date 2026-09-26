// 职责：场景演出触发器的纯判定——「只播一次」已播过、服务正忙时不触发，并给出原因。
// 为什么新建（复用 → 扩展 → 新建）：判定逻辑要脱离场景与服务单测，PerformanceRules 是单段演出的阶段机，
//   触发判定不属于它的职责；工程里没有可复用的「一次性触发」判定。

namespace Game.Performance
{
    /// <summary>触发判定。全部静态、无分配。</summary>
    public static class PerformanceTriggerRules
    {
        /// <summary>原因：只播一次且已播过。</summary>
        public const string ReasonPlayed = "played";

        /// <summary>原因：已有演出在播放。</summary>
        public const string ReasonBusy = "busy";

        /// <summary>
        /// 是否应触发。已播过优先于忙碌（已播过是永久原因，更有诊断价值）。
        /// </summary>
        /// <param name="once">是否只播一次。</param>
        /// <param name="hasPlayed">存档里是否已播过（once 为 false 时忽略）。</param>
        /// <param name="serviceRunning">服务是否正在播放。</param>
        /// <param name="reason">不触发时为 <see cref="ReasonPlayed"/> / <see cref="ReasonBusy"/>；触发时为 null。</param>
        public static bool ShouldFire(bool once, bool hasPlayed, bool serviceRunning, out string reason)
        {
            if (once && hasPlayed)
            {
                reason = ReasonPlayed;
                return false;
            }
            if (serviceRunning)
            {
                reason = ReasonBusy;
                return false;
            }
            reason = null;
            return true;
        }
    }
}
