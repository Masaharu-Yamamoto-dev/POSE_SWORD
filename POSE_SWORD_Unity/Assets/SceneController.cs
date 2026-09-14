using UnityEngine;
using System.Collections;

// Webから送られてくる合体JSONのデータ構造
// ▼【N人対応】hostSword/clientSwordの固定2枠から、可変長(2〜4)のplayers配列に変更
[System.Serializable]
public class BattleStartData
{
    public SwordData[] players;
}

public class SceneController : MonoBehaviour
{
    void Awake()
    {
        // The serialized two-player scene remains the source of the sword template.
        if (GetComponent<MultiplayerManager>() == null) gameObject.AddComponent<MultiplayerManager>();
    }

    [Header("剣の錬成装置（インデックス=プレイヤーID、最大4枠）")]
    public SwordGenerator[] generators = new SwordGenerator[NetworkManager.MaxPlayers];

    [Header("剣の錬成装置（マルチプレイヤーのテンプレート元）")]
    public SwordGenerator hostGenerator;
    public SwordGenerator clientGenerator;

    [Header("開始位置（剣モード・2人用）")]
    public Vector3 leftPosition = new Vector3(-5f, 0f, 0f);
    public Vector3 rightPosition = new Vector3(5f, 0f, 0f);

    // ▼【新規追加】独楽モード用の開始位置
    [Header("開始位置（独楽モード・2人用）")]
    public Vector3 komaLeftPosition = new Vector3(-4f, 3f, 0f); // 例: 剣モードより少し上で、少し近い
    public Vector3 komaRightPosition = new Vector3(4f, 3f, 0f);

    [Header("開始位置（剣モード・3〜4人用）")]
    public Vector3[] sword3PPositions = new Vector3[] {
        new Vector3(-6f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(6f, 0f, 0f)
    };
    public Vector3[] sword4PPositions = new Vector3[] {
        new Vector3(-7.5f, 0f, 0f), new Vector3(-2.5f, 0f, 0f), new Vector3(2.5f, 0f, 0f), new Vector3(7.5f, 0f, 0f)
    };

    [Header("開始位置（独楽モード・3〜4人用）")]
    public Vector3[] koma3PPositions = new Vector3[] {
        new Vector3(-5f, 3f, 0f), new Vector3(0f, 3f, 0f), new Vector3(5f, 3f, 0f)
    };
    public Vector3[] koma4PPositions = new Vector3[] {
        new Vector3(-6f, 3f, 0f), new Vector3(-2f, 3f, 0f), new Vector3(2f, 3f, 0f), new Vector3(6f, 3f, 0f)
    };

    [Header("カウントダウンTMP用UI")]
    public TMPro.TextMeshProUGUI countdownText; 

    // ▼【新規追加】インスペクタからカメラの引き具合をいつでも調整できる枠！
    [Header("演出設定")]
    [Tooltip("数値が大きいほどカメラが引きます（前回の 3f より、5f や 6f くらいにするとちょうどいい引きになります）")]
    public float countdownZoomSize = 5.5f; 

    [Header("【デバッグ】テスト用合体JSONファイル")]
    public TextAsset debugBattleJsonFile;
    [Header("【デバッグ】ゲーム起動時に自動でテスト開始するフラグ")]
    public bool autoTestOnStart = true;

    void Start()
    {
        if (autoTestOnStart && debugBattleJsonFile != null && !string.IsNullOrEmpty(debugBattleJsonFile.text))
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.SetPlayerInfoDirect(0, 2, true);
            StartBattle(debugBattleJsonFile.text);
        }
    }

    void Update()
    {
        // 起動後も「T」キーを押せば、インスペクタの数値を反映して何度でもカウントダウンからやり直せます
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (debugBattleJsonFile != null && !string.IsNullOrEmpty(debugBattleJsonFile.text))
            {
                Debug.Log("🔄 【デバッグ】Tキーが押されたため、バトルを再初期化します");
                StartBattle(debugBattleJsonFile.text);
            }
        }
    }

    public void StartBattle(string jsonString)
    {
        Debug.Log("🚩 StartBattle が呼ばれた");
        Debug.Log("⚔️ Webからバトル開始データを受信しました！");

        BattleStartData data = JsonUtility.FromJson<BattleStartData>(jsonString);
        int playerCount = data.players != null ? data.players.Length : 0;
        playerCount = Mathf.Clamp(playerCount, 2, NetworkManager.MaxPlayers);

        Vector3[] positions = GetSpawnPositions(playerCount);

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.playerCount = playerCount;

            for (int i = 0; i < NetworkManager.MaxPlayers; i++)
            {
                GameObject swordObj = NetworkManager.Instance.playerSwords[i];
                bool active = i < playerCount;
                if (swordObj == null) continue;

                swordObj.SetActive(active);
                if (!active) continue;

                swordObj.transform.position = positions[i];

                if (generators != null && i < generators.Length && generators[i] != null && data.players != null && i < data.players.Length)
                {
                    generators[i].GenerateSwordFromJson(JsonUtility.ToJson(data.players[i]));
                }
            }

            NetworkManager.Instance.ApplyModeSettings();

            // ▼【N人対応】カメラに生存者全員のTransformを渡す(自動追尾はSwordController側が毎フレーム行う)
            BattleCamera cameraCtrl = Camera.main != null ? Camera.main.GetComponent<BattleCamera>() : null;
            if (cameraCtrl != null)
            {
                Transform[] combatants = new Transform[NetworkManager.MaxPlayers];
                for (int i = 0; i < NetworkManager.MaxPlayers; i++)
                {
                    GameObject swordObj = NetworkManager.Instance.playerSwords[i];
                    combatants[i] = swordObj != null ? swordObj.transform : null;
                }
                cameraCtrl.combatants = combatants;
            }
        }

        // ▼【N人対応】生存者数を今回の人数でリセット(再戦時の不整合防止)
        SwordBattle.alivePlayerCount = playerCount;
        SwordBattle.matchEnded = false;

        Random.InitState((int)System.DateTime.Now.Ticks);

        // カウントダウン演出を開始
        StartCoroutine(CountdownCameraRoutine(playerCount));
    }

    // ▼【N人対応】人数・モードに応じたスポーン座標を返す(2人時は従来のleft/rightをそのまま使用)
    Vector3[] GetSpawnPositions(int playerCount)
    {
        bool koma = SwordController.isKomaMode;
        switch (playerCount)
        {
            case 3:
                return koma ? koma3PPositions : sword3PPositions;
            case 4:
                return koma ? koma4PPositions : sword4PPositions;
            default:
                return new Vector3[] {
                    koma ? komaLeftPosition : leftPosition,
                    koma ? komaRightPosition : rightPosition
                };
        }
    }

    IEnumerator CountdownCameraRoutine(int playerCount)
    {
        SwordBattle.isRoundStarted = false;

        if (countdownText != null) countdownText.gameObject.SetActive(true);

        BattleCamera cameraCtrl = Camera.main.GetComponent<BattleCamera>();

        if (playerCount == 2 && NetworkManager.Instance != null)
        {
            GameObject hostObj = NetworkManager.Instance.playerSwords[0];
            GameObject clientObj = NetworkManager.Instance.playerSwords[1];
            Transform hostTarget = hostObj != null ? hostObj.transform : null;
            Transform clientTarget = clientObj != null ? clientObj.transform : null;

            // ーーー 【最初の1秒：カウント3】 ホスト側の剣を映す ーーー
            if (countdownText != null) countdownText.text = "3";
            if (cameraCtrl != null && hostTarget != null) {
                // ★【修正】固定値の 3f ではなく、インスペクタで指定した countdownZoomSize を使う
                cameraCtrl.FocusOnTarget(hostTarget, countdownZoomSize);
            }
            yield return new WaitForSeconds(1.0f);

            // ーーー 【次の1秒：カウント2】 クライアント側の剣を映す ーーー
            if (countdownText != null) countdownText.text = "2";
            if (cameraCtrl != null && clientTarget != null) {
                // ★【修正】こちらもインスペクタの数値を使う
                cameraCtrl.FocusOnTarget(clientTarget, countdownZoomSize);
            }
            yield return new WaitForSeconds(1.0f);

            // ーーー 【最後の1秒：カウント1】 本来の引きカメラ位置に戻す ーーー
            if (countdownText != null) countdownText.text = "1";
            if (cameraCtrl != null) {
                cameraCtrl.ResetFocus();
            }
            yield return new WaitForSeconds(1.0f);
        }
        else
        {
            // ▼【N人対応】3〜4人戦は個別クローズアップせず、最初から全員が入る引きの画角でカウントする
            if (cameraCtrl != null) cameraCtrl.ResetFocus();

            if (countdownText != null) countdownText.text = "3";
            yield return new WaitForSeconds(1.0f);

            if (countdownText != null) countdownText.text = "2";
            yield return new WaitForSeconds(1.0f);

            if (countdownText != null) countdownText.text = "1";
            yield return new WaitForSeconds(1.0f);
        }

        // ーーー 【カウント0：GO!】 操作解禁、ゲームスタート！ ーーー
        if (countdownText != null) countdownText.text = "GO!";
        SwordBattle.isRoundStarted = true;
        Debug.Log("🟢 isRoundStarted を true にした！");   // ← この行を追加

        yield return new WaitForSeconds(1.0f);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }
}
