/**
 * Upstash Redis の REST API を MatchStore の契約に合わせた薄い層。
 *
 * pipeline は1回のHTTP往復で複数コマンドを送る（原子性は無い）。
 * MatchStore は原子性に頼らない設計なので、これで足りる。
 */
const flatToObject = flat => {
  const object = {};
  for (let i = 0; i + 1 < flat.length; i += 2) object[flat[i]] = flat[i + 1];
  return object;
};

// HGETALL だけ REST がフラットな配列で返すため、オブジェクトへ正規化する。
const normalise = (command, result) => {
  if (command !== 'HGETALL') return result;
  return Array.isArray(result) ? flatToObject(result) : (result ?? {});
};

export function createUpstashClient({ url, token, fetchImpl = fetch }) {
  const endpoint = `${String(url).replace(/\/$/, '')}/pipeline`;
  return {
    async pipeline(commands) {
      if (!commands.length) return [];
      const response = await fetchImpl(endpoint, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify(commands),
      });
      if (!response.ok) throw new Error(`KVへの要求が失敗しました (${response.status})`);
      const payload = await response.json();
      if (!Array.isArray(payload)) throw new Error('KVの応答を解釈できませんでした');
      return payload.map((entry, index) => {
        if (entry?.error) throw new Error(`KVコマンド ${commands[index][0]} が失敗: ${entry.error}`);
        return normalise(commands[index][0], entry?.result);
      });
    },
  };
}
