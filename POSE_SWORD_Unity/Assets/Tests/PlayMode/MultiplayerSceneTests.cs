using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Reflection allows tests to exercise the serialized scene's Assembly-CSharp
// components without moving legacy scripts into a different assembly.
public class MultiplayerSceneTests
{
    private Component manager;
    private static Type RuntimeType(string name) { return Type.GetType(name + ", Assembly-CSharp", true); }
    private void Call(string method, string json) { manager.GetType().GetMethod(method).Invoke(manager, new object[] { json }); }
    private bool Playing { get { return (bool)manager.GetType().GetProperty("IsPlaying").GetValue(manager); } }

    private string Config(string id, bool host, int capacity = 4, string mode = "0")
    {
        var texture = new Texture2D(4, 4);
        texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); texture.Apply();
        string image = Convert.ToBase64String(texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
        var players = Enumerable.Range(0, capacity).Select(i => "{\"playerId\":\"p" + i + "\",\"slotIndex\":" + i +
            ",\"spawnIndex\":" + i + ",\"swordData\":{\"name\":\"Test\",\"hp\":1000,\"attack\":1,\"weight\":50,\"imageStr\":\"" + image + "\"}}");
        return "{\"matchId\":\"" + id + "\",\"localPlayerId\":\"p" + (host ? "0" : "1") +
            "\",\"isHost\":" + (host ? "true" : "false") + ",\"gameMode\":\"" + mode + "\",\"players\":[" + string.Join(",", players) + "]}";
    }

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene");
        yield return null;
        manager = GameObject.Find("GameManager").GetComponent(RuntimeType("MultiplayerManager"));
        Assert.IsNotNull(manager);
        // エディタで遊ぶためのハーネスがシーンに付いていると、Playした直後に自前のホスト対戦を始めてテストの試合と競合する。
        // また MultiplayerManager に依存しているので、残したままだと後片付けで MultiplayerManager を消せない
        var harnessType = Type.GetType("MultiplayerLocalTestHarness, Assembly-CSharp", false);
        var harness = harnessType == null ? null : manager.GetComponent(harnessType) as MonoBehaviour;
        if (harness != null)
        {
            harness.StopAllCoroutines();
            UnityEngine.Object.Destroy(harness);
            yield return null;
        }
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (manager != null) UnityEngine.Object.Destroy(manager);
        yield return null;
    }

    [UnityTest]
    public IEnumerator HostEliminationContinuesAndRematchCreatesFourFreshSwords()
    {
        Call("InitializeMultiplayer", Config("first", true));
        yield return null; yield return null;
        Assert.AreEqual(4, GameObject.Find("MultiplayerArena").GetComponentsInChildren(RuntimeType("SwordBattle")).Length);
        Call("BeginMultiplayer", "{\"matchId\":\"first\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        Assert.IsTrue(Playing);
        Call("ForfeitMultiplayer", "{\"matchId\":\"first\",\"playerId\":\"p0\"}");
        yield return new WaitForFixedUpdate();
        Assert.IsTrue(Playing, "The eliminated host must keep simulating the other three players.");
        var hostSword = GameObject.Find("Sword_p0").GetComponent(RuntimeType("SwordBattle"));
        Assert.AreEqual(0, (int)hostSword.GetType().GetField("hp").GetValue(hostSword));
        // Eliminated players now ragdoll away like the local single-player DefeatRoutine instead of
        // freezing in place, so their Rigidbody2D stays simulated (physics keeps carrying it off).
        Assert.IsTrue(hostSword.GetComponent<Rigidbody2D>().simulated);
        foreach (string id in new[] { "p1", "p2" }) Call("ForfeitMultiplayer", "{\"matchId\":\"first\",\"playerId\":\"" + id + "\"}");
        yield return new WaitForFixedUpdate();
        Assert.IsFalse(Playing);
        Call("InitializeMultiplayer", Config("second", true));
        yield return null; yield return null;
        var swords = GameObject.Find("MultiplayerArena").GetComponentsInChildren(RuntimeType("SwordBattle"));
        Assert.AreEqual(4, swords.Length);
        Assert.IsTrue(swords.All(s => (int)s.GetType().GetField("hp").GetValue(s) == 1000));
        Assert.IsFalse(Playing, "Rematch must wait for the new initialization barrier.");
    }

    [UnityTest]
    public IEnumerator GuestOwnsOnlyItsAssignedSwordAndDoesNotSimulatePhysics()
    {
        Call("InitializeMultiplayer", Config("guest", false));
        yield return null; yield return null;
        var controllers = GameObject.Find("MultiplayerArena").GetComponentsInChildren(RuntimeType("SwordController"));
        Assert.AreEqual(1, controllers.Count(c => (bool)c.GetType().GetField("isLocalControlled").GetValue(c)));
        Assert.AreEqual("Sword_p1", controllers.Single(c => (bool)c.GetType().GetField("isLocalControlled").GetValue(c)).name);
        Assert.IsTrue(controllers.All(c => !c.GetComponent<Rigidbody2D>().simulated));
    }
    [UnityTest]
    public IEnumerator TwoPlayerRematchSupportsBothModesAndGuestSync()
    {
        foreach (string mode in new[] { "0", "1" })
        {
            string id = "two-" + mode;
            Call("InitializeMultiplayer", Config(id, true, 2, mode));
            yield return null; yield return null;
            var arena = GameObject.Find("MultiplayerArena");
            Assert.AreEqual(2, arena.GetComponentsInChildren(RuntimeType("SwordBattle")).Length);
            var bodies = arena.GetComponentsInChildren<Rigidbody2D>();
            Assert.IsTrue(bodies.All(b => b.gravityScale == (mode == "0" ? 1 : 0)));
            Assert.AreEqual(-bodies[0].position.x, bodies[1].position.x);
            Call("BeginMultiplayer", "{\"matchId\":\"" + id + "\"}");
            yield return new WaitForSecondsRealtime(3.2f);
            Assert.IsTrue(Playing);
            Call("ForfeitMultiplayer", "{\"matchId\":\"" + id + "\",\"playerId\":\"p1\"}");
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(Playing);
        }
        Call("InitializeMultiplayer", Config("two-guest", false, 2));
        yield return null; yield return null;
        Call("SyncMultiplayer", "{\"matchId\":\"two-guest\",\"tick\":1,\"phase\":\"PLAYING\",\"players\":[{\"playerId\":\"p0\",\"hp\":500},{\"playerId\":\"p1\",\"hp\":900}]}");
        Assert.IsTrue(Playing);
        var guest = GameObject.Find("Sword_p1").GetComponent(RuntimeType("SwordBattle"));
        Assert.AreEqual(900, (int)guest.GetType().GetField("hp").GetValue(guest));
    }

    // ===== 残機制：剣が壊れてから次の剣が出るまで =====

    // p0 は手持ちの剣を lives の本数だけ持つ。p1 は1本だけ
    private string LivesConfig(string id, bool host, params int[] lives)
    {
        var texture = new Texture2D(4, 4);
        texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); texture.Apply();
        string image = Convert.ToBase64String(texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
        Func<int, string> slot = hp => "{\"name\":\"Test\",\"hp\":" + hp + ",\"attack\":1,\"weight\":50,\"imageStr\":\"" + image +
            "\",\"hiltType\":\"0\",\"isEmpty\":false}";
        string p0 = "{\"playerId\":\"p0\",\"slotIndex\":0,\"spawnIndex\":0,\"swordData\":{\"name\":\"Test\",\"hp\":" + lives[0] +
            ",\"attack\":1,\"weight\":50,\"imageStr\":\"" + image + "\",\"swords\":[" + string.Join(",", lives.Select(slot)) + "],\"equippedIndex\":0}}";
        string p1 = "{\"playerId\":\"p1\",\"slotIndex\":1,\"spawnIndex\":1,\"swordData\":{\"name\":\"Test\",\"hp\":1000,\"attack\":1,\"weight\":50,\"imageStr\":\"" + image + "\"}}";
        return "{\"matchId\":\"" + id + "\",\"localPlayerId\":\"p" + (host ? "0" : "1") + "\",\"isHost\":" + (host ? "true" : "false") +
            ",\"gameMode\":\"0\",\"livesMode\":true,\"players\":[" + p0 + "," + p1 + "]}";
    }

    private static Component Sword(string playerId) { return GameObject.Find("Sword_" + playerId).GetComponent(RuntimeType("SwordBattle")); }
    private static int Hp(Component sword) { return (int)sword.GetType().GetField("hp").GetValue(sword); }
    private static bool Flag(Component sword, string property) { return (bool)sword.GetType().GetProperty(property).GetValue(sword); }
    private void QueueHit(Component attacker, Component target, int damage, bool byBody = true)
    {
        manager.GetType().GetMethod("QueueHit").Invoke(manager, new object[] { attacker, target, damage, Vector2.zero, false, byBody, 0f });
    }
    private void Sync(string id, int tick, int p0Hp, int p0LifeIndex)
    {
        Call("SyncMultiplayer", "{\"matchId\":\"" + id + "\",\"tick\":" + tick + ",\"phase\":\"PLAYING\",\"players\":[" +
            "{\"playerId\":\"p0\",\"hp\":" + p0Hp + ",\"lifeIndex\":" + p0LifeIndex + "},{\"playerId\":\"p1\",\"hp\":1000}]}");
    }

    [UnityTest]
    public IEnumerator HostShowsTheNextSwordOnlyAfterTheBrokenOneHasGone()
    {
        Call("InitializeMultiplayer", LivesConfig("lives-host", true, 1000, 700));
        yield return null; yield return null;
        var p0 = Sword("p0"); var p1 = Sword("p1");
        float spawnX = p0.transform.position.x;
        Call("BeginMultiplayer", "{\"matchId\":\"lives-host\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        Assert.IsTrue(Playing);

        QueueHit(p1, p0, 1000);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsTrue(Playing, "Losing a sword while another one is left must not end the match.");
        Assert.AreEqual(0, Hp(p0));
        Assert.IsTrue(Flag(p0, "IsDead"), "The broken sword must go through the normal defeat.");
        Assert.IsTrue(p0.GetComponentsInChildren<Collider2D>(true).All(c => !c.enabled));
        Assert.IsNull(GameObject.Find("DefeatGhost"), "The real sword plays its own defeat, so no ghost copy is needed.");
        var killHit = GameObject.Find("KillHitElectric(Clone)");
        Assert.IsNotNull(killHit, "The blow that breaks a sword uses the dedicated hit effect.");
        // 元の青白ではなく、紅紫（赤みが強く、青より赤が勝つ色）に塗り替わっている
        Assert.IsTrue(killHit.GetComponentsInChildren<ParticleSystem>(true).All(ps =>
            ps.main.startColor.color.r > ps.main.startColor.color.b && ps.main.startColor.color.r > ps.main.startColor.color.g));

        // 次の剣を待っている間は、攻撃も被弾も通らない
        QueueHit(p0, p1, 500);
        QueueHit(p1, p0, 500);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(1000, Hp(p1));
        Assert.AreEqual(0, Hp(p0));

        yield return new WaitForSecondsRealtime(1.2f);
        Assert.IsTrue(Flag(p0, "IsDead"), "The next sword must wait until the defeat has played out.");
        Assert.AreEqual(0, (int)manager.GetType().GetMethod("GetCurrentLifeIndex").Invoke(manager, new object[] { "p0" }));

        yield return new WaitForSecondsRealtime(2.1f);
        Assert.IsTrue(Playing);
        Assert.IsFalse(Flag(p0, "IsDead"));
        Assert.AreEqual(700, Hp(p0));
        Assert.AreEqual(1, (int)manager.GetType().GetMethod("GetCurrentLifeIndex").Invoke(manager, new object[] { "p0" }));
        Assert.IsTrue(Flag(p0, "IsRespawnInvincible"));
        Assert.IsTrue(p0.GetComponentsInChildren<Collider2D>(true).All(c => c.enabled));
        Assert.Less(Mathf.Abs(p0.transform.position.x - spawnX), 1f, "The next sword comes out at the spawn point.");
    }

    [UnityTest]
    public IEnumerator GuestFollowsTheHostsBreakAndRespawn()
    {
        Call("InitializeMultiplayer", LivesConfig("lives-guest", false, 1000, 700, 500));
        yield return null; yield return null;
        var p0 = Sword("p0");
        Sync("lives-guest", 1, 1000, 0);
        Assert.IsTrue(Playing);

        // 壊れた：HP0だけが届く。やられ演出は本物の剣がする
        Sync("lives-guest", 2, 0, 0);
        Assert.IsTrue(Flag(p0, "IsDead"));
        Assert.IsNull(GameObject.Find("DefeatGhost"));

        // 待ち時間が明けた：次の剣の番号とHPが届く
        Sync("lives-guest", 3, 700, 1);
        Assert.IsFalse(Flag(p0, "IsDead"));
        Assert.AreEqual(700, Hp(p0));
        Assert.IsNull(GameObject.Find("DefeatGhost"));

        // HP0の同期を取りこぼして持ち替えだけが届いた時は、抜け殻でやられ演出を見せてから次の剣を出す
        Sync("lives-guest", 4, 500, 2);
        Assert.IsFalse(Flag(p0, "IsDead"));
        Assert.AreEqual(500, Hp(p0));
        Assert.IsNotNull(GameObject.Find("DefeatGhost"));
        yield return null;
    }

    // ===== 撃破した側の手応え：本体で倒した剣だけ短く止める =====

    [UnityTest]
    public IEnumerator KillerFreezesBrieflyOnlyWhenTheBodyLandedTheBlow()
    {
        Call("InitializeMultiplayer", Config("freeze", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"freeze\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        Assert.IsTrue(Playing);
        var p1 = Sword("p1"); var p2 = Sword("p2"); var p3 = Sword("p3"); var p0 = Sword("p0");
        var body = p1.GetComponent<Rigidbody2D>();

        QueueHit(p1, p2, 1000);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsTrue(Playing);
        Assert.AreEqual(RigidbodyConstraints2D.FreezeAll, body.constraints, "The sword that broke another must pause.");
        Assert.AreEqual(RigidbodyConstraints2D.None, p3.GetComponent<Rigidbody2D>().constraints, "Bystanders keep moving.");
        Assert.IsTrue(p1.GetComponentsInChildren<Collider2D>().All(c => c.enabled), "The paused sword can still be hit.");

        yield return new WaitForSecondsRealtime(.75f);
        Assert.AreEqual(RigidbodyConstraints2D.None, body.constraints, "The pause lasts only a moment.");

        // 分身などで倒した時は本体を止めない
        QueueHit(p3, p0, 1000, false);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsTrue(Playing);
        Assert.IsTrue(Flag(p0, "IsDead"));
        Assert.AreEqual(RigidbodyConstraints2D.None, p3.GetComponent<Rigidbody2D>().constraints);
    }

    [UnityTest]
    public IEnumerator SwordKilledWhilePausedGoesStraightIntoItsDefeat()
    {
        Call("InitializeMultiplayer", Config("freeze-ko", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"freeze-ko\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0"); var p1 = Sword("p1"); var p2 = Sword("p2");
        var body = p1.GetComponent<Rigidbody2D>();

        QueueHit(p1, p2, 1000);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(RigidbodyConstraints2D.FreezeAll, body.constraints);

        QueueHit(p0, p1, 1000);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsTrue(Playing);
        Assert.IsTrue(Flag(p1, "IsDead"));
        Assert.AreEqual(RigidbodyConstraints2D.None, body.constraints, "The pause is dropped as soon as the sword is defeated.");

        // やられ演出どおり、とどめの印を書き終えると跳ね上がる
        float heightAtDefeat = body.position.y;
        yield return new WaitForSecondsRealtime(1.6f);
        Assert.Greater(body.position.y, heightAtDefeat + 1f);
    }

    // ===== 必殺技の時間：カットインが抜けてから始まり、撃破で止まっている間は進まない =====

    private static void FireUltimate(Component sword, string hiltType)
    {
        sword.GetType().GetField("hiltType").SetValue(sword, hiltType);
        sword.GetType().GetField("currentSp").SetValue(sword, 100f);
        sword.GetType().GetMethod("ExecuteMultiplayerUltimate").Invoke(sword, null);
    }

    [UnityTest]
    public IEnumerator UltimateWaitsForItsCutinAndAHitStopDoesNotEndTheSlowMotion()
    {
        Call("InitializeMultiplayer", Config("cutin-wait", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"cutin-wait\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");

        FireUltimate(p0, "0"); // 大回転斬
        yield return new WaitForSecondsRealtime(.2f);
        Assert.IsTrue((bool)p0.GetType().GetField("isDashing").GetValue(p0), "The sword is protected while its cutin plays.");
        Assert.Less(p0.GetComponent<Rigidbody2D>().linearVelocity.y, 5f, "The jump must wait until the cutin has passed.");
        Assert.AreEqual(.05f, Time.timeScale, .001f);

        // カットインの最中に誰かが大きく被弾しても、ヒットストップの後でスローが解けない
        var p1 = Sword("p1");
        p1.GetType().GetMethod("PlayClientDamageEffect").Invoke(p1, new object[] { 50, true });
        yield return new WaitForSecondsRealtime(.3f);
        Assert.AreEqual(.05f, Time.timeScale, .001f, "A hit stop must not cut the cutin's slow motion short.");
        Assert.IsFalse((bool)p0.GetType().GetField("isDashShooting").GetValue(p0));

        // カットインが抜けた後で、跳んで止まって発射する
        yield return new WaitForSecondsRealtime(1.1f);
        Assert.AreEqual(1f, Time.timeScale, .001f);
        Assert.IsTrue((bool)p0.GetType().GetField("isDashShooting").GetValue(p0) ||
            !(bool)p0.GetType().GetField("isDashing").GetValue(p0), "The dash starts once the cutin is over.");
    }

    [UnityTest]
    public IEnumerator GiantSpinDoesNotLoseTimeWhileThePlayerIsPausedForAKill()
    {
        Call("InitializeMultiplayer", Config("giant-freeze", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"giant-freeze\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p1 = Sword("p1"); var p2 = Sword("p2");
        float baseScale = p1.transform.localScale.x;

        FireUltimate(p1, "1"); // 巨大回転斬
        float giveUp = Time.realtimeSinceStartup + 3f;
        while (p1.transform.localScale.x < baseScale * 2f && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.Greater(p1.transform.localScale.x, baseScale * 2f, "The sword grows once the cutin is over.");

        // 回り始めた直後に本体で倒して0.3秒止まる。一回転（360/540=約0.67秒）の時間は、止まっていた分だけ後ろへずれる
        QueueHit(p1, p2, 1000);
        yield return new WaitForSecondsRealtime(.8f);
        Assert.Greater(p1.transform.localScale.x, baseScale * 2f, "The pause must not eat into the spin.");

        yield return new WaitForSecondsRealtime(.6f);
        Assert.AreEqual(baseScale, p1.transform.localScale.x, .001f, "The spin still ends after one full turn.");
    }

}
