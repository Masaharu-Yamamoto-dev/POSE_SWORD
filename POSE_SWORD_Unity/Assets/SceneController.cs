using UnityEngine;
using System.Collections;

// Webから送られてくる合体JSONのデータ構造
[System.Serializable]
public class BattleStartData
{
    public SwordData hostSword;
    public SwordData clientSword;
}

public class SceneController : MonoBehaviour
{
    [Header("剣の錬成装置")]
    public SwordGenerator hostGenerator;   
    public SwordGenerator clientGenerator; 

    [Header("開始位置（剣モード用）")]
    public Vector3 leftPosition = new Vector3(-5f, 0f, 0f);
    public Vector3 rightPosition = new Vector3(5f, 0f, 0f);

    // ▼【新規追加】独楽モード用の開始位置
    [Header("開始位置（独楽モード用）")]
    public Vector3 komaLeftPosition = new Vector3(-4f, 3f, 0f); // 例: 剣モードより少し上で、少し近い
    public Vector3 komaRightPosition = new Vector3(4f, 3f, 0f);

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
            if (NetworkManager.Instance != null) NetworkManager.Instance.SetHostMode(1);
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
        Debug.Log("⚔️ Webからバトル開始データを受信しました！");
        
        BattleStartData data = JsonUtility.FromJson<BattleStartData>(jsonString);

        Vector3 hostPos = SwordController.isKomaMode? komaLeftPosition : leftPosition;
        Vector3 clientPos = SwordController.isKomaMode? komaRightPosition : rightPosition;

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.hostSword.transform.position = hostPos;
            NetworkManager.Instance.clientSword.transform.position = clientPos;
            
            var hostController = NetworkManager.Instance.hostSword.GetComponent<SwordController>();
            var clientController = NetworkManager.Instance.clientSword.GetComponent<SwordController>();
            if (hostController) hostController.enemyTarget = NetworkManager.Instance.clientSword.transform;
            if (clientController) clientController.enemyTarget = NetworkManager.Instance.hostSword.transform;
        }

        hostGenerator.GenerateSwordFromJson(JsonUtility.ToJson(data.hostSword));
        clientGenerator.GenerateSwordFromJson(JsonUtility.ToJson(data.clientSword));

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ApplyModeSettings();
        }

        Random.InitState((int)System.DateTime.Now.Ticks);
        
        // カウントダウン演出を開始
        StartCoroutine(CountdownCameraRoutine());
    }

    IEnumerator CountdownCameraRoutine()
    {
        SwordBattle.isRoundStarted = false;
        
        if (countdownText != null) countdownText.gameObject.SetActive(true);

        BattleCamera cameraCtrl = Camera.main.GetComponent<BattleCamera>();
        Transform hostTarget = NetworkManager.Instance.hostSword.transform;
        Transform clientTarget = NetworkManager.Instance.clientSword.transform;

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

        // ーーー 【カウント0：GO!】 操作解禁、ゲームスタート！ ーーー
        if (countdownText != null) countdownText.text = "GO!";
        SwordBattle.isRoundStarted = true; 

        yield return new WaitForSeconds(1.0f);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }
}