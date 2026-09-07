using System.Linq;
using NUnit.Framework;
using PoseSword.Multiplayer;

public class MatchRulesTests
{
    [Test]
    public void ActionsAreChosenByHostSpAndRoundState()
    {
        Assert.IsNull(BattlePolicies.Action(true, 100, false, true, false, true));
        Assert.IsNull(BattlePolicies.Action(true, 100, true, false, false, true));
        Assert.IsNull(BattlePolicies.Action(true, 100, true, true, true, true));
        Assert.IsNull(BattlePolicies.Action(true, 19, true, true, false, true));
        Assert.AreEqual("KomaDash", BattlePolicies.Action(true, 20, true, true, false, true));
        Assert.AreEqual("Tornado", BattlePolicies.Action(true, 70, true, true, false, true));
        Assert.AreEqual("SwordDash", BattlePolicies.Action(false, 100, true, true, false, true));
        Assert.AreEqual("JumpLeft", BattlePolicies.Action(false, 99, true, true, false, false));
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
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b", "c" }, new[] { 100, 100, 100 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, new[] { 0, 100 }));
        Assert.Throws<System.ArgumentException>(() => new MatchRules(new[] { "a", "b" }, new[] { 100 }));
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
