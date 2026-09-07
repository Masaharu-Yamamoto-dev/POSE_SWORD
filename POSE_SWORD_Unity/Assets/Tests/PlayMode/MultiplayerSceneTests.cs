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

    private string Config(string id, bool host)
    {
        var texture = new Texture2D(4, 4);
        texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); texture.Apply();
        string image = Convert.ToBase64String(texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
        var players = Enumerable.Range(0, 4).Select(i => "{\"playerId\":\"p" + i + "\",\"slotIndex\":" + i +
            ",\"spawnIndex\":" + i + ",\"swordData\":{\"name\":\"Test\",\"hp\":1000,\"attack\":1,\"weight\":50,\"imageStr\":\"" + image + "\"}}");
        return "{\"matchId\":\"" + id + "\",\"localPlayerId\":\"p" + (host ? "0" : "2") +
            "\",\"isHost\":" + (host ? "true" : "false") + ",\"gameMode\":\"0\",\"players\":[" + string.Join(",", players) + "]}";
    }

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene");
        yield return null;
        manager = GameObject.Find("GameManager").GetComponent(RuntimeType("MultiplayerManager"));
        Assert.IsNotNull(manager);
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
        Assert.IsFalse(hostSword.GetComponent<Rigidbody2D>().simulated);
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
        Assert.AreEqual("Sword_p2", controllers.Single(c => (bool)c.GetType().GetField("isLocalControlled").GetValue(c)).name);
        Assert.IsTrue(controllers.All(c => !c.GetComponent<Rigidbody2D>().simulated));
    }
}
