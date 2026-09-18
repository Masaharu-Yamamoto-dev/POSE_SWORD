import { useEffect, useState } from 'react';

// TURN の資格情報は寿命が短く、発行にはAPIトークンが要る。トークンをブラウザに置けないので
// /api/turn（Cloudflare Realtime への窓口）に発行してもらい、返ってきたものだけを使う。
//
// 取得できるまでの間と、取得に失敗したときは STUN だけで動かす。同じ回線同士なら
// それでも繋がるので、ロビーの作成自体は妨げない。
const FALLBACK = Object.freeze({ config: { iceServers: [
  { urls: 'stun:stun.cloudflare.com:3478' },
  { urls: 'stun:stun.l.google.com:19302' },
] } });

export function useIceConfig(endpoint = '/api/turn') {
  const [peerOptions, setPeerOptions] = useState(FALLBACK);
  // relay 候補を出せる見込みがあるか。回線をまたぐ対戦が成立するかの目安になる。
  const [hasRelay, setHasRelay] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await fetch(endpoint);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const payload = await response.json();
        if (cancelled || !Array.isArray(payload.iceServers) || !payload.iceServers.length) return;
        setPeerOptions({ config: { iceServers: payload.iceServers } });
        setHasRelay(Boolean(payload.relay));
      } catch {
        // 取れなければ FALLBACK のまま使う。
      }
    })();
    return () => { cancelled = true; };
  }, [endpoint]);

  return { peerOptions, hasRelay };
}
