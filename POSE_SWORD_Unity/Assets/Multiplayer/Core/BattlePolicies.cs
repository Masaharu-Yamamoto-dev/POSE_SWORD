using System;
using System.Collections.Generic;
using System.Linq;

namespace PoseSword.Multiplayer
{
    public sealed class TargetCandidate
    {
        public readonly string Id;
        public readonly int Slot;
        public readonly float X, Y;
        public readonly bool Alive;
        public TargetCandidate(string id, int slot, float x, float y, bool alive)
        { Id = id; Slot = slot; X = x; Y = y; Alive = alive; }
    }

    public static class BattlePolicies
    {
        // ▼ 通常クリック/タップ用：SPが必殺技分たまっていてもここでは発動しない（ジャンプ・小ダッシュのみ）
        public static string PrimaryAction(bool koma, float sp, bool started, bool alive, bool dashing, bool right)
        {
            if (!started || !alive || dashing || float.IsNaN(sp) || float.IsInfinity(sp)) return null;
            if (koma) return sp >= 20 ? "KomaDash" : null;
            return right ? "JumpRight" : "JumpLeft";
        }

        // ▼ 必殺技専用ボタン/スペースキー用：SPが必殺技分たまっている時だけ発動する
        public static string UltimateAction(bool koma, float sp, bool started, bool alive, bool dashing)
        {
            if (!started || !alive || dashing || float.IsNaN(sp) || float.IsInfinity(sp)) return null;
            if (koma) return sp >= 70 ? "Tornado" : null;
            return sp >= 100 ? "SwordDash" : null;
        }

        public static string Target(string self, float x, float y, IEnumerable<TargetCandidate> candidates,
            string currentId, bool locked)
        {
            var targets = candidates.Where(c => c.Alive && c.Id != self)
                .Select(c => new { Candidate = c, Distance = Math.Pow(c.X - x, 2) + Math.Pow(c.Y - y, 2) })
                .OrderBy(c => c.Distance).ThenBy(c => c.Candidate.Slot).ToArray();
            if (targets.Length == 0) return null;
            var current = targets.FirstOrDefault(t => t.Candidate.Id == currentId);
            if (current != null && (locked || targets[0].Distance >= current.Distance * 0.64)) return currentId;
            return targets[0].Candidate.Id;
        }
    }
}
