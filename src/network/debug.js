// 通信の切り分け用のログ。端末のコンソールで
//   localStorage.setItem('POSE_SWORD_NET_DEBUG', '1')
// を実行して再読み込みすると出るようになる。
// 実機（スマホ）でも本番ビルドのまま切り替えられるよう、開発フラグではなく保存値で見る。
export const netDebug = () => {
  try { return localStorage.getItem('POSE_SWORD_NET_DEBUG') === '1'; } catch { return false; }
};

export const log = (...args) => { if (netDebug()) console.log('[net]', ...args); };
