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

        // カットイン（約1.35秒）が抜けた後で、跳んで止まって発射する
        yield return new WaitForSecondsRealtime(1.5f);
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

        FireUltimate(p1, "1"); // 巨大回転斬
        float giveUp = Time.realtimeSinceStartup + 4f;
        while (!Field(p1, "giantSpinHitActive") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsTrue(Field(p1, "giantSpinHitActive"), "The spin starts once the sword has grown and wound up.");

        // 回り始めた直後に本体で倒して0.3秒止まる。一回転（360/540=約0.67秒）の時間は、止まっていた分だけ後ろへずれる
        QueueHit(p1, p2, 1000);
        yield return new WaitForSecondsRealtime(.8f);
        Assert.IsTrue(Field(p1, "giantSpinHitActive"), "The pause must not eat into the spin.");

        yield return new WaitForSecondsRealtime(.5f);
        Assert.IsFalse(Field(p1, "giantSpinHitActive"), "The spin still ends after one full turn.");
    }

    [UnityTest]
    public IEnumerator GiantSpinGrowsAfterItsCutinHitsOnlyWhileTurningAndStaggersAfterwards()
    {
        Call("InitializeMultiplayer", Config("giant-flow", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"giant-flow\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p1 = Sword("p1");
        float baseScale = p1.transform.localScale.x;
        float baseDepth = p1.transform.localScale.z;

        FireUltimate(p1, "1");
        yield return new WaitForSecondsRealtime(.5f);
        Assert.AreEqual(baseScale, p1.transform.localScale.x, .001f, "Nothing grows while the cutin plays.");

        // 柄が光る：段階が同期用の値に出て、柄のまわりに光のにじみが出る
        float giveUp = Time.realtimeSinceStartup + 3f;
        while (GiantPhase(p1) != 1 && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.AreEqual(1, GiantPhase(p1), "The hilt glows first.");
        yield return null;
        var halo = GameObject.Find("GiantSpinHiltHalo");
        Assert.IsNotNull(halo, "The glowing hilt has a halo.");
        Assert.IsTrue(halo.GetComponent<SpriteRenderer>().enabled);

        giveUp = Time.realtimeSinceStartup + 3f;
        while (p1.transform.localScale.x < baseScale * 1.05f && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.Greater(p1.transform.localScale.x, baseScale * 1.05f, "The sword grows once the cutin is over.");
        Assert.IsFalse(Field(p1, "giantSpinHitActive"), "Growing does not hit anyone yet.");
        // 円は剣の LateUpdate で描くので、大きくなったフレームの終わりまで待ってから見る
        yield return null;
        var reach = GameObject.Find("GiantSpinReach_" + p1.name);
        Assert.IsNotNull(reach, "The reach of the spin is shown while the sword grows.");
        Assert.IsTrue(reach.GetComponent<LineRenderer>().enabled);

        giveUp = Time.realtimeSinceStartup + 3f;
        while (!Field(p1, "giantSpinHitActive") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsTrue(Field(p1, "giantSpinHitActive"));
        float giantScale = (float)p1.GetType().GetField("giantSpinScale").GetValue(p1);
        Assert.AreEqual(5f, giantScale, .001f, "The sword grows five times as big.");
        Assert.AreEqual(baseScale * giantScale, p1.transform.localScale.x, .01f, "The sword is fully grown before it turns.");
        Assert.AreEqual(4, GiantPhase(p1), "The turning stage is shared with guests.");
        yield return new WaitForSecondsRealtime(.15f);
        var slash = GameObject.Find("GiantSpinSlash");
        Assert.IsNotNull(slash, "The turn leaves a slash behind the blade.");
        Assert.IsTrue(slash.GetComponent<MeshRenderer>().enabled);
        // 奥行きまで拡大すると、手前(z=-5)に置いた柄がカメラ(z=-10)の後ろへ回り込んで消える
        Assert.AreEqual(baseDepth, p1.transform.localScale.z, .001f, "Growing must not stretch the depth.");
        var hilt = p1.GetComponentsInChildren<Transform>().First(t => t.CompareTag("Handle"));
        Assert.Greater(hilt.position.z, Camera.main.transform.position.z, "The hilt stays in front of the camera.");

        // 回し終えたら縮み、ふらつく。ふらつきは無敵ではない
        giveUp = Time.realtimeSinceStartup + 3f;
        while (!Flag(p1, "IsStaggered") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsTrue(Flag(p1, "IsStaggered"));
        Assert.AreEqual(baseScale, p1.transform.localScale.x, .001f);
        Assert.IsFalse(Field(p1, "isDashing"), "A staggering sword can be hit.");
        Assert.AreEqual(0, GiantPhase(p1), "The stage is cleared once the sword is back to size.");
        Assert.IsFalse(reach.GetComponent<LineRenderer>().enabled);
        Assert.IsFalse(slash.GetComponent<MeshRenderer>().enabled, "The slash is gone by then.");
        // ふらつき：本物の刀身は隠し、揺れる写しと黄色い星を見せる（本物を回すと当たり判定まで回るため）
        yield return null;
        var blade = p1.transform.Find("Blade").GetComponent<SpriteRenderer>();
        Assert.IsTrue(blade.forceRenderingOff, "The real blade is hidden behind its wobbling stand-in.");
        Assert.IsNotNull(GameObject.Find("GiantStaggerStandIn"));
        var star = GameObject.Find("GiantStaggerStar");
        Assert.IsNotNull(star, "Stars circle above a staggering sword.");
        Assert.IsTrue(star.GetComponent<SpriteRenderer>().enabled);

        giveUp = Time.realtimeSinceStartup + 2f;
        while (Flag(p1, "IsStaggered") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsFalse(Flag(p1, "IsStaggered"), "The stagger wears off.");
        yield return null;
        Assert.IsFalse(blade.forceRenderingOff, "The real blade shows again after the stagger.");
        Assert.IsFalse(star.GetComponent<SpriteRenderer>().enabled);
    }

    private static int GiantPhase(Component sword) => (int)sword.GetType().GetField("giantSpinPhase").GetValue(sword);

    // ===== オレ達アタック：分身が減らしたHPの2割を本体が回復する =====

    [UnityTest]
    public IEnumerator CloneLifestealHealsTheOwnerByAShareOfTheDamageDealt()
    {
        Call("InitializeMultiplayer", Config("lifesteal", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"lifesteal\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0"); var p1 = Sword("p1"); var p2 = Sword("p2");

        QueueHit(p1, p0, 500);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(500, Hp(p0));

        // 分身の一撃（本体ではない・回復率は剣の設定値）
        float rate = (float)p0.GetType().GetField("cloneLifestealRate").GetValue(p0);
        Assert.AreEqual(.2f, rate, .001f);
        p0.GetType().GetMethod("DealDamageTo").Invoke(p0, new object[] { p2, 100, Vector2.zero, true, false, false, rate });
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(900, Hp(p2));
        Assert.AreEqual(520, Hp(p0), "100 damage at 20% heals 20.");

        // 回復した量が「+20」で出る
        var textType = Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro", true);
        var popups = UnityEngine.Object.FindObjectsByType(RuntimeType("DamagePopup"), FindObjectsSortMode.None);
        Assert.IsTrue(popups.Any(p => (string)textType.GetProperty("text").GetValue(((Component)p).GetComponent(textType)) == "+20"),
            "The healed amount is shown.");
    }

    // 撃ち出された分身を、出てきた順に集める（出てきた時刻と、撃ち出した位置の本体からの距離も）
    private static IEnumerator CollectFiredClones(Component master, System.Collections.Generic.List<(Component clone, float at, float fromMaster)> fired, float seconds)
    {
        var cloneType = RuntimeType("SwordCloneProjectile");
        var launchedFlag = cloneType.GetProperty("Launched");
        var seen = new System.Collections.Generic.HashSet<int>();
        float giveUp = Time.realtimeSinceStartup + seconds;
        while (fired.Count < 3 && Time.realtimeSinceStartup < giveUp)
        {
            foreach (Component clone in UnityEngine.Object.FindObjectsByType(cloneType, FindObjectsSortMode.None))
            {
                if (clone == null || !seen.Add(clone.GetInstanceID())) continue;
                Assert.IsTrue((bool)launchedFlag.GetValue(clone), "A clone is fired the moment it appears.");
                var from = (Vector2)cloneType.GetProperty("LaunchedFrom").GetValue(clone);
                fired.Add((clone, Time.realtimeSinceStartup, Vector2.Distance(from, master.transform.position)));
            }
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator CloneRushChargesThenFiresClonesFromTheMasterOneAtATime()
    {
        Call("InitializeMultiplayer", Config("clone-rush", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"clone-rush\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var cloneType = RuntimeType("SwordCloneProjectile");
        var phaseField = p0.GetType().GetField("cloneRushPhase");

        FireUltimate(p0, "2"); // オレ達アタック
        // まず溜める：その間は分身は出ていない
        float giveUp = Time.realtimeSinceStartup + 3f;
        while ((int)phaseField.GetValue(p0) != 1 && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.AreEqual(1, (int)phaseField.GetValue(p0), "The master charges first.");
        Assert.AreEqual(3, (int)p0.GetType().GetField("cloneRushShotsLeft").GetValue(p0), "Three shots are ready.");
        Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType(cloneType, FindObjectsSortMode.None).Length, "No clone is out while charging.");

        // 本体の位置から、1体ずつ間を空けてそのまま撃ち出す
        var fired = new System.Collections.Generic.List<(Component clone, float at, float fromMaster)>();
        yield return CollectFiredClones(p0, fired, 3f);
        Assert.AreEqual(3, fired.Count, "Every clone is fired.");
        Assert.IsTrue(fired.All(f => f.fromMaster < .5f), "Each clone is fired from where the master is.");
        Assert.Greater(fired[1].at - fired[0].at, .2f, "There is a pause between shots.");
        Assert.Greater(fired[2].at - fired[1].at, .2f, "There is a pause between shots.");
        Assert.IsTrue(fired.Where(f => f.clone != null).All(f => !f.clone.GetComponent<Collider2D>().isTrigger), "A fired clone can hit.");
        yield return null;
        Assert.AreEqual(0, (int)phaseField.GetValue(p0), "The stage is cleared once all are fired.");
    }

    [UnityTest]
    public IEnumerator CloneRushAimsEachShotAtSomeoneStillStanding()
    {
        Call("InitializeMultiplayer", Config("clone-retarget", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"clone-retarget\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var targetField = RuntimeType("SwordCloneProjectile").GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);

        FireUltimate(p0, "2");
        var fired = new System.Collections.Generic.List<(Component clone, float at, float fromMaster)>();
        // 1体目が撃たれたら、それが狙った相手を倒す
        float giveUp = Time.realtimeSinceStartup + 4f;
        while (fired.Count == 0 && Time.realtimeSinceStartup < giveUp)
        {
            var first = UnityEngine.Object.FindObjectsByType(RuntimeType("SwordCloneProjectile"), FindObjectsSortMode.None).Cast<Component>().FirstOrDefault();
            if (first != null) fired.Add((first, Time.realtimeSinceStartup, 0f));
            yield return null;
        }
        Assert.AreEqual(1, fired.Count, "The first clone is fired.");
        var aimed = ((Transform)targetField.GetValue(fired[0].clone)).GetComponentInParent(RuntimeType("SwordBattle"));
        QueueHit(p0, aimed, 5000);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsFalse(Flag(aimed, "IsAlive"), "The aimed enemy is down.");

        // 次の分身は、まだ立っている相手を狙って撃たれる
        Component second = null;
        giveUp = Time.realtimeSinceStartup + 2f;
        while (second == null && Time.realtimeSinceStartup < giveUp)
        {
            second = UnityEngine.Object.FindObjectsByType(RuntimeType("SwordCloneProjectile"), FindObjectsSortMode.None).Cast<Component>()
                .FirstOrDefault(c => c != fired[0].clone);
            yield return null;
        }
        Assert.IsNotNull(second, "The next clone is fired.");
        var next = ((Transform)targetField.GetValue(second)).GetComponentInParent(RuntimeType("SwordBattle"));
        Assert.AreNotEqual(aimed, next, "It is aimed at someone else.");
        Assert.IsTrue(Flag(next, "IsAlive"), "Its target is still standing.");
    }

    [UnityTest]
    public IEnumerator CloneRushFiresAllThreeEvenWhenLyingOnTheGround()
    {
        Call("InitializeMultiplayer", Config("clone-lying", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"clone-lying\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var body = p0.GetComponent<Rigidbody2D>();
        // 横向きに寝かせて、地面に落ち着くまで待つ
        body.rotation = 90f; body.angularVelocity = 0f;
        yield return new WaitForSecondsRealtime(1.5f);

        FireUltimate(p0, "2");
        var fired = new System.Collections.Generic.List<(Component clone, float at, float fromMaster)>();
        yield return CollectFiredClones(p0, fired, 4f);
        Assert.AreEqual(3, fired.Count, "All three clones are fired even when the sword lies on the ground.");
        // 撃った直後（0.1秒以内）に床などに当たって消えた分身がないか
        yield return new WaitForSecondsRealtime(.1f);
        Assert.IsNotNull(fired[2].clone, "The last clone does not vanish the moment it is fired.");
    }

    [UnityTest]
    public IEnumerator CloneRushLetsTheMasterMoveAndSizesClonesLikeTheMastersBlade()
    {
        Call("InitializeMultiplayer", Config("clone-move", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"clone-move\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var body = p0.GetComponent<Rigidbody2D>();
        var cloneType = RuntimeType("SwordCloneProjectile");

        FireUltimate(p0, "2");
        float giveUp = Time.realtimeSinceStartup + 3f;
        Component clone = null;
        while (clone == null && Time.realtimeSinceStartup < giveUp)
        {
            clone = UnityEngine.Object.FindObjectsByType(cloneType, FindObjectsSortMode.None).Cast<Component>().FirstOrDefault();
            yield return null;
        }
        Assert.IsNotNull(clone);
        Assert.IsTrue(Field(p0, "isDashing"), "The master is still protected when the first clone is fired.");

        // 無敵の間でも、本体はジャンプできる
        float before = body.linearVelocity.y;
        p0.GetType().GetMethod("ExecuteMultiplayerAction").Invoke(p0, new object[] { true });
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.Greater(body.linearVelocity.y, before + 1f, "The master can jump while its clones take aim.");

        // 分身の横幅は、装備中の剣の画像に関係なく、本体の刀身の横幅（刀身の太さ×本体の大きさ）にそろう
        var sprite = clone.GetComponent<SpriteRenderer>().sprite;
        float bladeWidth = (float)RuntimeType("SwordGenerator").GetField("targetBladeWidth").GetValue(p0.GetComponent(RuntimeType("SwordGenerator")));
        Assert.AreEqual(bladeWidth * p0.transform.lossyScale.x, sprite.bounds.size.x * clone.transform.localScale.x, .01f,
            "A clone is as wide as its master's blade.");
    }

    // ===== オレ達シールド：本体は半減、分身は2回で割れ、触れた敵・殴ってきた敵に返す =====

    private IEnumerator RaiseShield(Component sword)
    {
        FireUltimate(sword, "3");
        float giveUp = Time.realtimeSinceStartup + 3f;
        while (!Flag(sword, "HasActiveLeafShield") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsTrue(Flag(sword, "HasActiveLeafShield"), "The shields come out once the cutin is over.");
        yield return new WaitForSecondsRealtime(.3f);
    }

    private static Component[] ShieldOrbs()
    {
        return UnityEngine.Object.FindObjectsByType(RuntimeType("LeafShieldOrb"), FindObjectsSortMode.None).Cast<Component>().ToArray();
    }

    private static int Durability(Component orb) { return (int)orb.GetType().GetProperty("Durability").GetValue(orb); }

    [UnityTest]
    public IEnumerator ShieldHalvesBodyDamageWearsAShieldAndHitsBack()
    {
        Call("InitializeMultiplayer", Config("shield-body", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"shield-body\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0"); var p1 = Sword("p1");
        yield return RaiseShield(p0);
        var orbs = ShieldOrbs();
        Assert.AreEqual(5, orbs.Length);
        // 自分の攻撃力を相手と違う値にして、本体の返しが「相手の攻撃力」で決まることを見分ける
        p0.GetType().GetField("attack").SetValue(p0, 30);
        Assert.IsTrue(orbs.All(o => Durability(o) == 2));
        Assert.IsFalse(Field(p0, "isDashing"), "The master is no longer invincible behind its shields.");
        float knockback = (float)p0.GetType().GetProperty("KnockbackScale").GetValue(p0);
        Assert.AreEqual(.3f, knockback, .001f, "Behind its shields the master is knocked back far less.");
        Assert.AreEqual(1f, (float)p1.GetType().GetProperty("KnockbackScale").GetValue(p1), .001f, "Others are knocked back as usual.");

        QueueHit(p1, p0, 100);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(950, Hp(p0), "Damage to the master is halved.");
        Assert.AreEqual(1, ShieldOrbs().Count(o => Durability(o) == 1), "Each hit on the master wears one shield down.");

        // 本体で殴ってきた相手には、相手の攻撃力ぶん（×1）を返す（自分の攻撃力30ではない）
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        int attack = (int)p1.GetType().GetField("attack").GetValue(p1);
        Assert.AreEqual(1000 - attack, Hp(p1), "The attacker takes its own attack back.");
    }

    [UnityTest]
    public IEnumerator ShieldOrbHitsBackOnTouchAndBreaksOnTheSecondTouch()
    {
        Call("InitializeMultiplayer", Config("shield-touch", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"shield-touch\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0"); var p1 = Sword("p1");
        yield return RaiseShield(p0);
        var orb = ShieldOrbs()[0];
        var touch = orb.GetType().GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
        var blade = p1.GetComponentsInChildren<Collider2D>().First(c => !c.CompareTag("Handle"));
        var handle = p1.GetComponentsInChildren<Collider2D>().First(c => c.CompareTag("Handle"));
        // 分身の返しは持ち主（自分）の攻撃力で決まる。相手の攻撃力（10）と違う値にして見分ける
        p0.GetType().GetField("attack").SetValue(p0, 30);
        int attack = 30;

        touch.Invoke(orb, new object[] { blade });
        touch.Invoke(orb, new object[] { handle }); // 刃と柄が続けて触れても1回に数える
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(1000 - attack, Hp(p1), "Touching a shield costs the enemy the master's attack.");
        Assert.AreEqual(1, Durability(orb));

        yield return new WaitForSecondsRealtime(.35f);
        touch.Invoke(orb, new object[] { blade });
        yield return null;
        Assert.IsTrue(orb == null, "A shield breaks on its second touch.");
        Assert.AreEqual(4, ShieldOrbs().Length);
    }

    [UnityTest]
    public IEnumerator ShieldRingReachesOutToAnEnemyJustOutOfRange()
    {
        Call("InitializeMultiplayer", Config("shield-ring", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"shield-ring\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0"); var p1 = Sword("p1");
        float sense = (float)p0.GetType().GetField("leafShieldSenseRadius").GetValue(p0);
        float farthest = (float)p0.GetType().GetField("leafShieldMaxOrbitRadius").GetValue(p0);
        float depth = (float)p0.GetType().GetField("leafShieldReachDepth").GetValue(p0);
        float usualSpeed = (float)p0.GetType().GetField("leafShieldOrbitSpeed").GetValue(p0);
        float chaseSpeed = (float)p0.GetType().GetField("leafShieldChaseOrbitSpeed").GetValue(p0);
        var speed = p0.GetType().GetProperty("LeafShieldOrbitSpeedNow");
        // 剣どうしが重なると物理で弾き飛ばされるので、確かめる間は2本とも物理で動かないよう止めておく
        var enemy = p1.GetComponent<Rigidbody2D>();
        p0.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        enemy.linearVelocity = Vector2.zero;
        enemy.constraints = RigidbodyConstraints2D.FreezeAll;
        // 輪は「一番近い敵」を見るので、ほかの敵（p2・p3）はこのテストの間、実射程よりはるか遠くへ移して止めておく
        for (int i = 2; i < 4; i++)
        {
            var other = Sword("p" + i).GetComponent<Rigidbody2D>();
            other.position = (Vector2)p0.transform.position + Vector2.right * (farthest * 3f + i * 10f);
            other.linearVelocity = Vector2.zero;
            other.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        var radius = p0.GetType().GetProperty("LeafShieldOrbitRadiusNow");
        Func<float> nearest = () => p1.GetComponentsInChildren<Collider2D>().Where(c => c.enabled)
            .Min(c => Vector2.Distance(c.ClosestPoint(p0.transform.position), p0.transform.position));
        Func<string> state = () => $"距離={nearest():F2}, 輪の半径={(float)radius.GetValue(p0):F2}, 回る速さ={(float)speed.GetValue(p0):F0}";
        // 追っている時の輪の半径：敵の一番近い点より depth だけ外（普段の半径5〜実射程の間）
        Func<float> expected = () => Mathf.Clamp(nearest() + depth, 5f, farthest);
        // 敵を「一番近い点までの距離が d」になる所へ置く（剣の中心と一番近い点の差は、はじめに測っておく）
        float farOffset = farthest + 5f;
        enemy.position = (Vector2)p0.transform.position + Vector2.right * farOffset;
        yield return new WaitForFixedUpdate();
        float gap = farOffset - nearest();
        Action<float> placeAt = d => enemy.position = (Vector2)p0.transform.position + Vector2.right * (d + gap);

        yield return RaiseShield(p0);
        Debug.Log("[テスト] 感知射程の外の敵：" + state());
        Assert.Less(farthest, sense, "Sensing reaches further than the ring can stretch.");
        Assert.AreEqual(5f, (float)radius.GetValue(p0), .01f, "An enemy beyond sensing range does not stretch the ring.");
        Assert.AreEqual(usualSpeed, (float)speed.GetValue(p0), .01f);
        Assert.IsNull(GameObject.Find("ShieldRingGuide_" + p0.name), "The temporary ring guide is gone.");
        var aura = GameObject.Find("LeafShieldAura");
        Assert.IsNotNull(aura, "The master is outlined while shielded.");
        Assert.IsTrue(aura.GetComponent<SpriteRenderer>().enabled);

        // 感知射程に入っても最大より遠い敵には、最大まで広げて止まる（届かない）
        float between = (sense + farthest) * .5f;
        placeAt(between);
        yield return new WaitForSecondsRealtime(.5f);
        Debug.Log("[テスト] 感知射程の中・最大より遠い：" + state());
        Assert.AreEqual(farthest, (float)radius.GetValue(p0), .3f, "The ring stretches to its limit toward a sensed enemy that is still too far.");
        Assert.AreEqual(usualSpeed, (float)speed.GetValue(p0), .01f, "The shields keep their usual speed while reaching.");
        Assert.AreEqual(usualSpeed, chaseSpeed, .01f);

        // 最大より近い敵には、その距離（の少し外）まで広げる
        placeAt((5f + farthest) * .5f);
        yield return new WaitForSecondsRealtime(1.5f);
        Debug.Log("[テスト] 最大より近い敵：" + state());
        Assert.AreEqual(expected(), (float)radius.GetValue(p0), .3f, "The ring fits a nearer enemy.");

        // 感知射程の境目を出入りしても、輪はすぐには戻らない
        placeAt(between);
        yield return new WaitForSecondsRealtime(.6f);
        for (int i = 0; i < 4; i++)
        {
            placeAt(i % 2 == 0 ? sense + .5f : sense - .5f);
            yield return new WaitForSecondsRealtime(.25f);
            Debug.Log("[テスト] 感知射程の境目を出入り：" + state());
            Assert.Greater((float)radius.GetValue(p0), farthest - 1f, "Brushing the edge of sensing range does not make the ring snap back.");
        }

        // 感知射程の外へ離れたら、少し待ってからゆっくり普段の半径へ戻る
        placeAt(sense + 3f);
        yield return new WaitForSecondsRealtime(.2f);
        Assert.Greater((float)radius.GetValue(p0), farthest - 1f, "The ring waits a moment before drawing back.");
        float returnSpeed = (float)p0.GetType().GetField("leafShieldRadiusReturnSpeed").GetValue(p0);
        yield return new WaitForSecondsRealtime(.6f + (farthest - 5f) / returnSpeed);
        Debug.Log("[テスト] 離れた後：" + state());
        Assert.AreEqual(5f, (float)radius.GetValue(p0), .01f, "The ring returns to its usual size.");

        // 近い敵には縮めない（普段の半径のまま）
        placeAt(2.5f);
        yield return new WaitForSecondsRealtime(1f);
        Debug.Log("[テスト] 近い敵：" + state());
        Assert.AreEqual(5f, (float)radius.GetValue(p0), .01f, "The ring never shrinks below its usual size.");
    }

    // ===== 大回転斬：倍率は×4、当たった物理ステップの間は無敵のまま、味方はすり抜ける =====

    private static bool Field(Component sword, string name)
    {
        return (bool)sword.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sword);
    }

    private static IEnumerator WaitUntilShooting(Component sword)
    {
        float giveUp = Time.realtimeSinceStartup + 3f;
        while (!Field(sword, "isDashShooting") && Time.realtimeSinceStartup < giveUp) yield return null;
    }

    // 1vs3：p0がボス、p1〜p3がトリオ。強化はすべて等倍にして、陣営の違いだけを見る
    private string SoloConfig(string id)
    {
        var config = Config(id, true);
        for (int i = 0; i < 4; i++)
            config = config.Replace("\"slotIndex\":" + i + ",", "\"slotIndex\":" + i + ",\"team\":" + (i == 0 ? 0 : 1) + ",");
        string buff = "{\"hpMultiplier\":1,\"attackMultiplier\":1,\"spGainMultiplier\":1,\"scaleMultiplier\":1,\"maxSp\":200," +
            "\"suppressRadius\":1," +
            "\"judgmentDamageMultiplier\":1,\"judgmentKnockback\":1}";
        return config.Replace("\"players\":[", "\"soloMode\":true,\"soloBuff\":" + buff + ",\"players\":[");
    }

    [UnityTest]
    public IEnumerator SpinDashStaysProtectedUntilItsHitIsResolvedAndDropsItsBonusAfterwards()
    {
        Call("InitializeMultiplayer", Config("spin-end", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-end\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var bonus = p0.GetType().GetField("dashDamageBonus", BindingFlags.NonPublic | BindingFlags.Instance);

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        Assert.IsTrue(Field(p0, "isDashShooting"), "The dash has launched.");
        Assert.AreEqual(4f, (float)bonus.GetValue(p0), .001f, "大回転斬 multiplies its hit by four.");

        // 相手に当たって突進が終わった瞬間を再現する。同じ物理ステップの残りの衝突でダメージを受けないよう、無敵はまだ残る
        p0.GetType().GetMethod("EndSwordDash").Invoke(p0, new object[] { true });
        Assert.IsTrue(Field(p0, "isDashing"), "The sword stays protected for the rest of the physics step.");
        Assert.IsFalse(Field(p0, "isDashShooting"));

        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsFalse(Field(p0, "isDashing"));
        Assert.AreEqual(1f, (float)bonus.GetValue(p0), .001f, "An unused bonus must not carry over to the next ordinary hit.");
    }

    private static int SpinPhase(Component sword) => (int)sword.GetType().GetField("spinDashPhase").GetValue(sword);

    private static bool InputBlocked(Component sword) =>
        (bool)sword.GetType().GetProperty("InputBlockedBySkill", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sword);

    private static IEnumerator WaitUntilSpinPhase(Component sword, int phase, float seconds = 4f)
    {
        float giveUp = Time.realtimeSinceStartup + seconds;
        while (SpinPhase(sword) != phase && Time.realtimeSinceStartup < giveUp) yield return null;
    }

    [UnityTest]
    public IEnumerator SpinDashThatMissesSlowsDownThenDescendsAndAJumpEndsTheDescent()
    {
        Call("InitializeMultiplayer", Config("spin-miss", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-miss\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        // 相手には当たらないようにして、2秒たって外れる終わり方を見る
        // 突進は速さを保って遠くまで届くので、自分以外の全員に当たらないようにする
        for (int i = 1; i < 4; i++)
        {
            var other = GameObject.Find("Sword_p" + i);
            if (other != null) foreach (var c in other.GetComponentsInChildren<Collider2D>()) c.enabled = false;
        }
        var body = p0.GetComponent<Rigidbody2D>();

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        Assert.AreEqual(3, SpinPhase(p0), "The dash stage is shown while it flies.");
        yield return null; // 演出は LateUpdate で段階の変わり目を見て出る
        Assert.IsNotNull(GameObject.Find("SpinDashBurst"), "Launching leaves the flash and shock ring where it started.");
        Assert.IsNotNull(GameObject.Find("SpinDashTrail"), "The dash draws a brush trail.");

        yield return WaitUntilSpinPhase(p0, 4);
        Assert.AreEqual(4, SpinPhase(p0), "Missing ends in the slow-down.");
        Assert.Greater(body.linearVelocity.magnitude, 1f, "It slows down instead of stopping dead.");
        Assert.AreEqual(0f, body.gravityScale, .001f, "It does not fall while slowing down.");
        Assert.IsTrue(InputBlocked(p0), "Input is not taken while it slows down.");

        yield return WaitUntilSpinPhase(p0, 6, 1f);
        Assert.AreEqual(6, SpinPhase(p0), "Then it descends gently.");
        Assert.IsFalse(InputBlocked(p0), "The descent can be controlled.");
        Assert.Less(body.gravityScale, 1f, "Gravity comes back gradually.");

        float before = body.linearVelocity.y;
        p0.GetType().GetMethod("ExecuteMultiplayerAction").Invoke(p0, new object[] { true });
        Assert.AreEqual(0, SpinPhase(p0), "A jump ends the descent at once.");
        Assert.AreEqual(1f, body.gravityScale, .001f, "Ordinary gravity is back after the jump.");
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.Greater(body.linearVelocity.y, before + 1f, "The jump itself happened.");
    }

    [UnityTest]
    public IEnumerator SpinDashGrazingAWallBehindItKeepsGoingInsteadOfTurningBack()
    {
        Call("InitializeMultiplayer", Config("spin-graze", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-graze\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        // 突進は速さを保って遠くまで届くので、自分以外の全員に当たらないようにする
        for (int i = 1; i < 4; i++)
        {
            var other = GameObject.Find("Sword_p" + i);
            if (other != null) foreach (var c in other.GetComponentsInChildren<Collider2D>()) c.enabled = false;
        }
        var body = p0.GetComponent<Rigidbody2D>();

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        // 天井ぎわから真下へ撃った時と同じ形を、床に邪魔されないよう横向きで再現する：
        // 右へ飛ばし、回っている刀身の左端がわずかに重なる所に、背中側の壁を置く
        // 突進中は保っている速さ（spinDashVelocity）が毎ステップ入るので、そちらを右向きにする
        p0.GetType().GetField("spinDashVelocity", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(p0, new Vector2(50f, 0f));
        body.linearVelocity = new Vector2(50f, 0f);
        yield return new WaitForFixedUpdate();
        float left = float.MaxValue;
        foreach (var c in p0.GetComponentsInChildren<Collider2D>()) if (c.enabled && !c.isTrigger) left = Mathf.Min(left, c.bounds.min.x);
        var ceiling = new GameObject("TestWallBehind").AddComponent<BoxCollider2D>();
        ceiling.size = new Vector2(4f, 40f);
        ceiling.transform.position = new Vector2(left + .2f - 2f, body.worldCenterOfMass.y);
        bool touched = false;
        try
        {
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForFixedUpdate();
                touched |= body.IsTouching(ceiling);
                Assert.Greater(body.linearVelocity.x, 30f, "Grazing the wall behind it must not turn the dash back.");
            }
            Assert.IsTrue(touched, "The spinning blade did graze the wall behind it.");
        }
        finally { UnityEngine.Object.Destroy(ceiling.gameObject); }
    }

    [UnityTest]
    public IEnumerator SpinDashSkimmingTheFloorSlidesAlongItInsteadOfBouncing()
    {
        Call("InitializeMultiplayer", Config("spin-skim", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-skim\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        for (int i = 1; i < 4; i++)
        {
            var other = GameObject.Find("Sword_p" + i);
            if (other != null) foreach (var c in other.GetComponentsInChildren<Collider2D>()) c.enabled = false;
        }
        var body = p0.GetComponent<Rigidbody2D>();

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        // 地上どうしで撃った時と同じ形：右へ少し下向き（約11°）に飛ばし、回る刀身の下端がわずかに重なる所に床を置く
        Vector2 heading = new Vector2(50f, -10f);
        p0.GetType().GetField("spinDashVelocity", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(p0, heading);
        body.linearVelocity = heading;
        yield return new WaitForFixedUpdate();
        float bottom = float.MaxValue;
        foreach (var c in p0.GetComponentsInChildren<Collider2D>()) if (c.enabled && !c.isTrigger) bottom = Mathf.Min(bottom, c.bounds.min.y);
        var floor = new GameObject("TestFloor").AddComponent<BoxCollider2D>();
        floor.size = new Vector2(200f, 4f);
        floor.transform.position = new Vector2(body.worldCenterOfMass.x, bottom + .2f - 2f);
        // 回る刀身が床を叩くと、そのステップだけ体が上へ弾かれる（次のステップで保っている速さに戻る）。
        // 刀身が床に届かない高さまで数回持ち上がったら、あとは床に沿ってなめらかに滑る
        int touchingSteps = 0;
        float startY = body.position.y;
        try
        {
            for (int i = 0; i < 16; i++)
            {
                yield return new WaitForFixedUpdate();
                if (body.IsTouching(floor)) touchingSteps++;
                Assert.Greater(body.linearVelocity.x, 40f, "It keeps going forward at full speed.");
            }
            Assert.Greater(touchingSteps, 0, "The spinning blade did touch the floor.");
            Assert.LessOrEqual(touchingSteps, 6, "It soon clears the floor instead of crawling along it.");
            Assert.Less(Mathf.Abs(body.linearVelocity.y), 5f, "Once clear it glides level.");
            Assert.Less(body.position.y - startY, 4f, "It is not knocked up into the air.");
            Assert.AreEqual(0f, ((Vector2)p0.GetType().GetField("spinDashVelocity", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p0)).y, .5f,
                "After touching it, the dash runs along the floor.");
        }
        finally { UnityEngine.Object.Destroy(floor.gameObject); }
    }

    [UnityTest]
    public IEnumerator SpinDashSlidingOffALiftTurnsBackTowardsItsTarget()
    {
        Call("InitializeMultiplayer", Config("spin-lift", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-lift\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        for (int i = 1; i < 4; i++)
        {
            var other = GameObject.Find("Sword_p" + i);
            if (other != null) foreach (var c in other.GetComponentsInChildren<Collider2D>()) c.enabled = false;
        }
        var body = p0.GetComponent<Rigidbody2D>();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var held = p0.GetType().GetField("spinDashVelocity", flags);

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        // リフトの上から、右下の地上の相手へ撃った時を再現する：足元に右端が少し先で終わるリフトを置き、狙う相手を右下に置く
        var target = new GameObject("TestTarget").transform;
        Vector2 heading = new Vector2(50f, -12f);
        held.SetValue(p0, heading);
        body.linearVelocity = heading;
        yield return new WaitForFixedUpdate();
        float bottom = float.MaxValue;
        foreach (var c in p0.GetComponentsInChildren<Collider2D>()) if (c.enabled && !c.isTrigger) bottom = Mathf.Min(bottom, c.bounds.min.y);
        Vector2 pivot = body.worldCenterOfMass;
        target.position = pivot + new Vector2(18f, -12f);
        p0.GetType().GetField("spinAimTarget", flags).SetValue(p0, target);
        var lift = new GameObject("TestLift").AddComponent<BoxCollider2D>();
        lift.size = new Vector2(10f, 1f);
        lift.transform.position = new Vector2(pivot.x + 1f, bottom + .2f - .5f); // 右端は 6 先
        // 向かい直した後に場面の床で跳ね返ることもあるので、滑った後に一度でも相手の方へ向かったかを見る
        bool skimmed = false, turned = false;
        try
        {
            for (int i = 0; i < 16 && !turned; i++)
            {
                yield return new WaitForFixedUpdate();
                Vector2 v = (Vector2)held.GetValue(p0);
                if (Mathf.Abs(v.y) < .5f) skimmed = true;
                else if (skimmed && v.y < -10f && v.x > 0f) turned = true;
            }
            Assert.IsTrue(skimmed, "It first slides along the lift.");
            Assert.IsTrue(turned, "Past the lift's edge it turns back down towards its target.");
        }
        finally { UnityEngine.Object.Destroy(lift.gameObject); UnityEngine.Object.Destroy(target.gameObject); }
    }

    [UnityTest]
    public IEnumerator SpinDashKeepsItsSpeedAndDirectionUntilItBounces()
    {
        Call("InitializeMultiplayer", Config("spin-keep", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-keep\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        // 突進は速さを保って遠くまで届くので、自分以外の全員に当たらないようにする
        for (int i = 1; i < 4; i++)
        {
            var other = GameObject.Find("Sword_p" + i);
            if (other != null) foreach (var c in other.GetComponentsInChildren<Collider2D>()) c.enabled = false;
        }
        var body = p0.GetComponent<Rigidbody2D>();

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        yield return new WaitForFixedUpdate();
        Vector2 launched = body.linearVelocity;
        float spin = body.angularVelocity;
        // 天井をかすって蹴られた時を再現する：速さと回転を乱しても、次のステップで元に戻る
        body.linearVelocity = launched * .3f + Vector2.Perpendicular(launched) * .5f;
        body.angularVelocity = spin * .2f;
        yield return new WaitForFixedUpdate();
        Assert.AreEqual(launched.x, body.linearVelocity.x, .5f, "The dash keeps the speed it was launched with.");
        Assert.AreEqual(launched.y, body.linearVelocity.y, .5f);
        Assert.AreEqual(spin, body.angularVelocity, 1f, "The dash keeps its spin.");
    }

    [UnityTest]
    public IEnumerator SpinDashThatHitsRecoilsBackwardsThenDescends()
    {
        Call("InitializeMultiplayer", Config("spin-hit", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-hit\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var body = p0.GetComponent<Rigidbody2D>();

        FireUltimate(p0, "0");
        yield return WaitUntilShooting(p0);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Vector2 travel = body.linearVelocity.normalized;

        // 敵に当たって突進が終わった時を再現する
        p0.GetType().GetMethod("EndSwordDash").Invoke(p0, new object[] { true });
        Assert.AreEqual(5, SpinPhase(p0), "A hit ends in the recoil.");
        Assert.IsTrue(InputBlocked(p0), "Input is not taken while it is knocked back.");
        yield return null;
        Assert.Less(Vector2.Dot(body.linearVelocity, travel), -1f, "It is knocked back against the way it was dashing.");
        Assert.IsNotNull(GameObject.Find("SpinDashSplash"), "Hitting another sword throws a splash in the player's colour.");

        yield return WaitUntilSpinPhase(p0, 6, 1f);
        Assert.AreEqual(6, SpinPhase(p0), "Then it descends gently.");
        yield return WaitUntilSpinPhase(p0, 0, 1.5f);
        Assert.AreEqual(0, SpinPhase(p0), "The descent ends by itself.");
        Assert.AreEqual(1f, body.gravityScale, .001f, "Ordinary gravity is back once it has descended.");
    }

    [UnityTest]
    public IEnumerator SpinDashPassesThroughTeammatesOnlyWhileItLasts()
    {
        Call("InitializeMultiplayer", SoloConfig("spin-team"));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"spin-team\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        Assert.IsTrue(Playing);
        var p0 = Sword("p0"); var p1 = Sword("p1"); var p2 = Sword("p2");
        var dasher = p1.GetComponentInChildren<Collider2D>();

        FireUltimate(p1, "0");
        yield return WaitUntilShooting(p1);
        Assert.IsTrue(Field(p1, "isDashShooting"), "The dash has launched.");
        Assert.IsTrue(p2.GetComponentsInChildren<Collider2D>().All(c => Physics2D.GetIgnoreCollision(dasher, c)), "Teammates are passed through.");
        Assert.IsTrue(p0.GetComponentsInChildren<Collider2D>().All(c => !Physics2D.GetIgnoreCollision(dasher, c)), "The opposing side is still hit.");

        float giveUp = Time.realtimeSinceStartup + 4f;
        while (Field(p1, "isDashing") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsFalse(Field(p1, "isDashing"));
        Assert.IsTrue(p2.GetComponentsInChildren<Collider2D>().All(c => !Physics2D.GetIgnoreCollision(dasher, c)), "Teammates collide again once the dash is over.");
    }

}
