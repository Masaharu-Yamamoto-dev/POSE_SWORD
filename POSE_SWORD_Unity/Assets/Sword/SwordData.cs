[System.Serializable]
public class SwordData
{
    public string name;
    public int attack;
    public int weight;
    public int hp;
    public string imageStr; // 👈 imageData から imageStr に変更
    public string hiltType; // 柄の種類＝必殺技の種類（未指定時は"0"＝デフォルトの必殺技）
    // 残機モード用：手持ちの剣(最大3本、空きスロットはisEmpty=true)。React側が常に3本分送ってくるが、
    // 残機モードでない試合ではMultiplayerManager側でこの配列を無視し、上記の単一ステータスだけを使う
    public SwordSlotData[] swords;
    public int equippedIndex; // swords配列内で現在装備中のインデックス(0〜2)
}

// 残機モードで持ち替える剣1本分のデータ。SwordDataと同じ構成にReact側のswords[]要素と合わせている
[System.Serializable]
public class SwordSlotData
{
    public string name;
    public int attack;
    public int weight;
    public int hp;
    public string imageStr;
    public string hiltType;
    public bool isEmpty;
}
