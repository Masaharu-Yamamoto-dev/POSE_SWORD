[System.Serializable]
public class SwordData
{
    public string name;
    public int attack;
    public int weight;
    public int hp;
    public string imageStr; // 👈 imageData から imageStr に変更
    public string hiltType; // 柄の種類＝必殺技の種類（未指定時は"0"＝デフォルトの必殺技）
}