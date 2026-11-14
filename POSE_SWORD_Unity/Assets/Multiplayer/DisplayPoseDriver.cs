using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

// ▼【新規追加】MultiplayerManager の「表示用の位置」の付け外しを、他のどのスクリプトよりも先に行うための部品。
// MultiplayerManager 自身は(物理の後に裁定するため)実行順が最後(10000)なので、この部品が最初(-10000)に動いて
//   FixedUpdate / Update の先頭：剣を本当の位置(物理・判定が使う位置)へ戻す
//   Update とコルーチンの後、LateUpdate・パーティクル更新の前：剣を表示用の位置(displayDelay 秒前)へ置く
// という順番を作る。MultiplayerManager が自分の GameObject に付ける。
//
// 表示用の位置に置くタイミングは LateUpdate より少し早い PreLateUpdate の先頭にしている。ワールド空間で粒を出すパーティクル
// (剣に付けた軌跡など)は LateUpdate より前(ParticleSystemBeginUpdateAll)に剣の位置を読むため、LateUpdate で置くと
// 粒だけが本当の位置(表示より先の位置)から出てしまう。PlayerLoop に差し込めなかった時は LateUpdate で代用する
[DefaultExecutionOrder(-10000)]
public class DisplayPoseDriver : MonoBehaviour
{
    [HideInInspector] public MultiplayerManager manager;

    static readonly List<DisplayPoseDriver> active = new List<DisplayPoseDriver>();
    static bool hookInstalled;
    // PlayerLoop の差し込みが今フレーム動いたか(動いていれば LateUpdate の代用は要らない)
    bool displayStepDone;

    void OnEnable() { active.Add(this); InstallHook(); }
    void OnDisable() { active.Remove(this); if (active.Count == 0) RemoveHook(); }

    void FixedUpdate() { if (manager != null) manager.RestoreTruePoses(); }
    void Update() { if (manager != null) manager.RestoreTruePoses(); displayStepDone = false; }
    void LateUpdate() { if (!displayStepDone) DisplayStep(); displayStepDone = false; }

    void DisplayStep()
    {
        displayStepDone = true;
        if (manager != null) manager.PlaybackLateUpdate();
    }

    static void RunDisplayStep()
    {
        for (int i = 0; i < active.Count; i++) if (active[i] != null) active[i].DisplayStep();
    }

    static void InstallHook()
    {
        if (hookInstalled) return;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (loop.subSystemList == null) return;
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(PreLateUpdate) || loop.subSystemList[i].subSystemList == null) continue;
            var subs = loop.subSystemList[i].subSystemList.ToList();
            if (subs.All(s => s.type != typeof(DisplayPoseDriver)))
                subs.Insert(0, new PlayerLoopSystem { type = typeof(DisplayPoseDriver), updateDelegate = RunDisplayStep });
            loop.subSystemList[i].subSystemList = subs.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            hookInstalled = true;
            return;
        }
    }

    static void RemoveHook()
    {
        if (!hookInstalled) return;
        hookInstalled = false;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (loop.subSystemList == null) return;
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(PreLateUpdate) || loop.subSystemList[i].subSystemList == null) continue;
            loop.subSystemList[i].subSystemList = loop.subSystemList[i].subSystemList.Where(s => s.type != typeof(DisplayPoseDriver)).ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
    }
}
