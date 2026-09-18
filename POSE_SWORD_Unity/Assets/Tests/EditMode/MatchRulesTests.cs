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
}
