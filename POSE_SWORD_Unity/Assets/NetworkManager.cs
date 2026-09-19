using System;
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
    public float centerX;
    public float centerY;
    public int dashType; // ▼ 技の種類 (0:通常, 1:小ダッシュ, 2:竜巻, 3:大回転斬り)
}

[System.Serializable]
public class SyncMessage {
    public string type = "SYNC";
    public SwordSyncData[] swords; // ▼ インデックス = プレイヤーID(0〜playerCount-1)
}

[System.Serializable]
public class InputMessage {
    public string type = "INPUT";
    public string action = "SWIPE";
    public int playerIndex; // ▼ どのプレイヤーの操作かをホストが判別するために必要
}

[System.Serializable]
public class PlayerInfoData {
    public int myIndex;
    public int playerCount;
    public int isHost; // 1 or 0
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;
    public const int MaxPlayers = 4;

    [Header("ステージ設定")]
    public GameObject swordStage;
    public GameObject komaStage;
    [Tooltip("剣モードの3・4人戦専用ステージ(任意)。割り当てておくと、3人以上の剣モード対戦の時だけ" +
        "swordStageの代わりにこちらが表示される。未設定ならこれまで通りswordStageが常に使われる。")]
    public GameObject swordStage3P;

    [DllImport("__Internal")]
    private static extern void SendToReact(string type, string jsonString);

    [Header("ネットワーク設定")]
    public bool isHost = true;
    [Tooltip("インデックス = プレイヤーID(0〜3)。使わないスロットはnullのままでよい")]
    public GameObject[] playerSwords = new GameObject[MaxPlayers];
    [Tooltip("自分が操作するプレイヤーのインデックス")]
    public int myPlayerIndex = 0;
    [Tooltip("今の部屋の対戦人数(2〜4)")]
    public int playerCount = 2;

    [Header("30fps・ラグ対策設定")]
    private float syncTimer = 0f;
    private float syncInterval = 0.0333f; // ▼ タイマーの基準を 0.0333秒 (30fps) に設定！

    // CLIENT補間用ターゲット(インデックス=プレイヤーID)
    private Vector3[] targetPos = new Vector3[MaxPlayers];
    private Quaternion[] targetRot = new Quaternion[MaxPlayers];
    private bool isFirstSync = true;
    private bool hasSyncTarget = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ApplyModeSettings();
    }

    public void SetPlayerInfo(string jsonString)
    {
        PlayerInfoData info = JsonUtility.FromJson<PlayerInfoData>(jsonString);
        myPlayerIndex = info.myIndex;
        playerCount = Mathf.Clamp(info.playerCount, 2, MaxPlayers);
        isHost = (info.isHost == 1);
        ApplyModeSettings();
        Debug.Log($"🎮 プレイヤー情報設定: myIndex={myPlayerIndex}, playerCount={playerCount}, isHost={isHost}");
    }

    // ▼【デバッグ用】C#側から直接呼べる簡易版(SceneControllerのautoTestOnStartから使用)
    public void SetPlayerInfoDirect(int myIndex, int count, bool hostFlag)
    {
        myPlayerIndex = myIndex;
        playerCount = Mathf.Clamp(count, 2, MaxPlayers);
        isHost = hostFlag;
        ApplyModeSettings();
    }

    public GameObject GetMySword()
    {
        if (myPlayerIndex < 0 || myPlayerIndex >= playerSwords.Length) return null;
        return playerSwords[myPlayerIndex];
    }

    public void ApplyModeSettings()
    {
        for (int i = 0; i < playerSwords.Length; i++)
        {
            GameObject obj = playerSwords[i];
            if (obj == null) continue;

            var controller = obj.GetComponent<SwordController>();
            bool isMine = (i == myPlayerIndex);

            if (isHost)
            {
                // ホストは全プレイヤー分の物理演算を担うため、Rigidbodyのモードはインスペクタ設定のまま(Dynamic)
                if (controller != null) controller.isLocalControlled = isMine;
            }
            else
            {
                var rb = obj.GetComponent<Rigidbody2D>();
                if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;
                if (controller != null) controller.isLocalControlled = isMine;
            }
        }

        Debug.Log($"🌐 モード設定適用: isHost={isHost}, myPlayerIndex={myPlayerIndex}, playerCount={playerCount}");
    }

    public void SendData(string type, string jsonString)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
            SendToReact(type, jsonString);
        #endif
        // ▼【新規追加】本番(WebGL)ではReact/サーバー側がこのメッセージを受け取って処理するが、
        // エディタ内ではそもそも送信すらされない。MultiplayerLocalTestHarness等のデバッグ用
        // ループバックがここを購読して、送信されるはずだったメッセージを横取りできるようにする
        #if UNITY_EDITOR
            EditorSendDataHook?.Invoke(type, jsonString);
        #endif
    }
    #if UNITY_EDITOR
    public static event Action<string, string> EditorSendDataHook;
    #endif

    // HOST：タイマーで正確に30fpsに間引いて送信(全プレイヤー分)
    void FixedUpdate()
    {
        if (!isHost) return;

        syncTimer += Time.fixedDeltaTime;
        if (syncTimer < syncInterval) return;
        syncTimer -= syncInterval; // 溢れた時間を引くことで高精度な30fpsを維持

        SwordSyncData[] swordsData = new SwordSyncData[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            if (playerSwords[i] == null) continue;
            swordsData[i] = GetSyncData(playerSwords[i]);
        }

        SyncMessage sync = new SyncMessage { swords = swordsData };
        SendData("SYNC", JsonUtility.ToJson(sync));
    }

    // CLIENT：30fps通信の隙間のコマを、毎フレームLerpでヌルヌル追従(全プレイヤー分)
    void Update()
    {
        if (isHost || !hasSyncTarget) return;

        float t = Time.deltaTime * 25f;
        for (int i = 0; i < playerCount; i++)
        {
            GameObject obj = playerSwords[i];
            if (obj == null) continue;
            obj.transform.position = Vector3.Lerp(obj.transform.position, targetPos[i], t);
            obj.transform.rotation = Quaternion.Lerp(obj.transform.rotation, targetRot[i], t);
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
            dashType = battle != null ? battle.currentDashType : 0
        };
    }

    public void ReceiveInput(string jsonString)
    {
        InputMessage msg = JsonUtility.FromJson<InputMessage>(jsonString);
        if (msg.playerIndex < 0 || msg.playerIndex >= playerSwords.Length) return;

        GameObject targetObj = playerSwords[msg.playerIndex];
        if (targetObj != null)
        {
            SwordBattle battle = targetObj.GetComponent<SwordBattle>();
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
        if (sync.swords == null) return;

        int count = Mathf.Min(sync.swords.Length, playerCount, playerSwords.Length);

        // 初回のみ瞬時にスナップ（位置ズレでの開幕フライング感を解消）
        if (isFirstSync)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject obj = playerSwords[i];
                SwordSyncData data = sync.swords[i];
                if (obj == null || data == null) continue;

                obj.transform.position = new Vector3(data.x, data.y, obj.transform.position.z);
                obj.transform.rotation = Quaternion.Euler(0, 0, data.rotation);
                targetPos[i] = obj.transform.position;
                targetRot[i] = obj.transform.rotation;
            }
            isFirstSync = false;
        }

        for (int i = 0; i < count; i++)
        {
            GameObject obj = playerSwords[i];
            SwordSyncData data = sync.swords[i];
            if (obj == null || data == null) continue;

            // 座標・回転はターゲット（目的地）を更新するだけ
            targetPos[i] = new Vector3(data.x, data.y, obj.transform.position.z);
            targetRot[i] = Quaternion.Euler(0, 0, data.rotation);

            // HP・SP・状態（位置以外）は即時反映
            ApplyNonPositionSync(obj, data);
        }
        hasSyncTarget = true;
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

        // ▼ 技番号（dashType）に応じてホストと100%同じ必殺技カラーエフェクトに塗り分ける
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
                    case 4: // 剣：巨大化一回転
                        sr.color = new Color(1f, 0.3f, 0.3f); // 赤色
                        break;
                    case 5: // 剣：分身突進
                        sr.color = new Color(0.3f, 0.6f, 1f); // 青色
                        break;
                    default: // 通常状態
                        sr.color = Color.white;
                        break;
                }
            }
        }
    }

    public void ResetMatch(string emptyMessage)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void SetGameMode(string modeStr)
    {
        SwordController.isKomaMode = (modeStr == "1");

        for (int i = 0; i < playerSwords.Length; i++)
        {
            GameObject obj = playerSwords[i];
            if (obj == null) continue;
            var controller = obj.GetComponent<SwordController>();
            if (controller != null)
            {
                // ▼ Inspectorの配線ミスで柄(Handle-A)参照が別プレイヤーの剣を指していることがあるため、
                // モード切り替えで柄の表示/非表示を変える直前に必ず自分自身の柄を参照し直す
                controller.ResolveOwnHandle();
                // ▼ SwordGenerator側の柄参照(hiltTypeに応じた見た目の切り替え先)がResolveOwnHandle()の
                // 結果に追従していないと、表示/非表示は直っても柄の絵柄自体が古い/別の剣のままになるため、
                // MultiplayerManager.CreateSword / SceneController.CreateDynamicPlayerSwordと同様に同期する
                var generator = obj.GetComponent<SwordGenerator>();
                if (generator != null) generator.handleObject = controller.handleObject;
                controller.ApplyPhysicsMode();
            }
        }

        // ▼【新規追加】MultiplayerManager.InitializeMultiplayerと同様、剣モードは2人用(swordStage)と
        // 3・4人用(swordStage3P)でステージを使い分ける
        bool use3PSwordStage = !SwordController.isKomaMode && playerCount >= 3;
        if (swordStage != null) swordStage.SetActive(!SwordController.isKomaMode && !use3PSwordStage);
        if (swordStage3P != null) swordStage3P.SetActive(use3PSwordStage);
        if (komaStage != null) komaStage.SetActive(SwordController.isKomaMode);
    }
}
