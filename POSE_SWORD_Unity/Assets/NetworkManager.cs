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
    public int dashType; // ▼【新規追加】技の種類 (0:通常, 1:小ダッシュ, 2:竜巻, 3:大回転斬り)
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

    [Header("30fps・ラグ対策設定")]
    private float syncInterval = 0.0333f; // 30fps間引き用
    private float syncTimer = 0f;
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

    void Update()
    {
        if (!isHost)
        {
            if (hostSword != null)
            {
                hostSword.transform.position = Vector3.Lerp(hostSword.transform.position, hostTargetPos, Time.deltaTime * 25f);
                hostSword.transform.rotation = Quaternion.Lerp(hostSword.transform.rotation, hostTargetRot, Time.deltaTime * 25f);
            }
            if (clientSword != null)
            {
                clientSword.transform.position = Vector3.Lerp(clientSword.transform.position, clientTargetPos, Time.deltaTime * 25f);
                clientSword.transform.rotation = Quaternion.Lerp(clientSword.transform.rotation, clientTargetRot, Time.deltaTime * 25f);
            }
        }
    }

    void FixedUpdate()
    {
        if (isHost && hostSword != null && clientSword != null)
        {
            syncTimer += Time.fixedDeltaTime;
            if (syncTimer >= syncInterval)
            {
                syncTimer = 0f; 

                SyncMessage sync = new SyncMessage
                {
                    hostSword = GetSyncData(hostSword),
                    clientSword = GetSyncData(clientSword)
                };
                string syncJson = JsonUtility.ToJson(sync);
                SendData("SYNC", syncJson);
            }
        }
    }

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
            centerY = cy,
            dashType = battle != null ? battle.currentDashType : 0 // ▼【新規追加】現在の技番号を送る
        };
    }

    public void ReceiveInput(string jsonString)
    {
        InputMessage msg = JsonUtility.FromJson<InputMessage>(jsonString);
        GameObject enemyObj = isHost ? clientSword : hostSword;
        
        if (enemyObj != null)
        {
            SwordBattle battle = enemyObj.GetComponent<SwordBattle>();
            if (battle != null)
            {
                battle.ExecuteRemoteAction(msg.action);
            }
        }
    }

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

        Vector3 nextPos = new Vector3(data.x, data.y, obj.transform.position.z);
        Quaternion nextRot = Quaternion.Euler(0, 0, data.rotation);

        if (isFirstSync)
        {
            obj.transform.position = nextPos;
            obj.transform.rotation = nextRot;
            hostTargetPos = nextPos;
            clientTargetPos = nextPos;
            isFirstSync = false;
        }

        if (obj == hostSword)
        {
            hostTargetPos = nextPos;
            hostTargetRot = nextRot;
        }
        else if (obj == clientSword)
        {
            clientTargetPos = nextPos;
            clientTargetRot = nextRot;
        }

        SwordBattle battle = obj.GetComponent<SwordBattle>();
        if (battle != null)
        {
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
                battle.UpdateUI(); 
            }

            battle.currentSp = data.sp;
            battle.isDashing = data.isDashing;

            // ▼【大幅修正】一律で赤にするのをやめ、技番号（dashType）に応じてホストと完全に同じエフェクト色を再現！
            Transform blade = obj.transform.Find("Blade");
            if (blade != null)
            {
                SpriteRenderer sr = blade.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    switch (data.dashType)
                    {
                        case 1: // 独楽：通常小ダッシュ
                            sr.color = new Color(1f, 0.5f, 0.5f); // 薄赤色
                            break;
                        case 2: // 独楽：超必殺・竜巻
                            sr.color = new Color(1f, 0.8f, 0.2f); // オレンジ・金色
                            break;
                        case 3: // 剣：大回転斬りダッシュ
                            sr.color = new Color(0.5f, 1f, 1f);   // 水色
                            break;
                        default: // 通常状態
                            sr.color = Color.white;
                            break;
                    }
                }
            }
        }
    }

    public void ResetMatch(string emptyMessage)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
        Time.timeScale = 1f; 
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }

    public void SetGameMode(string modeStr)
    {
        SwordController.isKomaMode = (modeStr == "1");

        if (hostSword != null) hostSword.GetComponent<SwordController>().ApplyPhysicsMode();
        if (clientSword != null) clientSword.GetComponent<SwordController>().ApplyPhysicsMode();
        
        if (swordStage != null) swordStage.SetActive(!SwordController.isKomaMode);
        if (komaStage != null) komaStage.SetActive(SwordController.isKomaMode);
    }
}