/**
 * Vercel サーバーサイド関数 — HuggingFace Space への中継
 *
 * ブラウザからは /api/cutout を呼ぶ。
 * APIキーはこのサーバー側でのみ付与されるため、ブラウザには漏れない。
 *
 * 必要な Vercel 環境変数（VITE_ を付けないこと！）:
 *   API_URL  ... HuggingFace Space の URL（例: https://user-pose-sword-api.hf.space）
 *   API_KEY  ... HuggingFace Space の Secrets に設定した API_KEY と同じ値
 */
// Vercel の最大実行時間を延長（画像処理は時間がかかるため）
// BodyParserの上限も引き上げ（base64画像は大きい）
export const config = {
  api: {
    bodyParser: {
      sizeLimit: '10mb',
    },
  },
  maxDuration: 60,
};

export default async function handler(req, res) {
  if (req.method !== "POST") {
    return res.status(405).json({ error: "Method Not Allowed" });
  }

  const apiUrl = process.env.API_URL;
  const apiKey = process.env.API_KEY;

  if (!apiUrl) {
    return res.status(500).json({ error: "API_URL が設定されていません" });
  }

  try {
    const response = await fetch(`${apiUrl}/cutout`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(apiKey ? { "x-api-key": apiKey } : {}),
      },
      body: JSON.stringify(req.body),
    });

    const data = await response.json();
    return res.status(response.status).json(data);
  } catch (err) {
    console.error("HuggingFace Space への接続エラー:", err);
    return res.status(502).json({ error: "APIサーバーへの接続に失敗しました" });
  }
}
