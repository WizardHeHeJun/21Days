// 职责：保存玩家运行状态与回放快照；SO 配置与 JSON 存档都不适合存逐 tick 状态。
using Game.Core.Replay;
using UnityEngine;

namespace Game.Player
{
    public sealed class PlayerModel : IReplayState
    {
        public Vector2 Position { get; internal set; }
        public Vector2 Facing { get; internal set; } = Vector2.right;
        public bool IsSneaking { get; internal set; }

        /// <summary>
        /// 奔跑模式是否开启（Run 键按下沿切换）。潜行按住时移动临时按潜行速度走，但本字段不变，
        /// 松开潜行后恢复奔跑。它跨 tick 持续，所以进回放快照与存档。
        /// </summary>
        public bool IsRunning { get; internal set; }

        public bool IsDisguised { get; internal set; }
        public int Health { get; internal set; }
        public float AttackCooldownLeft { get; internal set; }
        public bool PreviousDisguise { get; internal set; }
        public bool PreviousAttack { get; internal set; }

        /// <summary>上 tick 的 Run 键状态，用于按下沿判定；进快照，恢复后长按不会被误判为再切一次。</summary>
        public bool PreviousRun { get; internal set; }

        // 快照给的是「有效奔跑」：潜行按住时虽保留奔跑模式，但实际按潜行走，跨模块读者不该看成在跑。
        public PlayerSnapshot Snapshot => new PlayerSnapshot(Position, Facing, IsSneaking, IsDisguised, Health, IsRunning && !IsSneaking);

        public PlayerSaveData Capture() => new PlayerSaveData
        {
            PositionX = Position.x,
            PositionY = Position.y,
            FacingX = Facing.x,
            FacingY = Facing.y,
            IsSneaking = IsSneaking,
            IsDisguised = IsDisguised,
            Health = Health,
            AttackCooldownLeft = AttackCooldownLeft,
            PreviousDisguise = PreviousDisguise,
            PreviousAttack = PreviousAttack,
            IsRunning = IsRunning,
            PreviousRun = PreviousRun,
        };

        public void Restore(PlayerSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            Position = new Vector2(saved.PositionX, saved.PositionY);
            Facing = new Vector2(saved.FacingX, saved.FacingY);
            IsSneaking = saved.IsSneaking;
            IsDisguised = saved.IsDisguised;
            Health = saved.Health;
            AttackCooldownLeft = saved.AttackCooldownLeft;
            PreviousDisguise = saved.PreviousDisguise;
            PreviousAttack = saved.PreviousAttack;
            IsRunning = saved.IsRunning;
            PreviousRun = saved.PreviousRun;
        }

        public void Serialize(IStateWriter writer)
        {
            writer.WriteVector2(Position);
            writer.WriteVector2(Facing);
            writer.WriteBool(IsSneaking);
            writer.WriteBool(IsDisguised);
            writer.WriteInt(Health);
            writer.WriteFloat(AttackCooldownLeft);
            writer.WriteBool(PreviousDisguise);
            writer.WriteBool(PreviousAttack);
            // 以下两项为回放格式 v4 追加，只能加在流末尾（顺序即格式）。
            writer.WriteBool(IsRunning);
            writer.WriteBool(PreviousRun);
        }

        public void Deserialize(IStateReader reader)
        {
            Position = reader.ReadVector2();
            Facing = reader.ReadVector2();
            IsSneaking = reader.ReadBool();
            IsDisguised = reader.ReadBool();
            Health = reader.ReadInt();
            AttackCooldownLeft = reader.ReadFloat();
            PreviousDisguise = reader.ReadBool();
            PreviousAttack = reader.ReadBool();
            IsRunning = reader.ReadBool();
            PreviousRun = reader.ReadBool();
        }
    }
}
