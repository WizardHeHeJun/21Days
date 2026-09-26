// 职责：退出游戏的唯一实现——记一条日志，编辑器里退出 Play 模式，出包后 Application.Quit()。
// 为什么新建（按「复用 → 扩展 → 新增」的顺序）：
//   1. 复用不行：原实现是 PauseMenuController.HandleQuit，私有方法，外部调不到；
//   2. 扩展不行：把它改成 public 让 TitleState 调，等于让「标题」这个状态依赖「暂停菜单控制器」，
//      依赖方向不对（标题在暂停菜单之前，也不该认识暂停会话）；退出和暂停菜单本来就是两件事。
//   所以抽成 Boot/ 下一个无状态静态小类，暂停菜单与标题界面共用，将来别处的「退出」也调它。

using Game.Core.Logging;
using UnityEngine;

namespace Game.Core.Boot
{
    /// <summary>
    /// 退出游戏。调用方只管说「为什么退」，平台差异（编辑器 / 出包）在这里处理。
    /// <para>触屏为主的平台（手机）一般不给退出按钮，是否显示由界面按 <c>IPlatformService.IsTouchPrimary</c> 决定，不在这里判断。</para>
    /// </summary>
    public static class GameQuit
    {
        /// <summary>退出游戏。</summary>
        /// <param name="reason">谁发起的退出（如「暂停菜单」「标题界面」），只用于日志。</param>
        public static void Quit(string reason)
        {
            Log.Info($"退出游戏（来源：{reason}）");
#if UNITY_EDITOR
            // 仅调试用途：编辑器里 Application.Quit 不生效，改为退出 Play 模式。
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
