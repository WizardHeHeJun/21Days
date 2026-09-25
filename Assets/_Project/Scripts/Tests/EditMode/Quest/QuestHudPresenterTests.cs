// 职责：QuestHudPresenter 任务键（Gameplay/Journal）两段纯逻辑的 EditMode 回归：何时开面板、键位提示取哪条绑定。
// 为什么新建：对应被测类一个测试类；现有 Quest 测试各对一个被测类（通知、规则、指引数学），塞进去名实不符。
//   两个被测方法都是纯静态函数，不需要容器、事件总线与 UIService 就能覆盖。

using Game.Quest;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Quest
{
    public sealed class QuestHudPresenterTests
    {
        [Test]
        public void ShouldOpenOnJournal_WhenHudReadyAndIdle_ReturnsTrue()
        {
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(true, false, false), Is.True);
        }

        [Test]
        public void ShouldOpenOnJournal_WhenBlocked_ReturnsFalse()
        {
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(false, false, false), Is.False, "HUD 还没开好（标题界面前）");
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(true, true, false), Is.False, "对白进行中");
            Assert.That(QuestHudPresenter.ShouldOpenOnJournal(true, false, true), Is.False, "面板已开着，任务键不负责关");
        }

        [Test]
        public void KeyboardBindingDisplay_WithKeyboardAndGamepad_ReturnsKeyboardText()
        {
            // 与 GameInput 里 Gameplay/Journal 同形：先手柄后键盘也要取到键盘那条。
            var action = new InputAction("Journal", InputActionType.Button);
            action.AddBinding("<Gamepad>/select");
            action.AddBinding("<Keyboard>/tab");
            try
            {
                Assert.That(QuestHudPresenter.KeyboardBindingDisplay(action), Is.EqualTo("Tab"));
            }
            finally
            {
                action.Dispose();
            }
        }

        [Test]
        public void KeyboardBindingDisplay_WithoutKeyboardBinding_ReturnsEmpty()
        {
            var action = new InputAction("Journal", InputActionType.Button);
            action.AddBinding("<Gamepad>/select");
            try
            {
                Assert.That(QuestHudPresenter.KeyboardBindingDisplay(action), Is.Empty);
                Assert.That(QuestHudPresenter.KeyboardBindingDisplay(null), Is.Empty);
            }
            finally
            {
                action.Dispose();
            }
        }
    }
}
