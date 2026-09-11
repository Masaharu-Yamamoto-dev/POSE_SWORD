using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Covers the editor-only driver that makes the four-player match playable from
// Unity's Play button alone: it must build the roster, generate usable swords
// from its own procedural images, run the real MP_* handshake, and let bots fight.
public class LocalFourPlayerDriverTests
{
    private Component driver;
    private Component manager;
    private static Type RuntimeType(string name) { return Type.GetType(name + ", Assembly-CSharp", true); }
    private T Field<T>(Component target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
    }

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene");
        var gameManager = GameObject.Find("GameManager");
        driver = gameManager.GetComponent(RuntimeType("LocalFourPlayerDriver"));
        Assert.IsNotNull(driver, "The scene must carry the driver so pressing Play starts a match.");
        manager = gameManager.GetComponent(RuntimeType("MultiplayerManager"));
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (driver != null) UnityEngine.Object.Destroy(driver);
        if (manager != null) UnityEngine.Object.Destroy(manager);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PressingPlayBuildsFourSwordsAndReachesPlayingWithoutReact()
    {
        Assert.IsTrue(Field<bool>(driver, "autoStart"), "autoStart must be on so Play alone is enough.");
        Assert.AreEqual(4, Field<int>(driver, "playerCount"));
        Assert.IsFalse(Field<bool>(driver, "enableBots"),
            "Bots must ship off: in sword mode every bot input applies spinTorque, so the swords would spin unprompted.");

        // Start() waits one frame, InitializeMultiplayer's coroutine waits another.
        yield return null; yield return null; yield return null;

        var arena = GameObject.Find("MultiplayerArena");
        Assert.IsNotNull(arena, "The driver must initialize a match on its own.");
        var swords = arena.GetComponentsInChildren(RuntimeType("SwordBattle"));
        Assert.AreEqual(4, swords.Length);
        foreach (string id in new[] { "P1", "P2", "P3", "P4" }) Assert.IsNotNull(GameObject.Find("Sword_" + id), id);

        // The blades must come from the project's own cutout art, not from anything
        // this driver invents, and must survive SwordGenerator into a real collider.
        var images = Field<Texture2D[]>(driver, "swordImages");
        Assert.AreEqual(4, images.Length, "the scene must supply one existing image per player");
        CollectionAssert.AllItemsAreNotNull(images);

        // Sword_P1/P3 use sampleA.png (291x438), Sword_P2/P4 use sampleB.png (179x267).
        var expected = new[] { new Vector2Int(291, 438), new Vector2Int(179, 267) };
        for (int i = 0; i < swords.Length; i++)
        {
            var sword = swords[i];
            var blade = sword.transform.Find("Blade");
            var sprite = blade.GetComponent<SpriteRenderer>().sprite;
            Assert.IsNotNull(sprite, "each sword needs a generated sprite");

            var size = new Vector2Int(sprite.texture.width, sprite.texture.height);
            Assert.AreEqual(expected[i % 2], size,
                $"{sword.name} must render the existing Assets/Sword cutout, not a substitute");
            var collider = blade.GetComponent<PolygonCollider2D>();
            Assert.IsNotNull(collider, "each blade needs a regenerated collider");
            Assert.Greater(collider.GetTotalPointCount(), 2, "the blade outline must be a real polygon");
            Assert.AreEqual(300, (int)sword.GetType().GetField("hp").GetValue(sword));

            // These cutouts land near 4.5 world units tall. An over-long blade would
            // hit MultiplayerManager's 8-unit clamp and distort reach and damage.
            float height = blade.GetComponent<SpriteRenderer>().bounds.size.y;
            Assert.That(height, Is.InRange(3.5f, 6f), "blade must be proportioned like the source cutout");

            // The handle art rides along with the cloned template; it must not go missing.
            var handle = sword.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(r => r.transform != blade && r.sprite != null);
            Assert.IsNotNull(handle, $"{sword.name} lost its sword_handle sprite");
        }

        // The driver answers MP_INITIALIZED with BeginMultiplayer, exactly as React does.
        yield return new WaitForSecondsRealtime(3.4f);
        Assert.IsTrue((bool)manager.GetType().GetProperty("IsPlaying").GetValue(manager),
            "The driver must carry the match through the countdown into PLAYING.");
        Assert.IsNotNull(Field<object>(driver, "latestSync"), "MP_SYNC must reach the driver's HUD state.");
    }

    [UnityTest]
    public IEnumerator BotsFightAnUnattendedMatchThroughToADecidedResult()
    {
        // Bots ship off so sword mode stays still until the player acts; this test turns them on.
        driver.GetType().GetField("enableBots").SetValue(driver, true);
        yield return null; yield return null; yield return null;

        float startDeadline = Time.realtimeSinceStartup + 10f;
        while (!(bool)manager.GetType().GetProperty("IsPlaying").GetValue(manager) &&
               Time.realtimeSinceStartup < startDeadline) yield return null;
        Assert.IsTrue((bool)manager.GetType().GetProperty("IsPlaying").GetValue(manager), "match never started");

        // Nobody touches the keyboard: the bots alone have to carry the match to an end.
        float deadline = Time.realtimeSinceStartup + 90f;
        while (Field<object>(driver, "latestResult") == null && Time.realtimeSinceStartup < deadline) yield return null;

        var result = Field<object>(driver, "latestResult");
        Assert.IsNotNull(result, "MP_RESULT must reach the driver so Play mode can show standings.");

        var standings = (Array)result.GetType().GetField("standings").GetValue(result);
        Assert.AreEqual(4, standings.Length, "every player must be ranked");
        foreach (var score in standings)
        {
            int rank = (int)score.GetType().GetField("rank").GetValue(score);
            Assert.That(rank, Is.InRange(1, 4), "rank must be a real placing");
        }

        bool draw = (bool)result.GetType().GetField("draw").GetValue(result);
        string winner = (string)result.GetType().GetField("winnerId").GetValue(result);
        Assert.IsTrue(draw || !string.IsNullOrEmpty(winner), "a finished match is either a draw or has a winner");

        // The manager must have frozen the match rather than leaving physics running.
        Assert.IsFalse((bool)manager.GetType().GetProperty("IsPlaying").GetValue(manager));
    }
}
