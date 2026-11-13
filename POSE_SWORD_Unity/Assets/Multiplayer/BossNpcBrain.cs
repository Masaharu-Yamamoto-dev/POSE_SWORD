using UnityEngine;

// ▼【新規追加】1vs3のNPCボスの頭脳。ホストのUnityでだけ、NPCの剣に MultiplayerManager.CreateSword が付ける。
// 入力は人間と同じ SwordBattle.ExecuteMultiplayer* を直接呼ぶ。撃てる・動けるの判定は
// BattlePolicies（ExecuteMultiplayer* の中）に任せ、ここでは「何をしたいか」だけを決める。
// こうしておけば、ルールを変えても人間とNPCで判定が食い違わない。
public class BossNpcBrain : MonoBehaviour
{
    // 判断する間隔（秒）。実際の間隔はこの値に 0.8〜1.2 倍の揺らぎを掛ける（一定の拍だと動きが機械的に見えるため）。
    // ReceiveMultiplayerInput の 40ms 制限を通らないので、連打の上限はこの値で決まる。
    public float decisionInterval = 0.35f;
    // 掌握斬を撃つのに必要な、範囲内のトリオの人数。1人だけのときはSPを溜めたまま待つ。
    public int suppressMinTargets = 2;
    // 必殺技を撃つ距離。ターゲットがこれより遠いときは撃たずに近づく（空振りでSPを捨てないため）。
    public float ultimateRange = 6f;

    MultiplayerManager owner;
    SwordBattle battle;
    SwordController controller;
    float nextDecision;

    public void Init(MultiplayerManager owner, SwordBattle battle)
    {
        this.owner = owner;
        this.battle = battle;
        controller = battle.GetComponent<SwordController>();
        // 開始直後も揺らぎを入れて、カウントダウン明けの1手目が毎回同じ瞬間にならないようにする
        ScheduleNext();
    }

    void Update()
    {
        if (owner == null || battle == null || controller == null) return;
        if (!owner.IsPlaying || !battle.IsAlive) return;
        // 時間の基準は MultiplayerManager（入力間隔・拘束時間）と同じ unscaledTime に揃える
        if (Time.unscaledTime < nextDecision) return;
        ScheduleNext();
        Decide();
    }

    void ScheduleNext()
    {
        nextDecision = Time.unscaledTime + decisionInterval * Random.Range(0.8f, 1.2f);
    }

    void Decide()
    {
        // ターゲットはホストの UpdateTargets が味方を除いて 0.25 秒ごとに選び直している。自前では選ばない。
        Transform target = controller.enemyTarget;

        // 1. 掌握斬：SPが満タンで、範囲に十分な人数がいるときだけ。数え方は ApplySuppress と同じ判定を使う
        if (battle.SuppressReady && owner.CountSuppressTargets(battle.PlayerId) >= suppressMinTargets)
        {
            battle.ExecuteMultiplayerSuppress();
            return;
        }

        if (target == null) return;
        Vector3 self = battle.transform.position;
        Vector3 to = target.position - self;

        // 2. 必殺技：通常の必殺技ライン以上で、満タン未満のとき。満タンは掌握斬のために取っておく
        // （必殺技で使い切ると掌握斬がいつまでも撃てないため）。
        if (battle.currentSp >= battle.ultimateSp && battle.currentSp < battle.maxSp &&
            to.sqrMagnitude <= ultimateRange * ultimateRange)
        {
            battle.ExecuteMultiplayerUltimate();
            return;
        }

        // 3. それ以外はターゲットの方へジャンプして近づく
        battle.ExecuteMultiplayerAction(to.x > 0f);
    }
}
