using UnityEngine;
using UnityEngine.SceneManagement;
using System.Runtime.InteropServices;

[System.Serializable]
public class SwordSyncData {
    public float x;
    public float y;
    public float rotation;
    public int hp;
    public bool isDashing;
    public float sp;
    public float centerX;
    public float centerY;
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

    [Header("ステージ設定")]
    public GameObject swordStage;
    public GameObject komaStage;

    [DllImport("__Internal")]
    private static extern void SendToReact(string type, string jsonString);

    [Header("ネットワーク設定")]
    public bool isHost = true;
    public GameObject hostSword;
    public GameObject clientSword;

    // SYNC送信の間引き（20fps）
    private float syncTimer = 0f;
    public float syncInterval = 0.05f;

    // CLIENT補間用ターゲット
    private Vector3 hostTargetPos;
    private Quaternion hostTargetRot;
    private Vector3 clientTargetPos;
    private Quaternion clientTargetRot;
    private bool isFirstSync = true;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ApplyModeSettings();
    }

    public void SetHostMode(int isHostInt)
    {
        isHost = (isHostInt == 1);
        ApplyModeSettings();
        Debug.Log($"🎮 モード設定適用: isHost={isHost}");
    }

    public void ApplyModeSettings()
    {
        if (isHost)
        {
            if (hostSword != null) hostSword.GetComponent<SwordController>().isLocalControlled = true;
            if (clientSword != null) clientSword.GetComponent<SwordController>().isLocalControlled = false;
        }
        else
        {
            if (hostSword != null)
            {
                hostSword.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                hostSword.GetComponent<SwordController>().isLocalControlled = false;
            }
            if (clientSword != null)
            {
                clientSword.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
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

    // HOST：タイマーで間引き（秒間20回）
    void FixedUpdate()
    {
        if (!isHost || hostSword == null || clientSword == null) return;

        syncTimer += Time.fixedDeltaTime;
        if (syncTimer < syncInterval) return;
        syncTimer -= syncInterval;

        SyncMessage sync = new SyncMessage
        {
            hostSword = GetSyncData(hostSword),
            clientSword = GetSyncData(clientSword)
        };
        SendData("SYNC", JsonUtility.ToJson(sync));
    }

    // CLIENT：毎フレームLerpで滑らかにターゲットへ追従
    void Update()
    {
        if (isHost || !hasSyncTarget) return;

        float t = Time.deltaTime * 25f;
        if (hostSword != null)
        {
            hostSword.transform.position = Vector3.Lerp(hostSword.transform.position, hostTargetPos, t);
            hostSword.transform.rotation = Quaternion.Lerp(hostSword.transform.rotation, hostTargetRot, t);
        }
        if (clientSword != null)
        {
            clientSword.transform.position = Vector3.Lerp(clientSword.transform.position, clientTargetPos, t);
            clientSword.transform.rotation = Quaternion.Lerp(clientSword.transform.rotation, clientTargetRot, t);
        }
    }

    private bool hasSyncTarget = false;

    SwordSyncData GetSyncData(GameObject obj)
    {
        SwordBattle battle = obj.GetComponent<SwordBattle>();

        float cx = obj.transform.position.x;
        float cy = obj.transform.position.y;
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
            centerX = cx,
            centerY = cy
        };
    }

    public void ReceiveInput(string jsonString)
    {
        Debug.Log($"🎮 ReceiveInput: {jsonString}");
        InputMessage msg = JsonUtility.FromJson<InputMessage>(jsonString);

        GameObject enemyObj = isHost ? clientSword : hostSword;
        if (enemyObj != null)
        {
            SwordBattle battle = enemyObj.GetComponent<SwordBattle>();
            if (battle != null)
            {
                battle.ExecuteRemoteAction(msg.action);
                Debug.Log($"✅ 相手の剣がアクション（{msg.action}）を実行しました！");
            }
        }
    }

    // Debug.Logを削除（WebGLで毎フレームconsole.logするとフレームが激落ちする）
    public void SyncTransform(string jsonString)
    {
        if (isHost) return;

        SyncMessage sync = JsonUtility.FromJson<SyncMessage>(jsonString);

        // 初回のみ瞬時にスナップ（ゲーム開始時のワープ感を消す）
        if (isFirstSync)
        {
            if (hostSword != null)
            {
                hostSword.transform.position = new Vector3(sync.hostSword.x, sync.hostSword.y, hostSword.transform.position.z);
                hostSword.transform.rotation = Quaternion.Euler(0, 0, sync.hostSword.rotation);
                hostTargetPos = hostSword.transform.position;
                hostTargetRot = hostSword.transform.rotation;
            }
            if (clientSword != null)
            {
                clientSword.transform.position = new Vector3(sync.clientSword.x, sync.clientSword.y, clientSword.transform.position.z);
                clientSword.transform.rotation = Quaternion.Euler(0, 0, sync.clientSword.rotation);
                clientTargetPos = clientSword.transform.position;
                clientTargetRot = clientSword.transform.rotation;
            }
            isFirstSync = false;
        }

        // 座標・回転はターゲットを更新するだけ（実際の移動はUpdate()のLerpが担当）
        if (hostSword != null)
            hostTargetPos = new Vector3(sync.hostSword.x, sync.hostSword.y, hostSword.transform.position.z);
        if (clientSword != null)
            clientTargetPos = new Vector3(sync.clientSword.x, sync.clientSword.y, clientSword.transform.position.z);

        hostTargetRot = Quaternion.Euler(0, 0, sync.hostSword.rotation);
        clientTargetRot = Quaternion.Euler(0, 0, sync.clientSword.rotation);
        hasSyncTarget = true;

        // HP・SP・状態は即時反映
        ApplyNonPositionSync(hostSword, sync.hostSword);
        ApplyNonPositionSync(clientSword, sync.clientSword);
    }

    void ApplyNonPositionSync(GameObject obj, SwordSyncData data)
    {
        if (obj == null) return;

        SwordBattle battle = obj.GetComponent<SwordBattle>();
        if (battle == null) return;

        if (!isHost)
            battle.currentCenterPosition = new Vector3(data.centerX, data.centerY, obj.transform.position.z);

        if (battle.hp > data.hp)
            battle.PlayClientDamageEffect(battle.hp - data.hp);

        if (battle.hp != data.hp)
        {
            battle.hp = data.hp;
            battle.UpdateUI();
        }

        battle.currentSp = data.sp;
        battle.isDashing = data.isDashing;

        Transform blade = obj.transform.Find("Blade");
        if (blade != null)
        {
            SpriteRenderer sr = blade.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = data.isDashing ? new Color(1f, 0.5f, 0.5f) : Color.white;
        }
    }

    public void ResetMatch(string emptyMessage)
    {
        Debug.Log("🔄 Webからの指示でシーンをリセットします！");
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void SetGameMode(string modeStr)
    {
        SwordController.isKomaMode = (modeStr == "1");

        if (hostSword != null) hostSword.GetComponent<SwordController>().ApplyPhysicsMode();
        if (clientSword != null) clientSword.GetComponent<SwordController>().ApplyPhysicsMode();

        if (swordStage != null) swordStage.SetActive(!SwordController.isKomaMode);
        if (komaStage != null) komaStage.SetActive(SwordController.isKomaMode);

        Debug.Log(SwordController.isKomaMode ? "🌀 独楽モード・ステージへ移行" : "⚔️ 剣モード・ステージへ移行");
    }
}
