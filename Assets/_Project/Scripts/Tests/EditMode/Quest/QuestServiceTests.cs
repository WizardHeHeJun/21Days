// 职责：钉住 QuestService.ResetProgress——推进若干任务后重置，进度回到新开局（主线 1001 追踪、计数清零），
//   存档分区同步写回，并补发 HUD / 面板刷新所需的事件。
// 为什么新建：QuestService 此前没有测试文件（QuestRulesTests 测纯规则，不经过门面的存档写回与事件发布），
//   按「测试类 = <被测类>Tests」单独成文件。数据用真实生成的任务表（与 QuestCatalogTests 同一来源）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Quest;
using Game.Tests.EditMode.Core;
using MessagePipe;
using NUnit.Framework;

namespace Game.Tests.EditMode.Quest
{
    /// <summary><see cref="QuestService"/> 的 EditMode 测试。不经容器，依赖全部用最小假实现。</summary>
    public sealed class QuestServiceTests
    {
        private const int MainQuest = 1001;
        private const int CrateQuest = 2002;

        private FakeSaveService saves;
        private FakePublisher<QuestObjectiveProgressedEvent> progressed;
        private FakePublisher<QuestTrackingChangedEvent> tracking;
        private QuestService service;

        [SetUp]
        public void SetUp()
        {
            global::cfg.Tables tables = ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes());
            var catalog = new QuestCatalog(new FakeConfigService(tables), NullTelemetryScope.Instance);
            saves = new FakeSaveService();
            progressed = new FakePublisher<QuestObjectiveProgressedEvent>();
            tracking = new FakePublisher<QuestTrackingChangedEvent>();
            service = new QuestService(
                catalog,
                saves,
                new FakePublisher<QuestActivatedEvent>(),
                progressed,
                new FakePublisher<QuestCompletedEvent>(),
                tracking,
                NullTelemetryScope.Instance);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(service.IsReady, Is.True, "真实任务表应能通过校验");
        }

        [TearDown]
        public void TearDown() => service.Dispose();

        [Test]
        public void ResetProgress_AfterAdvancing_ReturnsToInitialState()
        {
            service.Report(QuestObjectiveKind.TalkTo, MainQuest.ToString());
            service.Report(QuestObjectiveKind.Counter, "crate", 2);
            service.Track(CrateQuest);
            Assert.That(service.TryGet(MainQuest, out QuestProgress main), Is.True);
            Assert.That(main.ObjectiveIndex, Is.EqualTo(1), "前置：主线应已推进到第二个目标");
            Assert.That(service.TrackedId, Is.EqualTo(CrateQuest), "前置：追踪已切到支线");

            progressed.Received.Clear();
            tracking.Received.Clear();
            service.ResetProgress();

            Assert.That(service.TrackedId, Is.EqualTo(MainQuest), "重置后追踪回到默认主线");
            Assert.That(service.TryGet(MainQuest, out main), Is.True);
            Assert.That(main.State, Is.EqualTo(QuestState.InProgress));
            Assert.That(main.ObjectiveIndex, Is.Zero, "主线目标回到第一个");
            Assert.That(main.Count, Is.Zero);
            Assert.That(service.TryGet(CrateQuest, out QuestProgress crates), Is.True);
            Assert.That(crates.State, Is.EqualTo(QuestState.InProgress));
            Assert.That(crates.Count, Is.Zero, "物资箱计数清零");

            QuestSaveData saved = saves.Get<QuestSaveData>();
            Assert.That(saved.TrackedId, Is.EqualTo(MainQuest), "存档分区同步写回");
            Assert.That(saved.Quests.Exists(q => q.Id == CrateQuest && q.Count == 0), Is.True);
        }

        [Test]
        public void ResetProgress_PublishesTrackingAndProgressEventsForRefresh()
        {
            service.Report(QuestObjectiveKind.Counter, "crate");
            progressed.Received.Clear();
            tracking.Received.Clear();

            service.ResetProgress();

            Assert.That(tracking.Received.Count, Is.GreaterThanOrEqualTo(1), "至少一次追踪变更，HUD 据此刷新");
            Assert.That(tracking.Received[tracking.Received.Count - 1].QuestId, Is.EqualTo(MainQuest));
            Assert.That(progressed.Received.Exists(e => e.QuestId == CrateQuest && e.Count == 0), Is.True,
                "进行中任务各发一条计数归零的进度事件");
        }

        /// <summary>只记录收到的消息。</summary>
        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public List<T> Received { get; } = new List<T>();
            public void Publish(T message) => Received.Add(message);
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>。</summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables) => Tables = tables;
            public global::cfg.Tables Tables { get; }
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>内存分区：Get 按类型懒建；落盘相关一律不支持（本文件用不到）。</summary>
        private sealed class FakeSaveService : ISaveService
        {
            private readonly Dictionary<Type, ISaveData> parts = new Dictionary<Type, ISaveData>();

            public T Get<T>() where T : class, ISaveData, new()
            {
                if (!parts.TryGetValue(typeof(T), out ISaveData part))
                {
                    part = new T();
                    parts[typeof(T)] = part;
                }

                return (T)part;
            }

            public UniTask<bool> SaveAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask<bool> LoadAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default) => throw new NotSupportedException();
            public SaveSnapshot Capture() => throw new NotSupportedException();
            public void Commit(SaveSnapshot snapshot) => throw new NotSupportedException();

            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException();

            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default)
                where T : class, new() => throw new NotSupportedException();

            public UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class =>
                throw new NotSupportedException();

            public bool Exists(int slot) => false;
            public void Delete(int slot) => throw new NotSupportedException();
        }
    }
}
