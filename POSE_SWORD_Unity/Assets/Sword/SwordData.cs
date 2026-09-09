[System.Serializable]
public class SwordData
{
    public string name;
    public int attack;
    public int weight;
    public int hp;
    public string imageStr; // 👈 imageData から imageStr に変更
    public int handleId; // 必殺技の種類（未指定時は0＝デフォルトの必殺技）
}

// 👈 Reactから送られてくる、2本分の剣を内包する親の構造クラスもここに追加
[System.Serializable]
public class BattleInitData
{
    public SwordData hostSword;
    public SwordData clientSword;
}