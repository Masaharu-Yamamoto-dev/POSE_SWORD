// 1vs3 の NPC ボス。PeerJS の接続を持たないプレイヤーとして HostRoom の名簿に入り、
// 入力はホストの Unity（BossNpcBrain）が作る。ここでは名簿に載せる ID と剣データだけを決める。
//
// 剣データは人間の剣と同じ形にして validateSword() を通す。そうしておけば、
// ゲストへの画像の配信（ROSTER）・ゲスト側の検証・Unity の剣生成がすべて今の仕組みのまま動く。
import { BOSS_NPC_BLADE_BASE64 } from './bossNpcBlade.js';

// 人間のプレイヤーは p0, p1, ... なので、重ならない ID にする。
export const NPC_PLAYER_ID = 'npc';

// 剣本体の値。ボスの倍率（HostRoom.js の SOLO_BUFF）は Unity 側で掛かるので、ここは倍率を掛ける前の値にする。
// validateSword の上限（MAX_HP 1030 / MAX_ATTACK 130 / MAX_WEIGHT 120）を超えると名簿に入れられない。
// 試合での実際の値は bossStats() で確かめられる（今の倍率で hp 1600 / attack 175）。
export const BOSS_NPC_SWORD = Object.freeze({
  name: '鬼神の大太刀',
  hp: 800,
  attack: 50,
  weight: 60,
  imageStr: BOSS_NPC_BLADE_BASE64,
  // 柄はデフォルト（"0"）。必殺技も柄で決まるので、NPC の必殺技を変えたいときはここを変える。
  hiltType: '0',
});
