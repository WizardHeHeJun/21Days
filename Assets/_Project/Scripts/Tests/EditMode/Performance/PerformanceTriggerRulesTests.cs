// 职责：钉住 PerformanceTriggerRules 的触发判定（once / played / busy 与原因优先级）。
// 为什么新建：Performance 模块首次落地（PRP/performance-pipeline 波 1），一个被测类一个测试类。
using Game.Performance;
using NUnit.Framework;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceTriggerRulesTests
    {
        [Test]
        public void ShouldFire_FreshAndIdle_Fires()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, false, false, out string reason), Is.True);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void ShouldFire_OnceAndPlayed_SkipsAsPlayed()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, true, false, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(PerformanceTriggerRules.ReasonPlayed));
        }

        [Test]
        public void ShouldFire_NotOnceButPlayed_Fires()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(false, true, false, out string reason), Is.True);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void ShouldFire_ServiceRunning_SkipsAsBusy()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, false, true, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(PerformanceTriggerRules.ReasonBusy));
        }

        [Test]
        public void ShouldFire_PlayedAndBusy_ReportsPlayedFirst()
        {
            Assert.That(PerformanceTriggerRules.ShouldFire(true, true, true, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(PerformanceTriggerRules.ReasonPlayed));
        }

        [Test]
        public void ReasonConstants_MatchTelemetryValues()
        {
            Assert.That(PerformanceTriggerRules.ReasonPlayed, Is.EqualTo("played"));
            Assert.That(PerformanceTriggerRules.ReasonBusy, Is.EqualTo("busy"));
        }
    }
}
