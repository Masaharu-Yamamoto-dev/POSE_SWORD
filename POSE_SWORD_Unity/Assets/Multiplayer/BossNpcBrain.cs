using System.Linq;
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
    // 掌握斬を撃つのに必要な、範囲内のトリオの人数。足りないあいだはSPを溜めたまま待つ。
    // 生き残りのトリオがこれより少ないときは、生き残りの人数まで下げて判断する（Decide 参照）。
    public int suppressMinTargets = 2;
    // 必殺技を撃つ距離。ターゲットがこれより遠いときは撃たずに近づく（空振りでSPを捨てないため）。
    public float ultimateRange = 6f;

    [Header("上下の判断")]
    // ターゲットがこれより下にいたら「下にいる」とみなす（ワールド座標の単位）。
    // 小さすぎると同じ高さの相手でも揺れで「下」と判断してジャンプを控えてしまう。
    public float belowThreshold = 1.5f;
    // この速さより遅ければ「止まっている（足場の上にいる）」とみなす（units/秒）
    public float restingSpeed = 0.5f;
    // 下にターゲットがいて、この秒数止まったままなら、足場の上で動けなくなっているとみなして跳び降りる
    public float ledgeWaitTime = 0.5f;

    MultiplayerManager owner;
    SwordBattle battle;
    SwordController controller;
    Rigidbody2D body;
    float nextDecision;
    // 止まり始めた時刻。動いているあいだは -1
    float restingSince = -1f;

    public void Init(MultiplayerManager owner, SwordBattle battle)
    {
        this.owner = owner;
        this.battle = battle;
        controller = battle.GetComponent<SwordController>();
        body = battle.GetComponent<Rigidbody2D>();
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

        // 1. 掌握斬：SPが満タンで、範囲に十分な人数がいるときだけ。数え方は ApplySuppress と同じ判定を使う。
        // 必要人数は「設定値」と「生き残りのトリオの人数」の小さい方にする。生き残りが設定値より少ないと
        // 条件を満たす日が来ず、満タンのSPを抱えたまま掌握斬を撃てなくなるため。
        // suppressMinTargets 自体は書き換えない（Inspector の調整値が試合中に変わってしまうため）。
        // 下限の1は、トリオが全滅した瞬間に「0人で可」となって空撃ちしないため。
        int aliveOpponents = owner.OpponentSwordsOf(battle.PlayerId).Count();
        int minTargets = Mathf.Max(1, Mathf.Min(suppressMinTargets, aliveOpponents));
        if (battle.SuppressReady && owner.CountSuppressTargets(battle.PlayerId) >= minTargets)
        {
            battle.ExecuteMultiplayerSuppress();
            return;
        }

        UpdateResting();
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

        // 3. それ以外はターゲットの方へ移動する
        Move(to);
    }

    // 移動の判断。ジャンプは横向きの力と上向きの力が一緒に掛かるので、高さの関係で使い分ける。
    // ・同じ高さ・上にいる：ターゲットの方へ跳ぶ（上にいる相手には、跳ぶこと自体が近づく手段になる）
    // ・下にいる：跳ぶと離れてしまうので、落ちているあいだは何もせず重力で近づく。
    //   ただし足場の上で止まったまま ledgeWaitTime 経ったら、待っても落ちないので相手の方へ跳んで足場から下りる
    void Move(Vector3 to)
    {
        bool targetBelow = to.y < -belowThreshold;
        if (targetBelow && !StuckOnLedge()) return;
        battle.ExecuteMultiplayerAction(to.x > 0f);
    }

    // 判断のたびに、止まっているかどうかを記録する。止まり続けている時間で「足場の上で動けない」を見分ける
    void UpdateResting()
    {
        bool resting = body != null && body.linearVelocity.sqrMagnitude < restingSpeed * restingSpeed;
        if (!resting) restingSince = -1f;
        else if (restingSince < 0f) restingSince = Time.unscaledTime;
    }

    bool StuckOnLedge()
    {
        return restingSince >= 0f && Time.unscaledTime - restingSince >= ledgeWaitTime;
    }
}
