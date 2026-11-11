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

        float giveUp = Time.realtimeSinceStartup + 3f;
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
        Assert.AreEqual(baseScale * 6f, p1.transform.localScale.x, .01f, "The sword is fully grown before it turns.");
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
        Assert.IsFalse(reach.GetComponent<LineRenderer>().enabled);

        giveUp = Time.realtimeSinceStartup + 2f;
        while (Flag(p1, "IsStaggered") && Time.realtimeSinceStartup < giveUp) yield return null;
        Assert.IsFalse(Flag(p1, "IsStaggered"), "The stagger wears off.");
    }

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

    [UnityTest]
    public IEnumerator CloneRushHoldsItsClonesThenFiresThemOneAtATime()
    {
        Call("InitializeMultiplayer", Config("clone-rush", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"clone-rush\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var cloneType = RuntimeType("SwordCloneProjectile");
        var launchedFlag = cloneType.GetProperty("Launched");
        Func<Component[]> clones = () => UnityEngine.Object.FindObjectsByType(cloneType, FindObjectsSortMode.None).Cast<Component>().ToArray();
        Func<Component, bool> launched = c => (bool)launchedFlag.GetValue(c);

        FireUltimate(p0, "2"); // オレ達アタック
        float giveUp = Time.realtimeSinceStartup + 3f;
        while (clones().Length < 3 && Time.realtimeSinceStartup < giveUp) yield return null;
        var held = clones();
        Assert.AreEqual(3, held.Length, "Three clones come out once the cutin is over.");
        Assert.IsTrue(held.All(c => !launched(c)), "The clones take aim before anything is fired.");
        Assert.IsTrue(held.All(c => c.GetComponent<Collider2D>().isTrigger), "Held clones cannot hit or be pushed.");
        // 壁や床の向こう側に出ていない（自分の中心との間に壁がない）
        foreach (var clone in held)
            Assert.IsFalse(Physics2D.LinecastAll(p0.transform.position, clone.transform.position).Any(h =>
                !h.collider.isTrigger && h.collider.GetComponentInParent(RuntimeType("SwordBattle")) == null &&
                h.collider.GetComponent(cloneType) == null), "A clone must not appear behind a wall.");

        // 1体ずつ、間を空けて撃つ
        var fired = new System.Collections.Generic.Dictionary<int, float>();
        giveUp = Time.realtimeSinceStartup + 3f;
        while (fired.Count < 3 && Time.realtimeSinceStartup < giveUp)
        {
            foreach (var clone in held)
                if (clone != null && launched(clone) && !fired.ContainsKey(clone.GetInstanceID())) fired[clone.GetInstanceID()] = Time.realtimeSinceStartup;
            if (fired.Count == 1)
                Assert.AreEqual(2, held.Count(c => c != null && !launched(c)), "Only one clone is fired at a time.");
            yield return null;
        }
        Assert.AreEqual(3, fired.Count, "Every clone is fired in the end.");
        Assert.IsTrue(held.Where(c => c != null).All(c => !c.GetComponent<Collider2D>().isTrigger), "A fired clone can hit.");
        var times = fired.Values.OrderBy(t => t).ToArray();
        Assert.Greater(times[1] - times[0], .1f, "There is a pause between shots.");
        Assert.Greater(times[2] - times[1], .1f, "There is a pause between shots.");
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
        Debug.Log($"[テスト] 寝かせた剣: 位置={p0.transform.position}, 向き={p0.transform.eulerAngles.z:F0}°");

        var cloneType = RuntimeType("SwordCloneProjectile");
        var launchedFlag = cloneType.GetProperty("Launched");
        FireUltimate(p0, "2");
        float giveUp = Time.realtimeSinceStartup + 3f;
        Component[] held = new Component[0];
        while (held.Length < 3 && Time.realtimeSinceStartup < giveUp)
        {
            held = UnityEngine.Object.FindObjectsByType(cloneType, FindObjectsSortMode.None).Cast<Component>().ToArray();
            yield return null;
        }
        // 分身は本体の位置で生まれてから持ち場へ飛び出すので、着くまで待ってから見る
        yield return new WaitForSecondsRealtime(.15f);
        foreach (var clone in held) Debug.Log($"[テスト] 分身の持ち場={clone.transform.position}");
        Assert.AreEqual(3, held.Length, "Three clones come out even when the sword lies on the ground.");
        // 寝ている剣より低い（地面すれすれの）場所には出さない
        foreach (var clone in held)
            Assert.Greater(clone.transform.position.y, p0.transform.position.y, "Clones appear above the sword, not down by the ground.");

        // 撃った直後（0.1秒以内）に壁や床に当たって消えた分身がないか
        var firedAt = new System.Collections.Generic.Dictionary<Component, float>();
        int lostRightAfterLaunch = 0;
        giveUp = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < giveUp)
        {
            foreach (var clone in held)
            {
                if (clone == null)
                    continue;
                if ((bool)launchedFlag.GetValue(clone) && !firedAt.ContainsKey(clone)) firedAt[clone] = Time.realtimeSinceStartup;
            }
            foreach (var pair in firedAt.ToArray())
                if (pair.Key == null && Time.realtimeSinceStartup - pair.Value < .1f) { lostRightAfterLaunch++; firedAt.Remove(pair.Key); }
            if (firedAt.Count + lostRightAfterLaunch >= 3 && held.All(c => c == null || (bool)launchedFlag.GetValue(c))) break;
            yield return null;
        }
        Assert.AreEqual(3, firedAt.Count + lostRightAfterLaunch, "All three clones are fired.");
        Assert.AreEqual(0, lostRightAfterLaunch, "No clone may vanish the moment it is fired.");
    }

    [UnityTest]
    public IEnumerator ClonesAreBornFromTheirMasterAndStepBackBeforeTheyStrike()
    {
        Call("InitializeMultiplayer", Config("clone-familiar", true));
        yield return null; yield return null;
        Call("BeginMultiplayer", "{\"matchId\":\"clone-familiar\"}");
        yield return new WaitForSecondsRealtime(3.2f);
        var p0 = Sword("p0");
        var cloneType = RuntimeType("SwordCloneProjectile");
        var launchedFlag = cloneType.GetProperty("Launched");

        FireUltimate(p0, "2");
        float giveUp = Time.realtimeSinceStartup + 3f;
        Component first = null;
        while (first == null && Time.realtimeSinceStartup < giveUp)
        {
            first = UnityEngine.Object.FindObjectsByType(cloneType, FindObjectsSortMode.None).Cast<Component>().FirstOrDefault();
            yield return null;
        }
        Assert.IsNotNull(first);
        // 見つけた時には既に持ち場へ飛び出し始めているので、分身が覚えている「生まれた場所」で確かめる
        var bornAt = (Vector2)cloneType.GetField("emergeFrom", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(first);
        Assert.Less(Vector2.Distance(bornAt, p0.transform.position), 1f, "A clone is born where its master stands.");

        yield return new WaitForSecondsRealtime(.3f);
        Assert.Greater(Vector2.Distance(first.transform.position, p0.transform.position), 1.5f, "It then moves out to its post.");

        // 撃つ直前に、相手と反対側へ身を引く（身を引いている間だけ、相手から離れていく）
        var target = Sword("p1").transform;
        var windingUp = cloneType.GetField("windingUp", BindingFlags.NonPublic | BindingFlags.Instance);
        float startDistance = -1f, farthest = 0f;
        giveUp = Time.realtimeSinceStartup + 2f;
        while (first != null && !(bool)launchedFlag.GetValue(first) && Time.realtimeSinceStartup < giveUp)
        {
            if ((bool)windingUp.GetValue(first))
            {
                float distance = Vector2.Distance(first.transform.position, target.position);
                if (startDistance < 0f) startDistance = distance;
                farthest = Mathf.Max(farthest, distance);
            }
            yield return new WaitForFixedUpdate();
        }
        Assert.IsTrue(first == null || (bool)launchedFlag.GetValue(first), "The clone is fired.");
        Assert.GreaterOrEqual(startDistance, 0f, "The clone draws back before it is fired.");
        Assert.Greater(farthest - startDistance, .2f, "Drawing back moves the clone away from its target.");
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
        Assert.IsTrue(Field(p0, "isDashing"), "The master is still protected while its clones come out.");

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
            "\"suppressRadius\":1,\"suppressDuration\":1,\"judgmentPullSeconds\":1,\"judgmentPullForce\":1," +
            "\"judgmentDamageMultiplier\":1,\"judgmentKnockback\":1,\"judgmentSweepSeconds\":1,\"judgmentSweepSpin\":1,\"judgmentSweepScale\":1}";
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
        p0.GetType().GetMethod("EndSwordDash").Invoke(p0, null);
        Assert.IsTrue(Field(p0, "isDashing"), "The sword stays protected for the rest of the physics step.");
        Assert.IsFalse(Field(p0, "isDashShooting"));

        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsFalse(Field(p0, "isDashing"));
        Assert.AreEqual(1f, (float)bonus.GetValue(p0), .001f, "An unused bonus must not carry over to the next ordinary hit.");
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
