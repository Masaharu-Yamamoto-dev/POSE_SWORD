using UnityEngine;

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
    public SwordGenerator hostGenerator;   // Host（自分）用のジェネレーター
    public SwordGenerator clientGenerator; // Client（相手）用のジェネレーター

    [Header("開始位置")]
    public Vector3 leftPosition = new Vector3(-5f, 0f, 0f);
    public Vector3 rightPosition = new Vector3(5f, 0f, 0f);

    // Web側から SendMessage('SceneController', 'StartBattle', jsonString) で呼ばれる関数
    public void StartBattle(string jsonString)
    {
        Debug.Log("⚔️ Webからバトル開始データを受信しました！");
        Debug.Log($"📋 受信JSON: {jsonString}");
        
        // JSON文字列をC#のデータに変換
        BattleStartData data = JsonUtility.FromJson<BattleStartData>(jsonString);
        Debug.Log($"🎯 Host: {data.hostSword.name}, Client: {data.clientSword.name}");

        // --- 💡 通信ズレを防ぐ「魔法のランダム」 ---
        // 2つの剣の名前を合体させた文字列からハッシュ（一意の数字）を作り、乱数の種（シード）にします
        string combinedName = data.hostSword.name + data.clientSword.name;
        Random.InitState(combinedName.GetHashCode());
        
        // 50%の確率でHostが左になる（HostとClientで絶対に同じ結果になる！）
        bool hostIsLeft = Random.value > 0.5f; 

        // 位置を決定
        Vector3 hostPos = hostIsLeft ? leftPosition : rightPosition;
        Vector3 clientPos = hostIsLeft ? rightPosition : leftPosition;

        // 剣のオブジェクトを移動
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.hostSword.transform.position = hostPos;
            NetworkManager.Instance.clientSword.transform.position = clientPos;
            Debug.Log($"📍 配置: Host={hostPos}, Client={clientPos}");
        }

        // --- 剣の生成 ---
        // 既存の関数をそのまま使えるように、単体のJSON文字列に戻して渡します
        hostGenerator.GenerateSwordFromJson(JsonUtility.ToJson(data.hostSword));
        Debug.Log($"✅ Host側の剣を生成: {data.hostSword.name}");
        
        clientGenerator.GenerateSwordFromJson(JsonUtility.ToJson(data.clientSword));
        Debug.Log($"✅ Client側の剣を生成: {data.clientSword.name}");

        // 物理演算と操作権限を適用（HostとClientのモード切り替え）
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ApplyModeSettings();
            Debug.Log($"🎮 モード設定適用: isHost={NetworkManager.Instance.isHost}");
        }

        // 乱数を通常の状態（時間ベース）に戻しておく
        Random.InitState((int)System.DateTime.Now.Ticks);
        
        Debug.Log($"⚔️ バトル開始完了！ Hostは{(hostIsLeft ? "左" : "右")}に配置されました。");
    }
}