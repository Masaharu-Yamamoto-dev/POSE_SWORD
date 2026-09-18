using System;
using System.Collections.Generic;
using System.Linq;

namespace PoseSword.Multiplayer
{
    public sealed class Hit
    {
        public string AttackerId { get; private set; }
        public string TargetId { get; private set; }
        public int Damage { get; private set; }

        public Hit(string attackerId, string targetId, int damage)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
        }
    }

    public sealed class PlayerScore
    {
        private readonly int[] lives;
        public string PlayerId { get; private set; }
        public int SlotIndex { get; private set; }
        public int Hp { get; internal set; }
        public bool Alive { get { return Hp > 0; } }
        public int Rank { get; internal set; }
        public int DamageDealt { get; internal set; }
        public int DamageTaken { get; internal set; }
        public int Kills { get; internal set; }
        public int EliminationTick { get; internal set; }
        public string EliminationReason { get; internal set; }
        // 残機モード：手持ちの剣の総数、残り本数(現在の剣を含む)、現在使用中の剣のインデックス。
        // RespawnSeqは持ち替え(次の剣に切り替わった)たびに増える単調増加カウンタで、
        // Unity側が「今tickで持ち替えが起きたか」を検知する目印として使う。
        public int MaxLives { get; private set; }
        public int LivesRemaining { get; internal set; }
        public int CurrentLifeIndex { get; internal set; }
        public int RespawnSeq { get; internal set; }

        internal PlayerScore(string id, int slotIndex, int[] lives)
        {
            PlayerId = id;
            SlotIndex = slotIndex;
            this.lives = lives;
            MaxLives = lives.Length;
            LivesRemaining = lives.Length;
            CurrentLifeIndex = 0;
            Hp = lives[0];
            EliminationTick = -1;
        }

        // 現在の剣のHPが0になった時に、まだ残機があれば次の剣のHPへ切り替える
        internal int ConsumeNextLife()
        {
            LivesRemaining--;
            CurrentLifeIndex++;
            RespawnSeq++;
            return lives[CurrentLifeIndex];
        }
    }

    // Owns only authoritative health, attribution and ranking. Unity supplies one
    // batch AFTER each physics step; no collision callback may decide the winner.
    public sealed class MatchRules
    {
        private readonly Dictionary<string, PlayerScore> byId;
        private readonly IReadOnlyList<PlayerScore> players;
        public IReadOnlyList<PlayerScore> Players { get { return players; } }
        public int LastTick { get; private set; }
        public bool Ended { get; private set; }
        public bool Draw { get; private set; }
        public string WinnerId { get; private set; }

        // 通常(残機モードなし)：各プレイヤーHP1つだけの1本勝負
        public MatchRules(string[] playerIds, int[] hitPoints)
            : this(playerIds, hitPoints == null ? null : hitPoints.Select(hp => new[] { hp }).ToArray())
        {
        }

        // 残機モード：各プレイヤーが複数本の剣(lives[slot][0]が現在の剣、以降は持ち替え先)を持つ
        public MatchRules(string[] playerIds, int[][] lives)
        {
            if (playerIds == null || lives == null ||
                playerIds.Length < 2 || playerIds.Length > 4 || playerIds.Length != lives.Length ||
                playerIds.Any(string.IsNullOrWhiteSpace) || playerIds.Distinct().Count() != playerIds.Length ||
                lives.Any(l => l == null || l.Length < 1 || l.Any(hp => hp < 1 || hp > 1000)))
                throw new ArgumentException("A match requires two to four distinct players with valid HP and lives.");

            var roster = playerIds.Select((id, slot) => new PlayerScore(id, slot, lives[slot])).ToList();
            players = roster.AsReadOnly();
            byId = roster.ToDictionary(p => p.PlayerId);
            LastTick = -1;
        }

        public PlayerScore Get(string id) { return byId[id]; }

        public void ResolveStep(int tick, IEnumerable<Hit> hits, IEnumerable<string> forfeits = null)
        {
            if (Ended || tick <= LastTick || tick < 0) return;
            if (hits == null) throw new ArgumentNullException("hits");

            // Snapshot eligibility before applying any damage: mutual lethal hits survive
            // callback reordering. A confirmed disconnection is not a damaging attack.
            var alive = new HashSet<string>(players.Where(p => p.Alive).Select(p => p.PlayerId));
            var disconnected = new HashSet<string>((forfeits ?? Enumerable.Empty<string>()).Where(alive.Contains));
            var attacks = hits.Where(h => h != null && h.Damage > 0 &&
                    h.AttackerId != null && h.TargetId != null && h.AttackerId != h.TargetId &&
                    alive.Contains(h.AttackerId) && alive.Contains(h.TargetId) &&
                    !disconnected.Contains(h.AttackerId) && !disconnected.Contains(h.TargetId))
                .GroupBy(h => new { h.AttackerId, h.TargetId })
                .Select(g => new Hit(g.Key.AttackerId, g.Key.TargetId, g.Max(h => h.Damage)))
                .ToArray();

            foreach (var targetGroup in attacks.GroupBy(h => h.TargetId))
            {
                var target = Get(targetGroup.Key);
                long total = targetGroup.Sum(h => (long)h.Damage);
                int actual = (int)Math.Min(target.Hp, total);
                var shares = targetGroup.Select(h => new Share {
                    Player = Get(h.AttackerId),
                    Exact = (decimal)h.Damage * actual / total
                }).ToList();
                foreach (var share in shares) share.Amount = (int)share.Exact;
                int remainder = actual - shares.Sum(s => s.Amount);
                foreach (var share in shares.OrderByDescending(s => s.Exact - s.Amount)
                    .ThenBy(s => s.Player.SlotIndex).Take(remainder)) share.Amount++;
                foreach (var share in shares) share.Player.DamageDealt += share.Amount;

                target.Hp -= actual;
                target.DamageTaken += actual;
                if (!target.Alive)
                {
                    shares.OrderByDescending(s => s.Amount).ThenBy(s => s.Player.SlotIndex).First().Player.Kills++;
                    // 残機モード：まだ手持ちの剣が残っていれば、脱落させず次の剣のHPへ持ち替えて続行する
                    if (target.LivesRemaining > 1) target.Hp = target.ConsumeNextLife();
                    else
                    {
                        target.LivesRemaining = 0;
                        target.EliminationReason = "KO";
                    }
                }
            }

            foreach (var id in disconnected)
            {
                // 切断は残機の有無に関わらず即座に脱落させる
                Get(id).Hp = 0;
                Get(id).LivesRemaining = 0;
                Get(id).EliminationReason = "DISCONNECTED";
            }

            var survivors = players.Where(p => p.Alive).ToArray();
            foreach (var player in players.Where(p => alive.Contains(p.PlayerId) && !p.Alive))
            {
                player.Rank = survivors.Length + 1;
                player.EliminationTick = tick;
            }
            LastTick = tick;
            if (survivors.Length > 1) return;

            Ended = true;
            Draw = survivors.Length == 0;
            if (!Draw)
            {
                survivors[0].Rank = 1;
                WinnerId = survivors[0].PlayerId;
            }
        }

        private sealed class Share
        {
            internal PlayerScore Player;
            internal decimal Exact;
            internal int Amount;
        }
    }
}
