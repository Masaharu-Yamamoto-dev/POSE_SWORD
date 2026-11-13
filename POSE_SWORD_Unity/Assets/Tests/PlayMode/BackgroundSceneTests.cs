using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// 背景（左右の色の板）のテスト。本番のReactのように、場面を読み込んだ直後（最初のフレームより前）に
// オンライン対戦の準備が届いても、右側の色がグラデーションの絵になっているかを見る
public class BackgroundSceneTests
{
    [UnityTest]
    public IEnumerator BackgroundKeepsItsGradientWhenTheMatchIsSetUpBeforeTheFirstFrame()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene");
        var gameManager = GameObject.Find("GameManager");
        var harness = gameManager.GetComponent(Type.GetType("MultiplayerLocalTestHarness, Assembly-CSharp"));
        if (harness != null) UnityEngine.Object.DestroyImmediate(harness);
        var manager = gameManager.GetComponent(Type.GetType("MultiplayerManager, Assembly-CSharp"));
        manager.GetType().GetMethod("InitializeMultiplayer").Invoke(manager, new object[] { Config() });
        yield return null; yield return null;

        var right = GameObject.Find("RightColor");
        var sprite = right.GetComponent<SpriteRenderer>().sprite;
        Assert.IsNotNull(sprite);
        Assert.AreEqual(1024, sprite.texture.width, "The right colour uses the half-gradient image, not the plain square that covers the whole screen.");
        float baseAngle = (float)Type.GetType("BackgroundManager, Assembly-CSharp").GetField("baseAngle").GetValue(
            UnityEngine.Object.FindFirstObjectByType(Type.GetType("BackgroundManager, Assembly-CSharp")));
        Assert.AreEqual(baseAngle, right.transform.eulerAngles.z, .01f, "The boundary is slanted from the start.");
    }

    static string Config()
    {
        var texture = new Texture2D(4, 4);
        string image = Convert.ToBase64String(texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
        string players = "";
        for (int i = 0; i < 2; i++)
            players += (i > 0 ? "," : "") + "{\"playerId\":\"p" + i + "\",\"slotIndex\":" + i + ",\"spawnIndex\":" + i +
                ",\"swordData\":{\"name\":\"Test\",\"hp\":1000,\"attack\":1,\"weight\":50,\"imageStr\":\"" + image + "\"}}";
        return "{\"matchId\":\"bg\",\"localPlayerId\":\"p0\",\"isHost\":true,\"gameMode\":\"0\",\"players\":[" + players + "]}";
    }
}
