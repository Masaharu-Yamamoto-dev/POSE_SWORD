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

    [DllImport("__Internal")]
    private static extern void SendToWeb(string type, string jsonString);

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
            // 【Clientモード】操作権限を奪い、物理演算を無効化（パラパラ漫画状態にする）
            if (hostSword != null)
            {
                hostSword.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                hostSword.GetComponent<SwordController>().isLocalControlled = false;
            }
            if (clientSword != null)
            {
                clientSword.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                clientSword.GetComponent<SwordController>().isLocalControlled = false;
            }
            Debug.Log("🌐 Clientモードで起動：物理演算を停止し、受信待機します");
        }
    }

    public void SendData(string type, string jsonString)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
            SendToWeb(type, jsonString);
        #endif
    }

    // --- Client側：画面タップをHostへ送信 ---
    void Update()
    {
        // Clientの時だけ、画面クリックを検知してWeb(Host)へINPUTを送る
        if (!isHost && Input.GetMouseButtonDown(0))
        {
            InputMessage msg = new InputMessage();
            SendData("INPUT", JsonUtility.ToJson(msg));
        }
    }

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
            SendData("SYNC", JsonUtility.ToJson(sync));
        }
    }

    SwordSyncData GetSyncData(GameObject obj)
    {
        SwordBattle battle = obj.GetComponent<SwordBattle>();
        return new SwordSyncData
        {
            x = obj.transform.position.x,
            y = obj.transform.position.y,
            rotation = obj.transform.eulerAngles.z,
            hp = battle != null ? battle.hp : 100
        };
    }

    // ========================================================
    // ▼ WebのJS側から SendMessage で呼ばれる受信関数たち
    // ========================================================

    // 【Host専用】 Web仕様書5：Clientからの操作入力を受信
    public void ReceiveInput(string jsonString)
    {
        if (!isHost) return; 
        if (clientSword != null)
        {
            // 受信した瞬間に、Clientの剣をジャンプさせる！
            clientSword.GetComponent<SwordController>().NetworkJump();
        }
    }

    // 【Client専用】 Web仕様書5：Hostからの座標とHPを受信
    public void SyncTransform(string jsonString)
    {
        if (isHost) return; 

        SyncMessage sync = JsonUtility.FromJson<SyncMessage>(jsonString);
        ApplySyncToGameObject(hostSword, sync.hostSword);
        ApplySyncToGameObject(clientSword, sync.clientSword);
    }

    void ApplySyncToGameObject(GameObject obj, SwordSyncData data)
    {
        if (obj == null) return;

        // Hostから送られてきた座標と回転を強制上書き
        obj.transform.position = new Vector3(data.x, data.y, obj.transform.position.z);
        obj.transform.rotation = Quaternion.Euler(0, 0, data.rotation);

        // HPの更新とUIの反映
        SwordBattle battle = obj.GetComponent<SwordBattle>();
        if (battle != null && battle.hp != data.hp)
        {
            battle.hp = data.hp;
            battle.UpdateUI();
        }
    }
    public void ResetMatch(string emptyMessage)
    {
        Debug.Log("🔄 Webからの指示でシーンをリセットします！");
        
        // 止まっていた時間を元に戻す
        Time.timeScale = 1f; 
        
        // 今のシーンを最初から読み込み直す（一瞬で初期状態に戻ります）
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }
}