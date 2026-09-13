// 待合所（/api/match/*）への薄い窓口。通信の失敗も「断られた理由」として返し、例外は投げない。
const DEFAULT_BASE = import.meta.env?.VITE_MATCH_API ?? '/api/match';

export function createMatchClient({ base = DEFAULT_BASE, fetchImpl = fetch } = {}) {
  const call = async (action, body) => {
    let response;
    try {
      response = await fetchImpl(`${base}/${action}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
    } catch {
      return { ok: false, reason: 'OFFLINE' };
    }
    const payload = await response.json().catch(() => ({}));
    if (response.status === 429) {
      return { ok: false, reason: payload.error ?? 'FULL', waiting: payload.waiting ?? null,
        retryAfter: payload.retryAfter ?? 10 };
    }
    if (response.status === 503) return { ok: false, reason: 'UNAVAILABLE' };
    if (!response.ok) return { ok: false, reason: 'ERROR' };
    return { ok: payload.ok !== false, ...payload };
  };

  return {
    enter: ({ targetSize }) => call('enter', { targetSize }),
    poll: ({ ticket, targetSize, room = null, exclude = [] }) =>
      call('poll', { ticket, targetSize, exclude, ...(room ?? {}) }),
    leave: ({ ticket, targetSize, roomId = null, hostToken = null, keepTicket = false }) =>
      call('leave', { ticket, targetSize, roomId, hostToken, keepTicket }),
  };
}
