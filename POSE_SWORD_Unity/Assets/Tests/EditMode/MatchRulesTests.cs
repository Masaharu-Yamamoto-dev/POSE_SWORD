using System.Linq;
using NUnit.Framework;
using PoseSword.Multiplayer;

public class MatchRulesTests
{
    [Test]
    public void PrimaryActionIsChosenByHostSpAndRoundState()
    {
        Assert.IsNull(BattlePolicies.PrimaryAction(true, 100, false, true, false, true));
        Assert.IsNull(BattlePolicies.PrimaryAction(true, 100, true, false, false, true));
        Assert.IsNull(BattlePolicies.PrimaryAction(true, 100, true, true, true, true));
        Assert.IsNull(BattlePolicies.PrimaryAction(true, 19, true, true, false, true));
        Assert.AreEqual("KomaDash", BattlePolicies.PrimaryAction(true, 20, true, true, false, true));
        // 通常入力(クリック/タップ)では、SPが必殺技分たまっていても剣モードは常にジャンプのみ
        Assert.AreEqual("JumpRight", BattlePolicies.PrimaryAction(false, 100, true, true, false, true));
        Assert.AreEqual("JumpLeft", BattlePolicies.PrimaryAction(false, 99, true, true, false, false));
    }

    [Test]
    public void UltimateActionRequiresSpThresholdAndRoundState()
    {
        Assert.IsNull(BattlePolicies.UltimateAction(true, 100, false, true, false));
        Assert.IsNull(BattlePolicies.UltimateAction(true, 100, true, false, false));
        Assert.IsNull(BattlePolicies.UltimateAction(true, 100, true, true, true));
        Assert.IsNull(BattlePolicies.UltimateAction(true, 69, true, true, false));
        Assert.AreEqual("Tornado", BattlePolicies.UltimateAction(true, 70, true, true, false));
        Assert.IsNull(BattlePolicies.UltimateAction(false, 99, true, true, false));
        Assert.AreEqual("SwordDash", BattlePolicies.UltimateAction(false, 100, true, true, false));
    }

    [Test]
    public void TargetSelectionUsesAliveEnemiesAndAvoidsOscillation()
    {
        var candidates = new[] { new TargetCandidate("self", 0, 0, 0, true),
            new TargetCandidate("a", 1, 10, 0, true), new TargetCandidate("b", 2, 9, 0, true) };
        Assert.AreEqual("b", BattlePolicies.Target("self", 0, 0, candidates, null, false));
        Assert.AreEqual("a", BattlePolicies.Target("self", 0, 0, candidates, "a", false));
        candidates[2] = new TargetCandidate("b", 2, 7, 0, true);
        Assert.AreEqual("b", BattlePolicies.Target("self", 0, 0, candidates, "a", false));
        Assert.AreEqual("a", BattlePolicies.Target("self", 0, 0, candidates, "a", true));
        candidates[1] = new TargetCandidate("a", 1, 10, 0, false);
        Assert.AreEqual("b", BattlePolicies.Target("self", 0, 0, candidates, "a", true));
        candidates[2] = new TargetCandidate("b", 2, 7, 0, false);
        Assert.IsNull(BattlePolicies.Target("self", 0, 0, candidates, "a", false));
    }

    private static MatchRules Four() => new MatchRules(new[] { "p0", "p1", "p2", "p3" }, new[] { 100, 100, 100, 100 });

    [Test]
    public void FirstEliminationDoesNotEndFourPlayerMatch()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p0", "p1", 100) });
        Assert.IsFalse(match.Ended);
        Assert.AreEqual(3, match.Players.Count(p => p.Alive));
        Assert.AreEqual(4, match.Get("p1").Rank);
    }

    [Test]
    public void HostEliminationDoesNotEndSimulation()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        match.ResolveStep(2, new[] { new Hit("p1", "p2", 100) });
        match.ResolveStep(3, new[] { new Hit("p1", "p3", 100) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual("p1", match.WinnerId);
        Assert.AreEqual(3, match.Get("p1").Kills);
    }

    [Test]
    public void SimultaneousEliminationsShareRankAndMutualFinalHitIsDraw()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p0", "p2", 100), new Hit("p1", "p3", 100) });
        Assert.AreEqual(3, match.Get("p2").Rank);
        Assert.AreEqual(3, match.Get("p3").Rank);
        match.ResolveStep(2, new[] { new Hit("p0", "p1", 100), new Hit("p1", "p0", 100) });
        Assert.IsTrue(match.Draw);
        Assert.IsNull(match.WinnerId);
        Assert.AreEqual(1, match.Get("p0").Rank);
        Assert.AreEqual(1, match.Get("p1").Rank);
    }

    [Test]
    public void DamageAttributionIsOrderIndependentAndExcludesOverkill()
    {
        var hits = new[] { new Hit("p1", "p3", 80), new Hit("p0", "p3", 80), new Hit("p2", "p3", 40) };
        var a = Four();
        var b = Four();
        a.ResolveStep(1, hits);
        b.ResolveStep(1, hits.Reverse());
        CollectionAssert.AreEqual(a.Players.Select(p => p.DamageDealt), b.Players.Select(p => p.DamageDealt));
        Assert.AreEqual(100, a.Players.Sum(p => p.DamageDealt));
        Assert.AreEqual(40, a.Get("p0").DamageDealt);
        Assert.AreEqual(1, a.Get("p0").Kills);
        Assert.AreEqual(100, a.Get("p3").DamageTaken);
    }

    [Test]
    public void DuplicateColliderHitsAreMergedButDifferentAttackersAreNot()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p0", "p3", 20), new Hit("p0", "p3", 30), new Hit("p1", "p3", 10) });
        Assert.AreEqual(60, match.Get("p3").Hp);
        Assert.AreEqual(30, match.Get("p0").DamageDealt);
    }

    [Test]
    public void ForfeitAwardsNoKillAndDeadPlayersCannotDealDamage()
    {
        var match = Four();
        match.ResolveStep(1, new Hit[0], new[] { "p0" });
        match.ResolveStep(2, new[] { new Hit("p0", "p1", 100) });
        Assert.AreEqual(100, match.Get("p1").Hp);
        Assert.AreEqual("DISCONNECTED", match.Get("p0").EliminationReason);
        Assert.AreEqual(0, match.Players.Sum(p => p.Kills));
    }

    [Test]
    public void OldStepsAndHitsAfterResultCannotChangeResults()
    {
        var match = new MatchRules(new[] { "a", "b" }, new[] { 100, 100 });
        match.ResolveStep(1, new[] { new Hit("a", "b", 20) });
        match.ResolveStep(1, new[] { new Hit("a", "b", 20) });
        Assert.AreEqual(80, match.Get("b").Hp);
        match.ResolveStep(2, new[] { new Hit("a", "b", 100) });
        match.ResolveStep(3, new[] { new Hit("b", "a", 100) });
        Assert.AreEqual("a", match.WinnerId);
        Assert.AreEqual(100, match.Get("a").Hp);
    }

    [Test]
    public void InvalidRosterIsRejected()
    {
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "a" }, new[] { 100, 100 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a" }, new[] { 100 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b", "c", "d", "e" }, new[] { 100, 100, 100, 100, 100 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, new[] { 0, 100 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, new[] { 100 }));
    }

    [Test]
    public void ThreePlayersRankBySurvivalLikeFour()
    {
        var match = new MatchRules(new[] { "a", "b", "c" }, new[] { 100, 100, 100 });
        match.ResolveStep(1, new[] { new Hit("a", "b", 100) });
        Assert.IsFalse(match.Ended);
        Assert.AreEqual(3, match.Get("b").Rank);
        match.ResolveStep(2, new[] { new Hit("c", "a", 100) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual("c", match.WinnerId);
        Assert.AreEqual(2, match.Get("a").Rank);
        Assert.AreEqual(1, match.Get("c").Rank);
    }

    [Test]
    public void InvalidHitsAreIgnoredAndDamageTotalsDoNotOverflow()
    {
        var match = Four();
        match.ResolveStep(1, new[] { null, new Hit("unknown", "p0", 100), new Hit("p0", "p0", 100),
            new Hit("p1", "p0", -1), new Hit("p0", "p3", int.MaxValue), new Hit("p1", "p3", int.MaxValue) });
        Assert.AreEqual(100, match.Get("p0").Hp);
        Assert.AreEqual(50, match.Get("p0").DamageDealt);
        Assert.AreEqual(50, match.Get("p1").DamageDealt);
    }

    [Test]
    public void LosingALifeDoesNotEliminateAndSwapsToNextSwordHp()
    {
        var match = new MatchRules(new[] { "a", "b" }, new int[][] { new[] { 100, 80, 50 }, new[] { 100 } });
        match.ResolveStep(1, new[] { new Hit("b", "a", 100) });
        Assert.IsFalse(match.Ended);
        Assert.IsTrue(match.Get("a").Alive);
        Assert.AreEqual(80, match.Get("a").Hp);
        Assert.AreEqual(2, match.Get("a").LivesRemaining);
        Assert.AreEqual(1, match.Get("a").CurrentLifeIndex);
        Assert.AreEqual(1, match.Get("a").RespawnSeq);
        Assert.AreEqual(1, match.Get("b").Kills);
        Assert.IsNull(match.Get("a").EliminationReason);
    }

    [Test]
    public void MatchEndsOnlyAfterAllLivesAreExhausted()
    {
        var match = new MatchRules(new[] { "a", "b" }, new int[][] { new[] { 100, 50 }, new[] { 100 } });
        match.ResolveStep(1, new[] { new Hit("b", "a", 100) });
        Assert.IsFalse(match.Ended);
        Assert.AreEqual(50, match.Get("a").Hp);
        match.ResolveStep(2, new[] { new Hit("b", "a", 50) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual("b", match.WinnerId);
        Assert.AreEqual(0, match.Get("a").LivesRemaining);
        Assert.AreEqual("KO", match.Get("a").EliminationReason);
        Assert.AreEqual(2, match.Get("b").Kills);
    }

    [Test]
    public void DisconnectionEliminatesEvenWithLivesRemaining()
    {
        var match = new MatchRules(new[] { "a", "b" }, new int[][] { new[] { 100, 100, 100 }, new[] { 100 } });
        match.ResolveStep(1, new Hit[0], new[] { "a" });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual("b", match.WinnerId);
        Assert.AreEqual(0, match.Get("a").LivesRemaining);
        Assert.AreEqual("DISCONNECTED", match.Get("a").EliminationReason);
    }

    [Test]
    public void LivesRosterRejectsEmptyLifeList()
    {
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, new int[][] { new int[0], new[] { 100 } }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, new int[][] { new[] { 100 }, null }));
    }

    // ===== 残機制：剣が壊れてから次の剣が出るまでの待ち時間 =====

    private static MatchRules WithRespawnDelay(int delay, params int[][] lives) =>
        new MatchRules(lives.Select((_, i) => "p" + i).ToArray(), lives, null, delay);

    [Test]
    public void BrokenSwordWaitsBeforeTheNextSwordComesOut()
    {
        var match = WithRespawnDelay(3, new[] { 100, 80 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        var p0 = match.Get("p0");
        // 壊れた直後：試合には残っているが、場には剣が無い。HPは0で、まだ壊れた剣の番号のまま
        Assert.IsTrue(p0.Alive);
        Assert.IsFalse(p0.OnField);
        Assert.IsTrue(p0.Respawning);
        Assert.AreEqual(0, p0.Hp);
        Assert.AreEqual(0, p0.CurrentLifeIndex);
        Assert.AreEqual(1, p0.LivesRemaining);
        Assert.AreEqual(0, p0.RespawnSeq);
        Assert.AreEqual(1, match.Get("p1").Kills);
        Assert.IsFalse(match.Ended);
        Assert.AreEqual(0, p0.Rank);
        Assert.AreEqual(-1, p0.EliminationTick);
        Assert.IsNull(p0.EliminationReason);

        match.ResolveStep(2, new Hit[0]);
        match.ResolveStep(3, new Hit[0]);
        Assert.IsTrue(p0.Respawning);
        Assert.AreEqual(0, p0.Hp);

        // 壊れたステップ(1)から3ステップ後に、次の剣のHPで場に戻る
        match.ResolveStep(4, new Hit[0]);
        Assert.IsFalse(p0.Respawning);
        Assert.IsTrue(p0.OnField);
        Assert.AreEqual(80, p0.Hp);
        Assert.AreEqual(1, p0.CurrentLifeIndex);
        Assert.AreEqual(1, p0.RespawnSeq);
        Assert.IsFalse(match.Ended);
    }

    [Test]
    public void ZeroDelaySwapsInTheSameStepAsBefore()
    {
        var match = WithRespawnDelay(0, new[] { 100, 80 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        Assert.IsFalse(match.Get("p0").Respawning);
        Assert.AreEqual(80, match.Get("p0").Hp);
        Assert.AreEqual(1, match.Get("p0").CurrentLifeIndex);
    }

    [Test]
    public void RespawnIsNotMissedWhenStepsAreSkipped()
    {
        var match = WithRespawnDelay(3, new[] { 100, 80 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        match.ResolveStep(10, new Hit[0]);
        Assert.AreEqual(80, match.Get("p0").Hp);
        Assert.IsTrue(match.Get("p0").OnField);
    }

    [Test]
    public void WaitingPlayerCanNeitherDealNorTakeDamage()
    {
        var match = WithRespawnDelay(5, new[] { 100, 80 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        match.ResolveStep(2, new[] { new Hit("p1", "p0", 50), new Hit("p0", "p1", 50) });
        Assert.AreEqual(0, match.LastStepDamage.Count);
        Assert.AreEqual(100, match.Get("p1").Hp);
        Assert.AreEqual(100, match.Get("p0").DamageTaken);
        Assert.AreEqual(0, match.Get("p0").DamageDealt);
        Assert.AreEqual(1, match.Get("p1").Kills);
        Assert.IsTrue(match.Get("p0").Respawning);

        // 戻ってきた後は普通に通る
        match.ResolveStep(6, new Hit[0]);
        match.ResolveStep(7, new[] { new Hit("p1", "p0", 30) });
        Assert.AreEqual(50, match.Get("p0").Hp);
    }

    [Test]
    public void WaitingPlayerStillCountsAsASurvivorForRanksAndTheResult()
    {
        var match = WithRespawnDelay(5, new[] { 100, 100 }, new[] { 100 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        // p0 が次の剣を待っている間に p2 が倒れても、p0 は生き残りとして数える(残り2人なので p2 は3位)
        match.ResolveStep(2, new[] { new Hit("p1", "p2", 100) });
        Assert.IsTrue(match.Get("p0").Respawning);
        Assert.IsFalse(match.Ended);
        Assert.AreEqual(3, match.Get("p2").Rank);
        Assert.AreEqual(0, match.Get("p0").Rank);

        match.ResolveStep(6, new Hit[0]);
        match.ResolveStep(7, new[] { new Hit("p1", "p0", 100) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual("p1", match.WinnerId);
        Assert.AreEqual(2, match.Get("p0").Rank);
        Assert.AreEqual("KO", match.Get("p0").EliminationReason);
        Assert.AreEqual(7, match.Get("p0").EliminationTick);
    }

    [Test]
    public void MutualFinalHitWinsForThePlayerWhoStillHasASword()
    {
        // 相打ちでも、次の剣が残っている側は待ちに入るだけなので、最後の1本だった側が負ける
        var match = WithRespawnDelay(5, new[] { 100, 100 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p0", "p1", 100), new Hit("p1", "p0", 100) });
        Assert.IsTrue(match.Ended);
        Assert.IsFalse(match.Draw);
        Assert.AreEqual("p0", match.WinnerId);
        Assert.AreEqual(1, match.Get("p0").Rank);
        Assert.AreEqual(2, match.Get("p1").Rank);
        Assert.IsTrue(match.Get("p0").Alive);
        Assert.IsTrue(match.Get("p0").Respawning);
        Assert.IsFalse(match.Get("p1").Alive);
    }

    [Test]
    public void BothWaitingAtOnceDoesNotEndTheMatch()
    {
        var match = WithRespawnDelay(5, new[] { 100, 100 }, new[] { 100, 100 });
        match.ResolveStep(1, new[] { new Hit("p0", "p1", 100), new Hit("p1", "p0", 100) });
        Assert.IsFalse(match.Ended);
        Assert.IsTrue(match.Players.All(p => p.Respawning));
        match.ResolveStep(6, new Hit[0]);
        Assert.IsTrue(match.Players.All(p => p.OnField && p.Hp == 100 && p.CurrentLifeIndex == 1));
    }

    [Test]
    public void DisconnectionWhileWaitingEliminatesAndNeverRespawns()
    {
        var match = WithRespawnDelay(5, new[] { 100, 100 }, new[] { 100 }, new[] { 100 });
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        match.ResolveStep(2, new Hit[0], new[] { "p0" });
        var p0 = match.Get("p0");
        Assert.IsFalse(p0.Alive);
        Assert.IsFalse(p0.Respawning);
        Assert.AreEqual("DISCONNECTED", p0.EliminationReason);
        Assert.AreEqual(0, p0.LivesRemaining);
        Assert.AreEqual(3, p0.Rank);
        match.ResolveStep(10, new Hit[0]);
        Assert.AreEqual(0, p0.Hp);
        Assert.AreEqual(0, p0.CurrentLifeIndex);
    }

    [Test]
    public void TeamIsStillStandingWhileItsLastMemberWaits()
    {
        var lives = new int[][] { new[] { 100 }, new[] { 100, 100 }, new[] { 100 }, new[] { 100 } };
        var match = new MatchRules(new[] { "boss", "t1", "t2", "t3" }, lives, new[] { 0, 1, 1, 1 }, 5);
        match.ResolveStep(1, new[] { new Hit("boss", "t1", 100) });
        match.ResolveStep(2, new[] { new Hit("boss", "t2", 100) });
        match.ResolveStep(3, new[] { new Hit("boss", "t3", 100) });
        // トリオで場に残っている剣は無いが、t1 が次の剣を待っているので決着しない
        Assert.IsTrue(match.Get("t1").Respawning);
        Assert.IsFalse(match.Ended);
        match.ResolveStep(6, new Hit[0]);
        Assert.IsTrue(match.Get("t1").OnField);
        match.ResolveStep(7, new[] { new Hit("t1", "boss", 100) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual(1, match.WinnerTeam);
    }

    [Test]
    public void NegativeRespawnDelayIsRejected()
    {
        Assert.Throws<System.ArgumentException>(() => WithRespawnDelay(-1, new[] { 100 }, new[] { 100 }));
    }

    // ===== 1vs3：入力とターゲットの方針 =====

    [Test]
    public void OmittedTeamMakesEveryCandidateTheirOwnTeam()
    {
        var c = new TargetCandidate("a", 2, 0, 0, true);
        Assert.AreEqual(2, c.Team);   // 陣営を省略したらスロット番号がそのまま入る
        Assert.AreEqual(9, new TargetCandidate("b", 2, 0, 0, true, 9).Team);
    }

    [Test]
    public void TargetSelectionSkipsTeammates()
    {
        // boss=team0 / t1,t2,t3=team1。トリオから見た敵はボスだけになる。
        var candidates = new[] {
            new TargetCandidate("boss", 0, 0, 0, true, 0),
            new TargetCandidate("t1", 1, 1, 0, true, 1),
            new TargetCandidate("t2", 2, 2, 0, true, 1),
            new TargetCandidate("t3", 3, 3, 0, true, 1) };
        // t1 のすぐ隣に味方 t2 がいても、狙うのは離れたボス
        Assert.AreEqual("boss", BattlePolicies.Target("t1", 1, 0, candidates, null, false));
        Assert.AreEqual("boss", BattlePolicies.Target("t3", 3, 0, candidates, null, false));
        // ボスから見ると3人とも敵。いちばん近い相手を狙う
        Assert.AreEqual("t1", BattlePolicies.Target("boss", 0, 0, candidates, null, false));
    }

    [Test]
    public void TargetSelectionReturnsNullWhenOnlyTeammatesRemain()
    {
        var candidates = new[] {
            new TargetCandidate("boss", 0, 0, 0, false, 0),   // ボスは撃破済み
            new TargetCandidate("t1", 1, 1, 0, true, 1),
            new TargetCandidate("t2", 2, 2, 0, true, 1) };
        Assert.IsNull(BattlePolicies.Target("t1", 1, 0, candidates, null, false));
    }

    [Test]
    public void SuppressedPlayersCannotActAtAll()
    {
        Assert.IsNull(BattlePolicies.PrimaryAction(false, 100, true, true, false, true, true));
        Assert.IsNull(BattlePolicies.PrimaryAction(true, 100, true, true, false, true, true));
        Assert.IsNull(BattlePolicies.UltimateAction(false, 100, true, true, false, true));
        Assert.IsNull(BattlePolicies.UltimateAction(true, 100, true, true, false, true));
        // 制圧されていなければ従来どおり通る
        Assert.AreEqual("JumpRight", BattlePolicies.PrimaryAction(false, 100, true, true, false, true, false));
        Assert.AreEqual("SwordDash", BattlePolicies.UltimateAction(false, 100, true, true, false, false));
    }

    [Test]
    public void UltimateThresholdComesFromTheCallerSoTheBossKeepsHundred()
    {
        // ボスはSP最大200でも、通常必殺技のラインは100のまま
        Assert.AreEqual("SwordDash", BattlePolicies.UltimateAction(false, 100, true, true, false, false, 100f));
        Assert.AreEqual("SwordDash", BattlePolicies.UltimateAction(false, 200, true, true, false, false, 100f));
        // 閾値を上げた場合はその値で判定する
        Assert.IsNull(BattlePolicies.UltimateAction(false, 199, true, true, false, false, 200f));
        Assert.AreEqual("SwordDash", BattlePolicies.UltimateAction(false, 200, true, true, false, false, 200f));
        // 独楽モードは閾値70のまま影響を受けない
        Assert.AreEqual("Tornado", BattlePolicies.UltimateAction(true, 70, true, true, false, false, 200f));
        Assert.IsNull(BattlePolicies.UltimateAction(false, 100, true, true, false, false, float.NaN));
    }

    [Test]
    public void OnlyTheBossCanSuppressAndOnlyOnAFullGauge()
    {
        // ゲージ満タン(200)でのみ発動。通常必殺技の100では撃てない
        Assert.IsTrue(BattlePolicies.CanSuppress(200, 200, true, true, false, false, true));
        Assert.IsFalse(BattlePolicies.CanSuppress(199, 200, true, true, false, false, true));
        // トリオ側は持っていない能力
        Assert.IsFalse(BattlePolicies.CanSuppress(200, 200, true, true, false, false, false));
        // 開始前・撃破後・突進中・制圧を受けている間は撃てない
        Assert.IsFalse(BattlePolicies.CanSuppress(200, 200, false, true, false, false, true));
        Assert.IsFalse(BattlePolicies.CanSuppress(200, 200, true, false, false, false, true));
        Assert.IsFalse(BattlePolicies.CanSuppress(200, 200, true, true, true, false, true));
        Assert.IsFalse(BattlePolicies.CanSuppress(200, 200, true, true, false, true, true));
        // 壊れた値は発動させない
        Assert.IsFalse(BattlePolicies.CanSuppress(float.NaN, 200, true, true, false, false, true));
        Assert.IsFalse(BattlePolicies.CanSuppress(200, 0, true, true, false, false, true));
    }

    private static TargetCandidate[] SuppressField() => new[] {
        new TargetCandidate("boss", 0, 0, 0, true, 0),
        new TargetCandidate("t1", 1, 3, 4, true, 1),     // ボスから距離5
        new TargetCandidate("t2", 2, 20, 0, true, 1),    // 距離20（範囲外）
        new TargetCandidate("t3", 3, 0, 5, true, 1) };   // 距離5

    [Test]
    public void SuppressTargetsCatchesOnlyEnemiesInsideTheRadius()
    {
        var caught = BattlePolicies.SuppressTargets("boss", 0, 0, 8f, SuppressField());
        CollectionAssert.AreEqual(new[] { "t1", "t3" }, caught);   // t2 は範囲外
    }

    [Test]
    public void SuppressTargetsExcludesSelfTeammatesAndTheDead()
    {
        var field = new[] {
            new TargetCandidate("boss", 0, 0, 0, true, 0),
            new TargetCandidate("ally", 1, 1, 0, true, 0),    // 同じ陣営
            new TargetCandidate("dead", 2, 1, 1, false, 1),   // 撃破済み
            new TargetCandidate("live", 3, 2, 0, true, 1) };
        CollectionAssert.AreEqual(new[] { "live" }, BattlePolicies.SuppressTargets("boss", 0, 0, 8f, field));
    }

    [Test]
    public void SuppressTargetsIncludesTheBoundaryAndRejectsAnEmptyRadius()
    {
        // ちょうど半径上（距離5）は含む
        CollectionAssert.AreEqual(new[] { "t1", "t3" }, BattlePolicies.SuppressTargets("boss", 0, 0, 5f, SuppressField()));
        // わずかに足りなければ含まない
        CollectionAssert.IsEmpty(BattlePolicies.SuppressTargets("boss", 0, 0, 4.9f, SuppressField()));
        CollectionAssert.IsEmpty(BattlePolicies.SuppressTargets("boss", 0, 0, 0f, SuppressField()));
        CollectionAssert.IsEmpty(BattlePolicies.SuppressTargets("boss", 0, 0, float.NaN, SuppressField()));
        CollectionAssert.IsEmpty(BattlePolicies.SuppressTargets("boss", 0, 0, 8f, null));
    }

    [Test]
    public void SuppressTargetsUsesTheGivenCentreNotTheCandidatePosition()
    {
        // ボスが名簿の座標から離れた位置で撃った場合も、渡した中心で判定する
        var caught = BattlePolicies.SuppressTargets("boss", 20, 0, 3f, SuppressField());
        CollectionAssert.AreEqual(new[] { "t2" }, caught);
    }

    // ===== 1vs3（ボス vs 三人組） =====
    // boss は team 0 で HP300(強化後)、t1〜t3 は team 1 で HP100。
    private static MatchRules Boss() => new MatchRules(new[] { "boss", "t1", "t2", "t3" },
        new int[][] { new[] { 300 }, new[] { 100 }, new[] { 100 }, new[] { 100 } }, new[] { 0, 1, 1, 1 });

    [Test]
    public void TeammatesCannotDamageEachOther()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("t1", "t2", 100), new Hit("t2", "t1", 60) });
        Assert.AreEqual(100, match.Get("t1").Hp);
        Assert.AreEqual(100, match.Get("t2").Hp);
        Assert.AreEqual(0, match.Players.Sum(p => p.DamageDealt));
        Assert.AreEqual(0, match.Players.Sum(p => p.DamageTaken));
        Assert.AreEqual(0, match.Players.Sum(p => p.Kills));
        Assert.IsFalse(match.Ended);
    }

    [Test]
    public void TrioMembersStillDamageTheBossWhileIgnoringEachOther()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("t1", "boss", 50), new Hit("t2", "t3", 100), new Hit("t3", "boss", 50) });
        Assert.AreEqual(200, match.Get("boss").Hp);
        Assert.AreEqual(100, match.Get("t3").Hp);
        Assert.AreEqual(50, match.Get("t1").DamageDealt);
        Assert.AreEqual(0, match.Get("t2").DamageDealt);
    }

    [Test]
    public void BossWinsOnlyAfterEveryTrioMemberFalls()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("boss", "t1", 100) });
        Assert.IsFalse(match.Ended);
        match.ResolveStep(2, new[] { new Hit("boss", "t2", 100) });
        Assert.IsFalse(match.Ended);
        match.ResolveStep(3, new[] { new Hit("boss", "t3", 100) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual(0, match.WinnerTeam);
        Assert.AreEqual("boss", match.WinnerId);
        Assert.AreEqual(3, match.Get("boss").Kills);
    }

    [Test]
    public void TrioWinsWhenTheBossFallsEvenWithMembersLost()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("boss", "t1", 100) });
        match.ResolveStep(2, new[] { new Hit("t2", "boss", 300) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual(1, match.WinnerTeam);
        // 勝者が3人いる陣営なので代表は決められない。WinnerTeam を見ること。
        Assert.IsNull(match.WinnerId);
        Assert.IsFalse(match.Draw);
    }

    [Test]
    public void TeamVictoryPutsEveryWinnerFirstAndEveryLoserSecond()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("boss", "t1", 100) });
        Assert.AreEqual(4, match.Get("t1").Rank);   // 決着前は脱落順のまま
        match.ResolveStep(2, new[] { new Hit("t2", "boss", 300) });
        Assert.AreEqual(1, match.Get("t1").Rank);   // 途中で落ちた味方も勝利チームなら1位
        Assert.AreEqual(1, match.Get("t2").Rank);
        Assert.AreEqual(1, match.Get("t3").Rank);
        Assert.AreEqual(2, match.Get("boss").Rank);
        Assert.AreEqual(1, match.Get("t1").EliminationTick);
    }

    [Test]
    public void MutualTeamWipeIsADraw()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("boss", "t1", 100) });
        match.ResolveStep(2, new[] { new Hit("boss", "t2", 100) });
        match.ResolveStep(3, new[] { new Hit("boss", "t3", 100), new Hit("t3", "boss", 300) });
        Assert.IsTrue(match.Ended);
        Assert.IsTrue(match.Draw);
        Assert.AreEqual(-1, match.WinnerTeam);
        Assert.IsNull(match.WinnerId);
    }

    [Test]
    public void ForfeitLeavesTheTrioShortHandedWithoutEndingTheMatch()
    {
        var match = Boss();
        match.ResolveStep(1, new Hit[0], new[] { "t1" });
        Assert.IsFalse(match.Ended);
        Assert.AreEqual("DISCONNECTED", match.Get("t1").EliminationReason);
        Assert.AreEqual(0, match.Players.Sum(p => p.Kills));
        Assert.AreEqual(2, match.Players.Count(p => p.Team == 1 && p.Alive));
        match.ResolveStep(2, new[] { new Hit("boss", "t2", 100), new Hit("boss", "t3", 100) });
        Assert.IsTrue(match.Ended);
        Assert.AreEqual(0, match.WinnerTeam);
    }

    [Test]
    public void BossKeepsFightingAfterLosingTwoOfThreeOpponents()
    {
        var match = Boss();
        match.ResolveStep(1, new[] { new Hit("boss", "t1", 100), new Hit("boss", "t2", 100),
            new Hit("t1", "boss", 100), new Hit("t2", "boss", 100) });
        Assert.IsFalse(match.Ended);
        Assert.AreEqual(100, match.Get("boss").Hp);
        Assert.AreEqual(1, match.Players.Count(p => p.Team == 1 && p.Alive));
    }

    [Test]
    public void IndividualMatchesKeepTheirOwnRankingWhenTeamsAreOmitted()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 100) });
        match.ResolveStep(2, new[] { new Hit("p1", "p2", 100) });
        match.ResolveStep(3, new[] { new Hit("p1", "p3", 100) });
        Assert.AreEqual("p1", match.WinnerId);
        Assert.AreEqual(1, match.Get("p1").Rank);
        Assert.AreEqual(2, match.Get("p3").Rank);
        Assert.AreEqual(3, match.Get("p2").Rank);
        Assert.AreEqual(4, match.Get("p0").Rank);
        // 個人戦は1人が1チーム。陣営番号はスロット番号がそのまま入る。
        Assert.AreEqual(match.Get("p1").SlotIndex, match.WinnerTeam);
    }

    [Test]
    public void TeamRosterIsValidated()
    {
        var lives = new int[][] { new[] { 100 }, new[] { 100 } };
        // 人数と陣営の数が合わない
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, lives, new[] { 0 }));
        // 負の陣営番号
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, lives, new[] { 0, -1 }));
        // 全員が同じ陣営だと決着が付かない
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, lives, new[] { 1, 1 }));
    }

    [Test]
    public void BuffedHitPointsAboveTheSwordLimitAreAccepted()
    {
        Assert.DoesNotThrow(() => new MatchRules(new[] { "boss", "t1" },
            new int[][] { new[] { 3000 }, new[] { 100 } }, new[] { 0, 1 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "boss", "t1" },
            new int[][] { new[] { MatchRules.MaxHitPoints + 1 }, new[] { 100 } }, new[] { 0, 1 }));
    }

    [Test]
    public void LastStepDamageListsEachAttackerCappedByRemainingHp()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p0", "p1", 30), new Hit("p0", "p1", 20), new Hit("p2", "p3", 15) });
        // 同じ組み合わせは1ステップで1件（大きい方）にまとまる
        Assert.AreEqual(2, match.LastStepDamage.Count);
        Assert.IsFalse(match.LastStepDamage.Any(d => d.Lethal));
        Assert.IsTrue(match.LastStepDamage.Any(d => d.AttackerId == "p0" && d.TargetId == "p1" && d.Amount == 30));
        Assert.IsTrue(match.LastStepDamage.Any(d => d.AttackerId == "p2" && d.TargetId == "p3" && d.Amount == 15));

        // 2人がかりで残りHP(70)を超えたときは、表示も頭打ち後の取り分になる
        match.ResolveStep(2, new[] { new Hit("p0", "p1", 60), new Hit("p2", "p1", 60) });
        Assert.AreEqual(70, match.LastStepDamage.Where(d => d.TargetId == "p1").Sum(d => d.Amount));
        Assert.IsTrue(match.LastStepDamage.All(d => d.Lethal));
        Assert.AreEqual(match.Get("p0").DamageDealt + match.Get("p2").DamageDealt,
            match.LastStepDamage.Sum(d => d.Amount) + 30 + 15);

        // ダメージが無いステップや、古いtickでは空になる
        match.ResolveStep(3, new Hit[0]);
        Assert.AreEqual(0, match.LastStepDamage.Count);
        match.ResolveStep(1, new[] { new Hit("p0", "p2", 10) });
        Assert.AreEqual(0, match.LastStepDamage.Count);
    }

    [Test]
    public void LastStepDamageMarksTheBlowThatCostALifeAsLethal()
    {
        var match = new MatchRules(new[] { "p0", "p1" }, new[] { new[] { 50 }, new[] { 40, 100 } }, null);
        match.ResolveStep(1, new[] { new Hit("p0", "p1", 60) });
        // 残機があるので脱落はしないが、剣は1本壊れているので撃破の一撃として扱う
        Assert.IsTrue(match.Get("p1").Alive);
        Assert.AreEqual(1, match.LastStepDamage.Count);
        Assert.AreEqual(40, match.LastStepDamage[0].Amount);
        Assert.AreEqual(60, match.LastStepDamage[0].RawDamage);
        Assert.IsTrue(match.LastStepDamage[0].Lethal);
    }

    [Test]
    public void EveryCollisionOrderingHasTheSameFourWayDraw()
    {
        var hits = new[] { new Hit("p0", "p1", 100), new Hit("p1", "p2", 100),
            new Hit("p2", "p3", 100), new Hit("p3", "p0", 100) };
        for (int a = 0; a < 4; a++)
        for (int b = 0; b < 4; b++)
        for (int c = 0; c < 4; c++)
        for (int d = 0; d < 4; d++)
        {
            var order = new[] { a, b, c, d };
            if (order.Distinct().Count() != 4) continue;
            var match = Four();
            match.ResolveStep(1, order.Select(i => hits[i]));
            Assert.IsTrue(match.Draw);
            Assert.IsTrue(match.Players.All(p => p.Rank == 1 && p.Kills == 1 && p.DamageDealt == 100));
        }
    }

    // ===== 巨大回転斬：2人目以降は威力を減らす =====

    [Test]
    public void GiantSpinHitsLaterTargetsForLess()
    {
        Assert.AreEqual(1f, BattlePolicies.GiantSpinHitRate(1, .7f, .5f));
        Assert.AreEqual(.7f, BattlePolicies.GiantSpinHitRate(2, .7f, .5f));
        Assert.AreEqual(.5f, BattlePolicies.GiantSpinHitRate(3, .7f, .5f));
        Assert.AreEqual(.5f, BattlePolicies.GiantSpinHitRate(4, .7f, .5f));
        // 設定を間違えても、威力が増えたり負になったりしない
        Assert.AreEqual(1f, BattlePolicies.GiantSpinHitRate(2, 3f, .5f));
        Assert.AreEqual(0f, BattlePolicies.GiantSpinHitRate(3, .7f, -1f));
        Assert.AreEqual(1f, BattlePolicies.GiantSpinHitRate(2, float.NaN, .5f));
    }

    // ===== ライフスティール：実際に減らした量の一部を、攻撃した側が回復する（今は使っている技はないが、仕組みは残してある） =====

    [Test]
    public void LifestealHealsAShareOfTheDamageActuallyDealt()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 50) });
        match.ResolveStep(2, new[] { new Hit("p0", "p2", 30, .2f) });
        Assert.AreEqual(56, match.Get("p0").Hp, "30 damage at 20% heals 6.");
        Assert.AreEqual(70, match.Get("p2").Hp);
        Assert.AreEqual(30, match.Get("p0").DamageDealt, "Healing does not change the damage stats.");
        Assert.AreEqual(1, match.LastStepHealing.Count);
        Assert.AreEqual("p0", match.LastStepHealing[0].PlayerId);
        Assert.AreEqual(6, match.LastStepHealing[0].Amount);
    }

    [Test]
    public void LifestealNeverHealsPastTheCurrentSwordsMaximum()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 3) });
        match.ResolveStep(2, new[] { new Hit("p0", "p2", 50, .2f) });
        Assert.AreEqual(100, match.Get("p0").Hp);
        Assert.AreEqual(3, match.LastStepHealing.Single().Amount, "Only the missing HP is recorded as healed.");

        match.ResolveStep(3, new[] { new Hit("p0", "p3", 50, .2f) });
        Assert.IsEmpty(match.LastStepHealing, "A full sword records no healing.");
    }

    [Test]
    public void LifestealOnAFinishingBlowUsesTheHpThatWasLeft()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 60), new Hit("p3", "p2", 90) });
        match.ResolveStep(2, new[] { new Hit("p0", "p2", 300, .2f) });
        Assert.IsFalse(match.Get("p2").Alive);
        Assert.AreEqual(42, match.Get("p0").Hp, "Only the 10 HP that was left counts: 10 at 20% heals 2.");
    }

    [Test]
    public void LifestealIsWorkedOutFromEachAttackersOwnShare()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p2", "p0", 50), new Hit("p2", "p1", 50) });
        // p0 と p1 が同じステップで p3 を殴る。合計100が丸ごと入り、取り分は 60 と 40
        match.ResolveStep(2, new[] { new Hit("p0", "p3", 60, .5f), new Hit("p1", "p3", 40, .5f) });
        Assert.AreEqual(80, match.Get("p0").Hp);
        Assert.AreEqual(70, match.Get("p1").Hp);
    }

    [Test]
    public void ASwordBrokenInTheSameStepDoesNotHeal()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p0", "p1", 50, 1f), new Hit("p1", "p0", 100) });
        Assert.IsFalse(match.Get("p0").Alive);
        Assert.IsEmpty(match.LastStepHealing);

        // 残機制でも、壊れたステップのうちに次の剣へ持ち替わった時は、壊れた剣の回復を新しい剣に持ち込まない
        var lives = new MatchRules(new[] { "p0", "p1" }, new[] { new[] { 100, 100 }, new[] { 100 } });
        lives.ResolveStep(1, new[] { new Hit("p0", "p1", 50, 1f), new Hit("p1", "p0", 100) });
        Assert.AreEqual(100, lives.Get("p0").Hp);
        Assert.IsEmpty(lives.LastStepHealing);
    }

    [Test]
    public void OrdinaryHitsDoNotHealAndTheBiggestHitsRateIsUsed()
    {
        var match = Four();
        match.ResolveStep(1, new[] { new Hit("p1", "p0", 50) });
        match.ResolveStep(2, new[] { new Hit("p0", "p2", 40) });
        Assert.AreEqual(50, match.Get("p0").Hp);
        Assert.IsEmpty(match.LastStepHealing);

        // 同じステップに同じ相手へのヒットが2つあれば、大きい方の1つだけが通り、その回復率を使う
        match.ResolveStep(3, new[] { new Hit("p0", "p3", 10, 1f), new Hit("p0", "p3", 30, .5f) });
        Assert.AreEqual(70, match.Get("p3").Hp);
        Assert.AreEqual(65, match.Get("p0").Hp);
    }

    [Test]
    public void LifestealRateMustBeBetweenZeroAndOne()
    {
        Assert.Throws<System.ArgumentException>(() => new Hit("p0", "p1", 10, -.1f));
        Assert.Throws<System.ArgumentException>(() => new Hit("p0", "p1", 10, 1.1f));
        Assert.Throws<System.ArgumentException>(() => new Hit("p0", "p1", 10, float.NaN));
        Assert.AreEqual(0f, new Hit("p0", "p1", 10).LifestealRate);
    }
}
