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

    // 1ステップで実際に通ったダメージ1件分（誰が・誰に・いくつ）。ダメージ数字の表示に使う。
    // Amount は対象の残りHPで頭打ちになった後の値なので、DamageDealt に足された値と一致する。
    public sealed class DamageRecord
    {
        public string AttackerId { get; private set; }
        public string TargetId { get; private set; }
        public int Amount { get; private set; }
        // この一撃で対象の剣が壊れたか（残機制で次の剣を待つことになった場合も含む）
        public bool Lethal { get; private set; }
        // 残りHPで頭打ちする前の、一撃そのものの威力。撃破の数字はこちらを見せる
        public int RawDamage { get; private set; }

        public DamageRecord(string attackerId, string targetId, int amount, bool lethal, int rawDamage)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Amount = amount;
            Lethal = lethal;
            RawDamage = rawDamage;
        }
    }

    public sealed class PlayerScore
    {
        private readonly int[] lives;
        public string PlayerId { get; private set; }
        public int SlotIndex { get; private set; }
        public int Hp { get; internal set; }
        // 試合に残っているか(脱落していないか)。残機制で次の剣を待っている間も true のままなので、
        // 順位・決着の判定はこれを見る
        public bool Alive { get { return Hp > 0 || Respawning; } }
        // 場に剣が出ていて、攻撃したり受けたりできるか。次の剣を待っている間は HP 0 なので false
        public bool OnField { get { return Hp > 0; } }
        // 残機制：剣が壊れてから次の剣が出るまでの間だけ true。RespawnTick のステップで次の剣のHPで場に戻る
        public bool Respawning { get { return RespawnTick >= 0; } }
        public int RespawnTick { get; private set; }
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
        // 陣営。個人戦では「1人が1チーム」とみなしてスロット番号がそのまま入るので、
        // 味方判定も決着判定も個人戦とチーム戦で同じ式のまま扱える。
        public int Team { get; private set; }

        internal PlayerScore(string id, int slotIndex, int[] lives, int team)
        {
            PlayerId = id;
            SlotIndex = slotIndex;
            this.lives = lives;
            MaxLives = lives.Length;
            LivesRemaining = lives.Length;
            CurrentLifeIndex = 0;
            Hp = lives[0];
            EliminationTick = -1;
            RespawnTick = -1;
            Team = team;
        }

        // 現在の剣のHPが0になった時に、まだ残機があれば脱落させず、respawnTick まで次の剣を待たせる。
        // 待っている間の CurrentLifeIndex は壊れた剣のまま(Unity側はこれが変わった時に次の剣を出す)
        internal void BreakSword(int respawnTick)
        {
            LivesRemaining--;
            RespawnTick = respawnTick;
        }

        // 待ち時間が明けたら、次の剣のHPで場に戻す
        internal void ReturnWithNextSword()
        {
            CurrentLifeIndex++;
            RespawnSeq++;
            RespawnTick = -1;
            Hp = lives[CurrentLifeIndex];
        }

        // 切断：残機の有無や待ち時間の途中かどうかに関わらず、その場で脱落させる
        internal void Forfeit()
        {
            Hp = 0;
            LivesRemaining = 0;
            RespawnTick = -1;
            EliminationReason = "DISCONNECTED";
        }
    }

    // Owns only authoritative health, attribution and ranking. Unity supplies one
    // batch AFTER each physics step; no collision callback may decide the winner.
    public sealed class MatchRules
    {
        // 1vs3のボスはHPに倍率が掛かるため、素の剣の上限(validateSword側の1000)より高い値が来る。
        // 上限を広げるだけなので、1000以下しか来ない既存の試合の挙動は変わらない。
        public const int MaxHitPoints = 4000;

        private readonly Dictionary<string, PlayerScore> byId;
        private readonly IReadOnlyList<PlayerScore> players;
        // チーム戦として構成されたか。個人戦(teams未指定)のときは順位付けに一切手を入れない。
        private readonly bool teamMatch;
        // 残機制で剣が壊れてから次の剣が出るまでのステップ数。0なら壊れたステップのうちに次の剣へ持ち替える
        private readonly int respawnDelayTicks;
        public IReadOnlyList<PlayerScore> Players { get { return players; } }
        public int LastTick { get; private set; }
        public bool Ended { get; private set; }
        public bool Draw { get; private set; }
        public string WinnerId { get; private set; }
        // 勝利した陣営。未決着と引き分けは -1。
        public int WinnerTeam { get; private set; }
        // 直前の ResolveStep で通ったダメージ。次の ResolveStep の呼び出しで作り直す。
        private readonly List<DamageRecord> lastStepDamage = new List<DamageRecord>();
        public IReadOnlyList<DamageRecord> LastStepDamage { get { return lastStepDamage; } }

        // 通常(残機モードなし)：各プレイヤーHP1つだけの1本勝負
        public MatchRules(string[] playerIds, int[] hitPoints)
            : this(playerIds, hitPoints == null ? null : hitPoints.Select(hp => new[] { hp }).ToArray(), null)
        {
        }

        // 残機モード：各プレイヤーが複数本の剣(lives[slot][0]が現在の剣、以降は持ち替え先)を持つ。
        // teams: 陣営を指定するとチーム戦になる(同じ番号同士は攻撃が通らず、決着も順位も陣営単位)。
        // 未指定なら従来どおりの個人戦で、1人が1チームとして扱われる。
        // respawnDelayTicks: 剣が壊れてから次の剣が場に出るまでのステップ数。待っている間は攻撃も被弾もしないが、
        // 脱落ではないので順位も決着も動かない。
        public MatchRules(string[] playerIds, int[][] lives, int[] teams = null, int respawnDelayTicks = 0)
        {
            if (playerIds == null || lives == null ||
                playerIds.Length < 2 || playerIds.Length > 4 || playerIds.Length != lives.Length ||
                playerIds.Any(string.IsNullOrWhiteSpace) || playerIds.Distinct().Count() != playerIds.Length ||
                lives.Any(l => l == null || l.Length < 1 || l.Any(hp => hp < 1 || hp > MaxHitPoints)))
                throw new ArgumentException("A match requires two to four distinct players with valid HP and lives.");
            // 全員が同じ陣営だと誰も倒せず決着が付かないので、2陣営以上を必須にする。
            if (teams != null && (teams.Length != playerIds.Length || teams.Any(t => t < 0) ||
                teams.Distinct().Count() < 2))
                throw new ArgumentException("A team match requires one team per player and at least two teams.");
            if (respawnDelayTicks < 0)
                throw new ArgumentException("The respawn delay cannot be negative.");

            teamMatch = teams != null;
            this.respawnDelayTicks = respawnDelayTicks;
            var roster = playerIds.Select((id, slot) =>
                new PlayerScore(id, slot, lives[slot], teamMatch ? teams[slot] : slot)).ToList();
            players = roster.AsReadOnly();
            byId = roster.ToDictionary(p => p.PlayerId);
            LastTick = -1;
            WinnerTeam = -1;
        }

        public PlayerScore Get(string id) { return byId[id]; }

        public void ResolveStep(int tick, IEnumerable<Hit> hits, IEnumerable<string> forfeits = null)
        {
            lastStepDamage.Clear();
            if (Ended || tick <= LastTick || tick < 0) return;
            if (hits == null) throw new ArgumentNullException("hits");

            // Snapshot eligibility before applying any damage: mutual lethal hits survive
            // callback reordering. A confirmed disconnection is not a damaging attack.
            // alive は試合に残っている人(次の剣を待っている人も含む)で、順位と切断に使う。
            // 攻撃できる・受けるのは場に剣が出ている人(onField)だけ。
            var alive = new HashSet<string>(players.Where(p => p.Alive).Select(p => p.PlayerId));
            var onField = new HashSet<string>(players.Where(p => p.OnField).Select(p => p.PlayerId));
            var disconnected = new HashSet<string>((forfeits ?? Enumerable.Empty<string>()).Where(alive.Contains));
            // 味方判定(最後の条件)はIDの存在が保証されてから評価する。onField.Contains が先に
            // 偽になるので、名簿に無いIDが Get() に渡ることはない。
            var attacks = hits.Where(h => h != null && h.Damage > 0 &&
                    h.AttackerId != null && h.TargetId != null && h.AttackerId != h.TargetId &&
                    onField.Contains(h.AttackerId) && onField.Contains(h.TargetId) &&
                    !disconnected.Contains(h.AttackerId) && !disconnected.Contains(h.TargetId) &&
                    Get(h.AttackerId).Team != Get(h.TargetId).Team)
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
                    Exact = (decimal)h.Damage * actual / total,
                    Raw = h.Damage
                }).ToList();
                foreach (var share in shares) share.Amount = (int)share.Exact;
                int remainder = actual - shares.Sum(s => s.Amount);
                foreach (var share in shares.OrderByDescending(s => s.Exact - s.Amount)
                    .ThenBy(s => s.Player.SlotIndex).Take(remainder)) share.Amount++;
                bool lethal = actual >= target.Hp;
                foreach (var share in shares)
                {
                    share.Player.DamageDealt += share.Amount;
                    if (share.Amount > 0) lastStepDamage.Add(new DamageRecord(share.Player.PlayerId, target.PlayerId, share.Amount, lethal, share.Raw));
                }

                target.Hp -= actual;
                target.DamageTaken += actual;
                if (!target.OnField)
                {
                    shares.OrderByDescending(s => s.Amount).ThenBy(s => s.Player.SlotIndex).First().Player.Kills++;
                    // 残機モード：まだ手持ちの剣が残っていれば、脱落させず次の剣を待たせる
                    if (target.LivesRemaining > 1) target.BreakSword(tick + respawnDelayTicks);
                    else
                    {
                        target.LivesRemaining = 0;
                        target.EliminationReason = "KO";
                    }
                }
            }

            // 切断は残機の有無に関わらず即座に脱落させる
            foreach (var id in disconnected) Get(id).Forfeit();

            // 待ち時間が明けた剣を場に戻す。このステップで壊れた剣も対象にするので、待ち時間0なら
            // 壊れたその場で次の剣に持ち替わる
            foreach (var player in players.Where(p => p.Respawning && tick >= p.RespawnTick)) player.ReturnWithNextSword();

            var survivors = players.Where(p => p.Alive).ToArray();
            foreach (var player in players.Where(p => alive.Contains(p.PlayerId) && !p.Alive))
            {
                player.Rank = survivors.Length + 1;
                player.EliminationTick = tick;
            }
            LastTick = tick;
            // 生存している陣営が1つになったら決着。個人戦は1人が1チームなので、
            // この式は「生存者が1人以下」と完全に同じ意味になる。
            var survivingTeams = survivors.Select(p => p.Team).Distinct().ToArray();
            if (survivingTeams.Length > 1) return;

            Ended = true;
            Draw = survivingTeams.Length == 0;
            if (Draw) return;

            WinnerTeam = survivingTeams[0];
            if (!teamMatch)
            {
                survivors[0].Rank = 1;
                WinnerId = survivors[0].PlayerId;
                return;
            }
            // チーム戦は陣営単位の勝敗。途中で撃破された味方も勝利チームなら1位にする。
            foreach (var player in players) player.Rank = player.Team == WinnerTeam ? 1 : 2;
            var winners = players.Where(p => p.Team == WinnerTeam).ToArray();
            // 勝者が複数いる陣営では代表を決められないので、WinnerId は空のままにする(WinnerTeam を見ること)。
            if (winners.Length == 1) WinnerId = winners[0].PlayerId;
        }

        private sealed class Share
        {
            internal PlayerScore Player;
            internal decimal Exact;
            internal int Amount;
            internal int Raw;
        }
    }
}
