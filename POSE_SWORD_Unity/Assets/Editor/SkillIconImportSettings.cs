using UnityEditor;
using UnityEngine;

// ▼【新規追加】Assets/UI/SkillIcons の必殺技アイコンの取り込み設定を、ファイルを置くだけで揃えるための設定。
// 手で直すと人によって設定がずれる(特にSprite Modeが自動でMultipleになると、1枚が細切れのSpriteに
// 分かれてしまい丸ゲージの位置が合わなくなる)ので、このフォルダの画像は常にここで決めた値で取り込む。
// 画像を描き直した時は tools/skill-icons/render_skill_icons.py で書き出すだけでよい
class SkillIconImportSettings : AssetPostprocessor
{
    public const string Folder = "Assets/UI/SkillIcons/";
    // ボタンは800x600基準のCanvasで高さ120。横1920の画面でも300px弱なので256で足りる。
    // ぼやけて見える時はここを512に上げる(Webビルドの容量はそのぶん増える)
    const int MaxSize = 256;

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = MaxSize;

        // 丸ゲージ(Filled/Radial 360)は四角いメッシュでないと形が崩れるのでFull Rectにする
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }
}
