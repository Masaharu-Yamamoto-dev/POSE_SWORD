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

        internal PlayerScore(string id, int slotIndex, int hp)
        {
            PlayerId = id;
            SlotIndex = slotIndex;
            Hp = hp;
            EliminationTick = -1;
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

        public MatchRules(string[] playerIds, int[] hitPoints)
        {
            if (playerIds == null || hitPoints == null ||
                playerIds.Length < 2 || playerIds.Length > 4 || playerIds.Length != hitPoints.Length ||
                playerIds.Any(string.IsNullOrWhiteSpace) || playerIds.Distinct().Count() != playerIds.Length ||
                hitPoints.Any(hp => hp < 1 || hp > 1000))
                throw new ArgumentException("A match requires two to four distinct players with valid HP.");

            var roster = playerIds.Select((id, slot) => new PlayerScore(id, slot, hitPoints[slot])).ToList();
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
                    target.EliminationReason = "KO";
                    shares.OrderByDescending(s => s.Amount).ThenBy(s => s.Player.SlotIndex).First().Player.Kills++;
                }
            }

            foreach (var id in disconnected)
            {
                Get(id).Hp = 0;
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
