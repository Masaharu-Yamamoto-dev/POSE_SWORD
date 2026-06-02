using UnityEngine;
using UnityEngine.SceneManagement;
using System.Runtime.InteropServices;

// ▼ Web側の仕様書に完全一致させたJSONデータ構造


[System.Serializable]
public class SwordSyncData {
    public float x;
    public float y;
    public float rotation;
    public int hp;
    public bool isDashing;
    public float sp;
    public float centerX; // ▼【新規追加】本物の中心X
    public float centerY; // ▼【新規追加】本物の中心Y
}

[System.Serializable]
public class SyncMessage {
    public string type = "SYNC";
    public SwordSyncData hostSword;
    public SwordSyncData clientSword;
}

[System.Serializable]
public class InputMessage {
    public string type = "INPUT";
    public string action = "SWIPE";
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;
    // NetworkManager.cs の上部（変数を宣言している場所）に追加
    [Header("ステージ設定")]
    public GameObject swordStage; // 剣モード用のステージ（背景や床）
    public GameObject komaStage;  // 独楽モード用のステージ（背景や四方の壁）
    [DllImport("__Internal")]
private static extern void SendToReact(string type, string jsonString);
    [Header("ネットワーク設定")]
    public bool isHost = true;         // Web側から試合開始時に指定される
    public GameObject hostSword;       // 左側：PlayerSword
    public GameObject clientSword;     // 右側：EnemyDummy

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ApplyModeSettings();
    }

    // --- 【Webからの呼び出し用】Host/Clientの決定 ---
    // Web担当者様: window.unityInstance.SendMessage('GameManager', 'SetHostMode', 1); (1=Host, 0=Client)
    public void SetHostMode(int isHostInt)
    {
        isHost = (isHostInt == 1);
        ApplyModeSettings();
    }

    // HostとClientで物理演算と操作権限を切り替える
    // HostとClientで物理演算と操作権限を切り替える
    public void ApplyModeSettings()
    {
        if (isHost)
        {
            // 【Hostモード】自分(Host)が操作し、物理演算はすべてオン
            if (hostSword != null) hostSword.GetComponent<SwordController>().isLocalControlled = true;
            if (clientSword != null) clientSword.GetComponent<SwordController>().isLocalControlled = false;
        }
        else
        {
            // 【Clientモード】
            if (hostSword != null)
            {
                hostSword.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                hostSword.GetComponent<SwordController>().isLocalControlled = false;
            }
            if (clientSword != null)
            {
                clientSword.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                
                // ▼【ココを修正！】Client側も、自分の剣は操作できるように「true」にする！
                clientSword.GetComponent<SwordController>().isLocalControlled = true;
            }
            Debug.Log("🌐 Clientモードで起動：物理演算を停止し、操作と受信待機します");
        }
    }

    public void SendData(string type, string jsonString)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
            SendToReact(type, jsonString);
        #endif
    }

    // --- Client側：画面タップをHostへ送信 ---
    // void Update()
    // {
    //     // Clientの時だけ、画面クリックを検知してWeb(Host)へINPUTを送る
    //     if (!isHost && Input.GetMouseButtonDown(0))
    //     {
    //         InputMessage msg = new InputMessage();
    //         SendData("INPUT", JsonUtility.ToJson(msg));
    //     }
    // }

    // --- Host側：毎フレーム座標をClientへ送信 ---
    void FixedUpdate()
    {
        if (isHost && hostSword != null && clientSword != null)
        {
            SyncMessage sync = new SyncMessage
            {
                hostSword = GetSyncData(hostSword),
                clientSword = GetSyncData(clientSword)
            };
            string syncJson = JsonUtility.ToJson(sync);
            SendData("SYNC", syncJson);
            
            // 【デバッグ】毎100フレームおきに同期ログを出力
            if (Time.frameCount % 100 == 0)
            {
                Debug.Log($"📡 SYNC送信: Host HP={sync.hostSword.hp}, Client HP={sync.clientSword.hp}");
            }
        }
    }

    SwordSyncData GetSyncData(GameObject obj)
    {
        SwordBattle battle = obj.GetComponent<SwordBattle>();
        
        // デフォルトは通常の座標
        float cx = obj.transform.position.x;
        float cy = obj.transform.position.y;

        // 独楽モードなら、SwordBattleが計算した「ブレない本当の中心」を載せる
        if (battle != null)
        {
            cx = battle.currentCenterPosition.x;
            cy = battle.currentCenterPosition.y;
        }

        return new SwordSyncData
        {
            x = obj.transform.position.x,
            y = obj.transform.position.y,
            rotation = obj.transform.eulerAngles.z,
            hp = battle != null ? battle.hp : 100,
            isDashing = battle != null ? battle.isDashing : false,
            sp = battle != null ? battle.currentSp : 0f,
            centerX = cx, // ▼【新規追加】
            centerY = cy  // ▼【新規追加】
        };
    }

    // ========================================================
    // ▼ WebのJS側から SendMessage で呼ばれる受信関数たち
    // ========================================================

    // 【Host専用】 Web仕様書5：Clientからの操作入力を受信
    // 【Host専用】 Web仕様書5：Clientからの操作入力を受信
    // ▼【変更】HostもClientも、相手のアクションを受信して強制発動させる
    public void ReceiveInput(string jsonString)
    {
        Debug.Log($"🎮 ReceiveInput: {jsonString}");
        InputMessage msg = JsonUtility.FromJson<InputMessage>(jsonString);
        
        // 自分がHostなら相手はClient剣、自分がClientなら相手はHost剣
        GameObject enemyObj = isHost ? clientSword : hostSword;
        
        if (enemyObj != null)
        {
            SwordBattle battle = enemyObj.GetComponent<SwordBattle>();
            if (battle != null)
            {
                // 送られてきたアクション名（JumpRightやTornado等）を渡して強制発動！
                battle.ExecuteRemoteAction(msg.action);
                Debug.Log($"✅ 相手の剣がアクション（{msg.action}）を実行しました！");
            }
        }
    }

    // 【Client専用】 Web仕様書5：Hostからの座標とHPを受信
    public void SyncTransform(string jsonString)
    {
        if (isHost) return; 
        Debug.Log($"🔄 SyncTransform受信: {jsonString}");

        SyncMessage sync = JsonUtility.FromJson<SyncMessage>(jsonString);
        ApplySyncToGameObject(hostSword, sync.hostSword);
        ApplySyncToGameObject(clientSword, sync.clientSword);
        
        Debug.Log($"✅ 同期完了: Host HP={sync.hostSword.hp}, Client HP={sync.clientSword.hp}");
    }

    // ▼【修正】NetworkManager.cs の ApplySyncToGameObject
    void ApplySyncToGameObject(GameObject obj, SwordSyncData data)
    {
        if (obj == null) return;

        obj.transform.position = new Vector3(data.x, data.y, obj.transform.position.z);
        obj.transform.rotation = Quaternion.Euler(0, 0, data.rotation);

        SwordBattle battle = obj.GetComponent<SwordBattle>();
        if (battle != null)
        {
            // ▼【新規追加】Client側：Hostから送られてきた「ブレない本当の中心座標」を同期する
            if (!isHost)
            {
                battle.currentCenterPosition = new Vector3(data.centerX, data.centerY, obj.transform.position.z);
            }
            if (battle.hp > data.hp)
            {
                int damageTaken = battle.hp - data.hp;
                battle.PlayClientDamageEffect(damageTaken);
            }

            if (battle.hp != data.hp)
            {
                battle.hp = data.hp;
                battle.UpdateUI(); // HPバーの更新
            }

            // ▼【追加】SPゲージの同期！
            battle.currentSp = data.sp;

            battle.isDashing = data.isDashing;
            Transform blade = obj.transform.Find("Blade");
            if (blade != null)
            {
                SpriteRenderer sr = blade.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = data.isDashing ? new Color(1f, 0.5f, 0.5f) : Color.white;
                }
            }
        }
    }
    public void ResetMatch(string emptyMessage)
    {
        Debug.Log("🔄 Webからの指示でシーンをリセットします！");
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
        // 止まっていた時間を元に戻す
        Time.timeScale = 1f; 
        
        // 今のシーンを最初から読み込み直す（一瞬で初期状態に戻ります）
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }
    // NetworkManager.cs

    // ▼【追加】Webから呼ばれるモード切り替え
    // Web担当者様: sendMessage('NetworkManager', 'SetGameMode', '1'); (1=独楽, 0=剣)
    public void SetGameMode(string modeStr)
    {
        SwordController.isKomaMode = (modeStr == "1");

        // 両方の剣の物理演算設定を切り替える
        if (hostSword != null) hostSword.GetComponent<SwordController>().ApplyPhysicsMode();
        if (clientSword != null) clientSword.GetComponent<SwordController>().ApplyPhysicsMode();
        
        // ▼【新規追加】ステージの表示・非表示を切り替える！
        if (swordStage != null) swordStage.SetActive(!SwordController.isKomaMode);
        if (komaStage != null) komaStage.SetActive(SwordController.isKomaMode);

        Debug.Log(SwordController.isKomaMode ? "🌀 独楽モード・ステージへ移行" : "⚔️ 剣モード・ステージへ移行");
    }
}