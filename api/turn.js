/**
 * TURN の資格情報を発行する。
 *
 * Cloudflare Realtime の APIトークンはこのサーバー側にとどまり、ブラウザには渡らない。
 * 返すのは寿命の短い username/credential だけ。
 *
 * 必要な環境変数（Vercel のプロジェクト設定に入れる）:
 *   CLOUDFLARE_TURN_KEY_ID     … Realtime の TURN キーID
 *   CLOUDFLARE_TURN_API_TOKEN  … 同じキーのAPIトークン（秘密）
 */
export const config = { maxDuration: 10 };

const ENDPOINT = keyId =>
  `https://rtc.live.cloudflare.com/v1/turn/keys/${encodeURIComponent(keyId)}/credentials/generate-ice-servers`;

// TURN が用意できないときの最低限。同じ回線同士なら STUN だけでも繋がるので、
// ここで諦めさせずにロビー自体は作れるようにしておく。
const STUN_ONLY = [
  { urls: 'stun:stun.cloudflare.com:3478' },
  { urls: 'stun:stun.l.google.com:19302' },
];

// 資格情報の寿命。対戦が終わるまで持てばよいので、既定は24時間。
const TTL_SECONDS = () => Number(process.env.TURN_TTL ?? 86400);

const degraded = res => res.status(200).json({ iceServers: STUN_ONLY, relay: false });

export default async function handler(req, res) {
  if (req.method !== 'GET') return res.status(405).json({ error: 'Method Not Allowed' });
  // 資格情報なので、CDNにもブラウザにも残さない。
  res.setHeader('Cache-Control', 'no-store');

  const keyId = process.env.CLOUDFLARE_TURN_KEY_ID;
  const apiToken = process.env.CLOUDFLARE_TURN_API_TOKEN;
  if (!keyId || !apiToken) return degraded(res);

  try {
    const response = await fetch(ENDPOINT(keyId), {
      method: 'POST',
      headers: { Authorization: `Bearer ${apiToken}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ ttl: TTL_SECONDS() }),
    });
    if (!response.ok) {
      console.error('[turn] Cloudflare が %d を返しました: %s', response.status,
        (await response.text().catch(() => '')).slice(0, 200));
      return degraded(res);
    }
    // generate-ice-servers は配列で返すが、単体オブジェクトで返る系統もあるため両方受ける。
    const payload = await response.json();
    const servers = Array.isArray(payload.iceServers) ? payload.iceServers
      : payload.iceServers ? [payload.iceServers] : [];
    if (!servers.length) return degraded(res);
    // Cloudflare 側が落ちても srflx だけは集まるように、Google の STUN も残す。
    return res.status(200).json({
      iceServers: [...servers, { urls: 'stun:stun.l.google.com:19302' }],
      relay: true,
    });
  } catch (error) {
    console.error('[turn] 資格情報を取得できませんでした', error);
    return degraded(res);
  }
}
