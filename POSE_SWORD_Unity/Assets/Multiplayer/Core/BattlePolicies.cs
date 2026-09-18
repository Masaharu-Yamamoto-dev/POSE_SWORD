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
        // 陣営。MatchRules と同じ約束で、個人戦は「1人が1チーム」としてスロット番号をそのまま使う。
        // 陣営を省略した従来の呼び出しは全員が別チームになるので、味方判定は何も除外しない。
        public readonly int Team;
        public TargetCandidate(string id, int slot, float x, float y, bool alive)
            : this(id, slot, x, y, alive, slot) { }
        public TargetCandidate(string id, int slot, float x, float y, bool alive, int team)
        { Id = id; Slot = slot; X = x; Y = y; Alive = alive; Team = team; }
    }

    public static class BattlePolicies
    {
        // ボスの制圧を受けている間は、どの入力も通さない。
        private static bool CannotAct(float sp, bool started, bool alive, bool dashing, bool stunned)
        {
            return !started || !alive || dashing || stunned || float.IsNaN(sp) || float.IsInfinity(sp);
        }

        // ▼ 通常クリック/タップ用：SPが必殺技分たまっていてもここでは発動しない（ジャンプ・小ダッシュのみ）
        public static string PrimaryAction(bool koma, float sp, bool started, bool alive, bool dashing, bool right,
            bool stunned = false)
        {
            if (CannotAct(sp, started, alive, dashing, stunned)) return null;
            if (koma) return sp >= 20 ? "KomaDash" : null;
            return right ? "JumpRight" : "JumpLeft";
        }

        // ▼ 必殺技専用ボタン/スペースキー用：SPが必殺技分たまっている時だけ発動する
        // ultimateSp は剣モードの発動ラインで、既定の100は従来の固定値と同じ。ボスはSP最大が200になるが
        // 通常必殺技のラインは100のままなので、UIと同じ値をここへ渡して判定をひとつに揃える。
        public static string UltimateAction(bool koma, float sp, bool started, bool alive, bool dashing,
            bool stunned = false, float ultimateSp = 100f)
        {
            if (CannotAct(sp, started, alive, dashing, stunned)) return null;
            if (float.IsNaN(ultimateSp) || float.IsInfinity(ultimateSp)) return null;
            if (koma) return sp >= 70 ? "Tornado" : null;
            return sp >= ultimateSp ? "SwordDash" : null;
        }

        // ボスの制圧を撃てるか。ゲージ満タン(=maxSp)が条件で、通常必殺技の100とは別枠。
        // 制圧を受けている側は撃ち返せない。
        public static bool CanSuppress(float sp, float maxSp, bool started, bool alive, bool dashing,
            bool stunned, bool isBoss)
        {
            if (!isBoss || CannotAct(sp, started, alive, dashing, stunned)) return false;
            if (float.IsNaN(maxSp) || float.IsInfinity(maxSp) || maxSp <= 0) return false;
            return sp >= maxSp;
        }

        // ボスの制圧：発動した瞬間に半径内にいた敵だけを返す。判定は一度きりで、
        // 以降どちらが動いても対象は変わらない。距離は二乗のまま比べて平方根を避ける。
        public static string[] SuppressTargets(string self, float x, float y, float radius,
            IEnumerable<TargetCandidate> candidates)
        {
            if (candidates == null || radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                return new string[0];
            int selfTeam = TeamOf(self, candidates);
            double limit = (double)radius * radius;
            return candidates.Where(c => c.Alive && c.Id != self && c.Team != selfTeam &&
                    Math.Pow(c.X - x, 2) + Math.Pow(c.Y - y, 2) <= limit)
                .OrderBy(c => c.Slot).Select(c => c.Id).ToArray();
        }

        // 名簿に自分がいなければ、誰とも同じ陣営でない値を返す（＝味方判定で何も除外しない）。
        private static int TeamOf(string self, IEnumerable<TargetCandidate> candidates)
        {
            var me = candidates.FirstOrDefault(c => c.Id == self);
            return me == null ? int.MinValue : me.Team;
        }

        public static string Target(string self, float x, float y, IEnumerable<TargetCandidate> candidates,
            string currentId, bool locked)
        {
            int selfTeam = TeamOf(self, candidates);
            var targets = candidates.Where(c => c.Alive && c.Id != self && c.Team != selfTeam)
                .Select(c => new { Candidate = c, Distance = Math.Pow(c.X - x, 2) + Math.Pow(c.Y - y, 2) })
                .OrderBy(c => c.Distance).ThenBy(c => c.Candidate.Slot).ToArray();
            if (targets.Length == 0) return null;
            var current = targets.FirstOrDefault(t => t.Candidate.Id == currentId);
            if (current != null && (locked || targets[0].Distance >= current.Distance * 0.64)) return currentId;
            return targets[0].Candidate.Id;
        }
    }
}
