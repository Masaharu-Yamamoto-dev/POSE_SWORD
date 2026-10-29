import { useState, useRef, useEffect, useEffectEvent, useCallback } from 'react';
import './App.css';
import './components/sumi.css';
import { styles } from './styles';

import TitleScreen from './screens/TitleScreen';
import LobbyScreen from './screens/LobbyScreen';
import ResultScreen from './screens/ResultScreen';
import { NameInputScreen, CraftPoseScreen, CraftingApiScreen } from './screens/CraftingScreens';
import CraftCompleteScreen from './screens/CraftCompleteScreen';
import SwordListScreen from './screens/SwordListScreen';
import { HILT_DATABASE } from './screens/hiltDatabase.js';
import MatchmakingScreen from './screens/MatchmakingScreen';
import BattleArena from './components/BattleArena.jsx';
import MatchIntro from './components/MatchIntro.jsx';
import { useMatchIntro } from './components/useMatchIntro.js';
import HowToPlayPanel from './components/HowToPlayPanel.jsx';
import SumiFilters from './components/SumiFilters.jsx';
import Stage16x9 from './components/Stage16x9.jsx';
import FullscreenButton from './components/FullscreenButton.jsx';
import FitToHeight from './components/FitToHeight.jsx';
import RotateNotice from './components/RotateNotice.jsx';
import { ConfirmSheet } from './components/SwordForgeParts.jsx';
import { useRoom } from './network/useRoom.js';
import { HOST_CLOSED_MESSAGE } from './network/RoomSession.js';
import { useRandomMatch } from './network/useRandomMatch.js';
import { useIceConfig } from './network/useIceConfig.js';
import { useCamera } from './useCamera.js';
import { DEFAULT_SPOT, base64Of, derivedImages, flipImage, makeOnepointImage, newPrivacy, privacyOf, publicImageOf } from './swordPrivacy.js';

const ACTIVE_PHASES = ['LOADING', 'COUNTDOWN', 'PLAYING'];
const ROOM_STEPS = ['LOBBY', 'PLAYING', 'RESULT'];
// ワンポイントの丸を動かしてから、焼き込んだ画像を作り直すまでの待ち時間
const ONEPOINT_DEBOUNCE_MS = 200;

// 画像が変わったかを、全体を比べずに見分ける（長さと、途中の数か所の文字）
const imageFingerprint = str => {
  if (!str) return '0';
  const n = str.length;
  return `${n}:${str.slice(n >> 1, (n >> 1) + 24)}:${str.slice((n * 3) >> 2, ((n * 3) >> 2) + 24)}`;
};

// 武器の中身が変わったかどうかだけを見る。
const swordKey = sword => sword && `${sword.id}:${sword.name}:${sword.hp}:${sword.attack}:${sword.weight}:${sword.hiltType}:${imageFingerprint(sword.imageStr)}`;

// 🌟 残機モード用のswords[](装備中以外も含む3本分)まで含めた同期判定キー
const syncKey = sync => {
  if (!sync) return null;
  const slots = (sync.swords || [])
    .map(s => `${s.name}:${s.hp}:${s.attack}:${s.weight}:${imageFingerprint(s.imageStr)}:${s.hiltType ?? '0'}:${s.isEmpty ? 1 : 0}`)
    .join('|');
  return `${swordKey(sync)}:${sync.equippedIndex}:${slots}`;
};

const hiltTypeOf = sword => typeof sword.hiltType === 'string' ? sword.hiltType : String(sword.hiltType || 0);

// 🌟 柄の補正適用および下限1の設定を行うヘルパー関数
const getFinalStats = (sword) => {
  if (!sword) return { hp: 1, attack: 1, weight: 1 };
  const hilt = HILT_DATABASE[sword.hiltType || "0"] || HILT_DATABASE["0"];
  return {
    hp: Math.max(1, (sword.hp || 0) + (hilt.hpBonus || 0)),
    attack: Math.max(1, (sword.attack || 0) + (hilt.attackBonus || 0)),
    weight: Math.max(1, (sword.weight || 0) + (hilt.weightBonus || 0)),
  };
};

// 柄の補正を足す前の剣本体の値。独楽モードの試合ではこちらで戦う（HostRoom.js の swordForGameMode）
const baseStatsOf = (sword) => ({
  hp: Math.max(1, sword?.hp || 0),
  attack: Math.max(1, sword?.attack || 0),
  weight: Math.max(1, sword?.weight || 0),
});

// 錬成中の画面を見せる最短の時間と、消えるまでの時間（CraftingScreens.css の craft-fade-out と同じ）
const CRAFT_MIN_MS = 2000;
const CRAFT_FADE_MS = 350;

export default function PoseSwordWeb() {
  const [step, setStep] = useState("TITLE");
  const [titleMode, setTitleMode] = useState("DEFAULT");
  const [showHowToPlay, setShowHowToPlay] = useState(false);
  const [craftReturnStep, setCraftReturnStep] = useState("TITLE");

  const [swordList, setSwordList] = useState([]);
  const [craftingTargetId, setCraftingTargetId] = useState(null);
  // 撮り直しを始めた画面（取りやめたらそこへ戻す）と、錬成完了の画面で完成の演出をするか
  const [recaptureFrom, setRecaptureFrom] = useState("SWORD_LIST");
  const [completeIntro, setCompleteIntro] = useState(true);
  const [mySwordData, setMySwordData] = useState(null);
  const mySwordRef = useRef(null);
  useEffect(() => { mySwordRef.current = mySwordData; }, [mySwordData]);
  const swordListRef = useRef(swordList);
  useEffect(() => { swordListRef.current = swordList; }, [swordList]);
  // 剣はこのタブの中にしか無く、どこにも保存していない。リロードやタブを閉じると全部消えるので、
  // 1本でも作ったらブラウザの「このページを離れますか？」を出す。
  // 文言はブラウザが決めたもので固定（独自の文章は今のブラウザでは出せない）。
  const hasSwords = swordList.length > 0;
  useEffect(() => {
    if (!hasSwords) return;
    // preventDefault だけでは出ないブラウザ（Safari・古いChrome）があるので、returnValue も空でない文字列にする。
    // この文字列は画面には出ない（どのブラウザも自前の決まった文言を出す）
    const warn = e => { e.preventDefault(); e.returnValue = '作成した剣は保存されません'; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [hasSwords]);
  // 最後にプライバシーを選んだときの見え方。新しく錬成した剣はこれで始まる
  const [lastPrivacyMode, setLastPrivacyMode] = useState('none');

// 🌟 通信・Unity送信用のデータ生成
  // 送る項目は1つずつ書き出す（剣のデータを丸ごと広げると、加工前の写真 imageSrc まで混ざる）。
  // 画像は publicImageOf で選んだものだけ。プライバシーを設定している剣は加工済みの画像になり、
  // ランダムマッチでは必ずシルエットになる
  const createSyncSwordData = (list, equipped, isRandomMatch = false) => {
    if (!equipped) return null;

    const swords = [0, 1, 2].map(index => {
      const sword = list[index];
      if (sword) {
        const stats = getFinalStats(sword);
        const image = publicImageOf(sword, isRandomMatch);
        return {
          name: sword.name,
          hp: stats.hp,
          attack: stats.attack,
          weight: stats.weight,
          imageStr: base64Of(image), // 🌟 Unityが画像を生成する時に使うデータ（オレ達シリーズの必殺技もここを参照する）
          imageSrc: image, // 🌟 相手のReact画面で表示する時に使うデータ
          hiltType: hiltTypeOf(sword),
          base: baseStatsOf(sword),
          isEmpty: false
        };
      } else {
        return {
          name: "empty",
          hp: 1,
          attack: 1,
          weight: 1,
          imageStr: "",
          hiltType: "0",
          isEmpty: true
        };
      }
    });

    const equippedIndex = Math.max(0, list.findIndex(s => s.id === equipped.id));
    // 装備中の剣は一覧の中の最新のもの（mySwordData は一覧の写しなので、念のため一覧を優先する）
    const main = list[equippedIndex]?.id === equipped.id ? list[equippedIndex] : equipped;
    const equippedStats = getFinalStats(main);
    const mainImage = publicImageOf(main, isRandomMatch);

    return {
      id: main.id,
      name: main.name,
      hp: equippedStats.hp,
      attack: equippedStats.attack,
      weight: equippedStats.weight,
      imageStr: base64Of(mainImage),
      imageSrc: mainImage,
      hiltType: hiltTypeOf(main),
      base: baseStatsOf(main),
      swords: swords,
      equippedIndex: equippedIndex
    };
  };

  const [userName, setUserName] = useState("");
  const [targetId, setTargetId] = useState("");
  const [captureCountdown, setCaptureCountdown] = useState(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const [capturedImage, setCapturedImage] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");
  const [isCopied, setIsCopied] = useState(false);

  // 🌟 ランダムマッチ状態の管理を追加
  const [isRandomMatchActive, setIsRandomMatchActive] = useState(false);

  // 🌟 アニメーション・撮り直し用の状態管理
  const [transitionDir, setTransitionDir] = useState("forward");
  const [isFlash, setIsFlash] = useState(false);
  // 錬成中の画面を消し始めたか（錬成が終わったあと、フェードアウトしてから錬成完了の画面へ移る）
  const [craftLeaving, setCraftLeaving] = useState(false);

  const resetToTitleRef = useRef(null);
  // 部屋にいる間に、こちらから抜けたのではなく部屋が終わったとき（ホストの解散・通信の切断）は、すぐにタイトルへ戻さずお知らせを出す。
  // ロビー・対戦結果の画面にいたときは、終わった時点の画面をそのまま残してその上に出す（対戦中だったときはタイトルの上に出す）。
  // lastRoomRef は直前に見ていた部屋の画面
  const lastRoomRef = useRef(null);
  const [roomNotice, setRoomNotice] = useState(null);
  // STUN/TURN は毎回サーバーに発行してもらう。固定の資格情報を埋め込まない。
  const { peerOptions } = useIceConfig();
  const room = useRoom({
    peerOptions,
    onClosed: message => {
      const last = lastRoomRef.current;
      // 部屋に入る前（接続の失敗・満員など）と、ランダムマッチ（次の相手を探し直す）は、これまでどおりタイトルに理由を出す
      if (!last || last.auto) { resetToTitleRef.current?.(message || "ロビーとの接続が終了しました。"); return; }
      // ID入力のメニューとIDを残したままにすると、タイトルが出直したときに同じIDへ自動で入り直してしまう。
      // お知らせを閉じたあとはタイトルのはじめのメニューに戻す
      setTitleMode("DEFAULT"); setTargetId("");
      const disbanded = message === HOST_CLOSED_MESSAGE;
      setRoomNotice({
        title: disbanded ? "ロビーが解散されました" : "通信エラー",
        message: disbanded ? HOST_CLOSED_MESSAGE : last.isHost ? "ロビーとの接続が切断されました。" : "ホストとの接続が切断されました。",
        frozen: last.screen === "LOBBY" || last.screen === "RESULT" ? last : null,
      });
    },
  });
  const view = room.view;
  const sentSwordRef = useRef(null);
  const [matchSize, setMatchSize] = useState(2);
  const [matchMode, setMatchMode] = useState("0");

  // 🌟 isRandomMatchActive の状態を渡してデータを生成
  const currentSyncSword = createSyncSwordData(swordList, mySwordData, isRandomMatchActive);
  const randomMatch = useRandomMatch({ room, sword: currentSyncSword });

  const resetToTitle = useCallback((msg = "") => {
    setTitleMode("DEFAULT"); setTargetId(""); setSystemMessage(msg); setStep("TITLE");
    setIsRandomMatchActive(false); // 🌟 追加
    sentSwordRef.current = null;
  }, []);
  useEffect(() => { resetToTitleRef.current = resetToTitle; }, [resetToTitle]);

  // ▼【復元】決着演出(2.5秒)を見せるためのディレイ
  const resultMatchId = view?.result?.matchId ?? null;
  // 「どの試合の演出待ちが済んだか」を覚える。次の試合では試合IDが変わるので、自動的に未完了へ戻る
  const [delayDoneMatchId, setDelayDoneMatchId] = useState(null);
  useEffect(() => {
    if (!resultMatchId) return;
    const timer = setTimeout(() => setDelayDoneMatchId(resultMatchId), 2500);
    return () => clearTimeout(timer);
  }, [resultMatchId]);
  const resultDelayDone = resultMatchId !== null && delayDoneMatchId === resultMatchId;

  const roomScreen = (() => {
    if (!view?.room || view.closed) return null;
    const me = view.room.players.find(p => p.playerId === view.localPlayerId);
    if (ACTIVE_PHASES.includes(view.room.phase)) return "PLAYING";
    if (view.result && me && !me.inLobby) return resultDelayDone ? "RESULT" : "PLAYING";
    return "LOBBY";
  })();
  const seeking = Boolean(randomMatch.view?.seeking);

  // 🌟 displayScreen を廃止し、直接 screen を使ってアニメーションを即座に発動させる
  const frozen = roomNotice?.frozen ?? null;
  const screen = frozen ? frozen.screen
    : seeking ? "MATCHING"
    : ROOM_STEPS.includes(step) || step === "TITLE" ? (roomScreen ?? "TITLE")
    : step;

  // お知らせ用に、いま見ている部屋の画面を覚えておく
  useEffect(() => {
    lastRoomRef.current = ROOM_STEPS.includes(screen) && view?.room
      ? { screen, view, roomId: room.roomId, isHost: view.isHost, auto: Boolean(view.room.autoStart) } : null;
  }, [screen, view, room.roomId]);
  // 対戦開始の演出（ロード画面を兼ねる）。読み込みが始まったら、ロビーの上に和紙をかぶせて消し、
  // そのまま演出を出す。Unity の読み込みは演出の裏で進み、最低限の時間（MATCH_INTRO_MS）が経って全員そろったら演出ごと消える
  const intro = useMatchIntro(view?.room?.matchId, view?.room?.phase);
  const introOn = screen === "PLAYING" && Boolean(view?.room) && intro.matchId === view.room.matchId && intro.stage !== "done";
  // 和紙をかぶせている間は、下にロビーを出したままにする（Unity の画面はまだ出さない）。
  // 開始した時点で全員「ロビーにいない」扱いになるので、見た目はロビーにいたときのままにしておく
  const lobbyLeaving = introOn && intro.stage === "fade";
  const shownScreen = lobbyLeaving ? "LOBBY" : screen;
  const shownView = frozen?.view ?? (lobbyLeaving
    ? { ...view, room: { ...view.room, phase: "LOBBY", players: view.room.players.map(p => ({ ...p, inLobby: true })) } }
    : view);
  // 読み込みの失敗などで開始が取り消され、ロビーに戻ったときのお知らせ。閉じた試合は覚えておき、二度は出さない
  const [seenAbortId, setSeenAbortId] = useState(null);
  const abortNotice = screen === "LOBBY" && !roomNotice && view?.aborted && view.aborted.matchId !== seenAbortId
    ? view.aborted : null;

  // 剣の情報を送り直すのはロビーにいる間だけ。ロビーから武器庫を開いて直している間は送らず、
  // ロビーに戻ったときにまとめて送る（直している途中の剣を相手に見せない）
  useEffect(() => {
    if (screen !== "LOBBY" || !view?.room || !mySwordData) return;
    const key = syncKey(currentSyncSword);
    if (sentSwordRef.current === key) return;
    sentSwordRef.current = key;
    room.updateSword(currentSyncSword);
  }, [screen, mySwordData, swordList, view?.room, room, currentSyncSword]);

  const camera = useCamera(step === "CRAFT_POSE", videoRef);

  const equipSword = (sword) => setMySwordData(sword);
  const reorderSwords = (newList) => setSwordList(newList);

  // 確かめるのは武器庫の確認シートで済ませている。最後の1本は消さない
  const deleteSword = (idToRemove) => {
    if (swordList.length <= 1) return;
    const newList = swordList.filter(s => s.id !== idToRemove);
    setSwordList(newList);
    if (mySwordRef.current?.id === idToRemove) setMySwordData(newList[0]);
  };

  const updateSword = (targetId, newProps) => {
    setSwordList(prev => {
      const newList = prev.map(s => s.id === targetId ? { ...s, ...newProps } : s);
      return newList;
    });
    if (mySwordRef.current?.id === targetId) setMySwordData(prev => ({ ...prev, ...newProps }));
  };

  // ワンポイントの焼き込みは、丸を動かし終えてから少し待って作り直す。
  // 剣ごとに番号を振り、作っている間に丸がまた動いたら（番号が変わったら）古い結果は捨てる
  const onepointJobRef = useRef({});
  const onepointTimerRef = useRef({});
  const scheduleOnepoint = (targetId) => {
    clearTimeout(onepointTimerRef.current[targetId]);
    const job = (onepointJobRef.current[targetId] ?? 0) + 1;
    onepointJobRef.current[targetId] = job;
    onepointTimerRef.current[targetId] = setTimeout(async () => {
      const sword = swordListRef.current.find(s => s.id === targetId);
      const privacy = privacyOf(sword);
      if (!sword || privacy.mode !== 'onepoint') return;
      try {
        const onepointSrc = await makeOnepointImage(sword.imageSrc, privacy.spot);
        if (onepointJobRef.current[targetId] === job) updateSword(targetId, { onepointSrc });
      } catch (e) { console.error(e); }
    }, ONEPOINT_DEBOUNCE_MS);
  };
  // 作りかけの焼き込みを取り消す（撮り直し・左右反転で写真そのものが変わるとき）
  const cancelOnepoint = (targetId) => {
    clearTimeout(onepointTimerRef.current[targetId]);
    onepointJobRef.current[targetId] = (onepointJobRef.current[targetId] ?? 0) + 1;
  };

  // 見え方や丸を変えたら、古い焼き込み画像はすぐ捨てる。作り直すまでの間はシルエットで代わりにする
  // （古い丸の位置の画像を一瞬でも相手に見せない）
  const updatePrivacy = (targetId, privacy) => {
    setLastPrivacyMode(privacy.mode);
    updateSword(targetId, { privacy, onepointSrc: '' });
    if (privacy.mode === 'onepoint') scheduleOnepoint(targetId);
    else cancelOnepoint(targetId);
  };

  // 左右反転：写真・シルエット・ワンポイントを全部反転し、丸の位置も左右を入れ替える。
  // 反転している間にもう一度押されても重ねて反転しない
  const flippingRef = useRef(new Set());
  const toggleSwordFlip = async (targetId) => {
    const target = swordListRef.current.find(s => s.id === targetId);
    if (!target || flippingRef.current.has(targetId)) return;
    flippingRef.current.add(targetId);
    cancelOnepoint(targetId);
    try {
      const { mode, spot } = privacyOf(target);
      const privacy = { mode, spot: { ...spot, x: 1 - spot.x } };
      const imageSrc = await flipImage(target.imageSrc);
      const images = await derivedImages(imageSrc, privacy);
      updateSword(targetId, { imageSrc, imageStr: base64Of(imageSrc), privacy, ...images });
    } catch (e) {
      console.error(e);
    } finally {
      flippingRef.current.delete(targetId);
    }
  };

  const startRecapture = (targetId, from = "SWORD_LIST") => {
    const target = swordList.find(s => s.id === targetId);
    if (target) {
      setCraftingTargetId(targetId); setUserName(target.baseName); setRecaptureFrom(from);
      setTransitionDir("forward");
      setStep("CRAFT_POSE");
    }
  };

  // 新しく錬成を始めた画面（名前入力で戻ったら、錬成完了から来たときは錬成完了へ戻す）
  const [newCraftFrom, setNewCraftFrom] = useState("SWORD_LIST");
  const startNewCrafting = (from = "SWORD_LIST") => {
    setNewCraftFrom(from);
    setCraftingTargetId(null); setUserName("");
    setTransitionDir("forward");
    setStep("NAME_INPUT");
  };

  // タイトルの「剣を錬成する」：所持が3本未満のときだけ、名前の入力から始める（戻るとタイトルへ）
  const startCraftingFromTitle = () => {
    if (swordList.length >= 3) return;
    setCraftReturnStep("TITLE");
    startNewCrafting("TITLE");
  };

  const goToCrafting = (returnStep) => {
    if (returnStep === "LOBBY") room.setReady(false);
    setCraftReturnStep(returnStep);
    setTransitionDir("forward");
    if (swordList.length === 0) {
      setCraftingTargetId(null); setUserName(""); setStep("NAME_INPUT");
    } else {
      setStep("SWORD_LIST");
    }
  };

  const cancelList = () => {
    setTransitionDir("back");
    setStep(craftReturnStep);
  };

  const handleLeave = () => {
    randomMatch.cancel();
    room.leave();
    setRoomNotice(null);
    resetToTitle("");
  };

  const openRandomMatch = () => {
    if (!mySwordData) return;
    setSystemMessage(""); room.clearError(); setTitleMode("MATCH_SIZE");
  };

  // ランダムマッチの待ち時間を数え始めた時刻。待ち表示が作り直されても（画面の縦横を切り替えたときなど）数え直さないよう、ここで持つ
  const [matchStartedAt, setMatchStartedAt] = useState(0);

  const startRandomMatch = (size) => {
    if (!mySwordData) return;
    setMatchSize(size);
    setMatchStartedAt(Date.now());
    setSystemMessage(""); setTitleMode("DEFAULT");
    setIsRandomMatchActive(true); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, true)); // 🌟 追加
    randomMatch.start(size, matchMode);
  };

  const cancelRandomMatch = () => {
    randomMatch.cancel();
    setIsRandomMatchActive(false); // 🌟 追加
    resetToTitle("");
  };

  const findNewOpponents = () => {
    room.leave();
    setIsRandomMatchActive(true); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, true)); // 🌟 追加
    setMatchStartedAt(Date.now());
    randomMatch.start(matchSize, matchMode);
  };

  const handleCopyId = () => {
    if (!room.roomId) return;
    navigator.clipboard.writeText(room.roomId).then(() => {
      setIsCopied(true); setTimeout(() => setIsCopied(false), 2000);
    }).catch(e => console.error(e));
  };

  const handleCreateRoom = () => {
    if (!mySwordData) return;
    setSystemMessage(""); setTitleMode("DEFAULT");
    setIsRandomMatchActive(false); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
    room.createRoom(createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
  };

  const connectToHost = () => {
    setSystemMessage("");
    if (!targetId.trim()) return setSystemMessage("IDを入力してください。");
    if (!/^\d+$/.test(targetId)) return setSystemMessage("半角数字のみで入力してください。");
    if (targetId.length < 6) return setSystemMessage("6桁で入力してください。");
    if (!mySwordData) return setSystemMessage("先に剣を錬成してください。");

    setIsRandomMatchActive(false); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
    room.joinRoom(targetId, createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
  };

  const handleJoinRoom = () => {
    setSystemMessage(""); room.clearError(); setTargetId(""); setTitleMode("JOIN_INPUT");
  };

  const handleCancelJoin = () => {
    setTitleMode("DEFAULT"); setSystemMessage("");
    room.leave();
  };

  const startCaptureCountdown = () => setCaptureCountdown(5);

  const executeCaptureAndCraft = () => {
    setIsFlash(true);
    setTimeout(() => setIsFlash(false), 500);

    setTimeout(() => {
      setStep("CRAFTING_API");
      setCraftLeaving(false);
      // 錬成中の演出は、錬成が早く終わっても最低これだけ見せる
      const craftStartedAt = performance.now();
      const video = videoRef.current;
      const canvas = canvasRef.current;
      if (!video || !canvas) return;
      canvas.width = video.videoWidth; canvas.height = video.videoHeight;
      const context = canvas.getContext('2d');
      // 画面で見えていたのと同じ向きで撮る（自撮り側は鏡像、外側カメラはそのまま）
      context.save();
      if (camera.mirrored) { context.scale(-1, 1); context.translate(-canvas.width, 0); }
      context.drawImage(video, 0, 0, canvas.width, canvas.height);
      context.restore();

      const base64Full = canvas.toDataURL('image/jpeg');
      setCapturedImage(base64Full);
      const base64DataOnly = base64Full.split(',')[1];
      const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? '/api'}/cutout`;

      // 🌟 asyncを追加
      fetch(pythonApiUrl, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ imageData: base64DataOnly, userName: userName }) })
        .then(res => { if (!res.ok) throw new Error(`HTTPエラー`); return res.json(); })
        .then(async (data) => {
          // 🌟 シルエット・ワンポイントの画像もここで作る（相手にはこちらだけを送る）
          const newImageSrc = "data:image/png;base64," + data.imageData;

          if (craftingTargetId) {
            // 撮り直し：見え方はそのまま、ワンポイントの丸は写真が変わるので真ん中に戻す
            const targetOld = swordList.find(s => s.id === craftingTargetId);
            const privacy = { mode: privacyOf(targetOld).mode, spot: { ...DEFAULT_SPOT } };
            cancelOnepoint(craftingTargetId);
            const images = await derivedImages(newImageSrc, privacy);
            const updatedProps = { hp: data.params.hp, attack: data.params.attack, weight: data.params.weight, imageStr: data.imageData, imageSrc: newImageSrc, privacy, ...images };
            updateSword(craftingTargetId, updatedProps);
            if (targetOld) setMySwordData({ ...targetOld, ...updatedProps });
            setCraftingTargetId(null);
          } else {
            // 新しい剣：最後にプライバシーを選んだときの見え方で始める
            const privacy = newPrivacy(lastPrivacyMode);
            const images = await derivedImages(newImageSrc, privacy);
            const newSword = { id: Date.now().toString(), baseName: userName, name: data.swordName || "無銘の剣", hp: data.params.hp, attack: data.params.attack, weight: data.params.weight, imageStr: data.imageData, imageSrc: newImageSrc, privacy, ...images, hiltType: "0" };
            setSwordList(prev => {
              let updatedList = [...prev];
              if (updatedList.length >= 3) updatedList.shift();
              updatedList.push(newSword);
              return updatedList;
            });
            setMySwordData(newSword);
          }
          // 錬成中の画面を最低2秒見せてから、その場でフェードアウトさせ、消えきったら錬成完了の画面へ
          const wait = Math.max(0, CRAFT_MIN_MS - (performance.now() - craftStartedAt));
          setTimeout(() => {
            setCraftLeaving(true);
            setTimeout(() => {
              setCompleteIntro(true);
              setStep("CRAFT_COMPLETE");
              setCraftLeaving(false);
            }, CRAFT_FADE_MS);
          }, wait);
        })
        .catch((error) => { console.error(error); alert("AIサーバーとの通信に失敗しました。"); setStep("CRAFT_POSE"); });
    }, 50);
  };

  // カウントダウンを打ち切って撮影する。数え終わった時と「今すぐ撮影！」の両方から呼ぶ
  const captureNow = () => { setCaptureCountdown(null); executeCaptureAndCraft(); };
  const onCountdownEnd = useEffectEvent(captureNow);

  useEffect(() => {
    if (captureCountdown === null) return;
    const timer = setTimeout(() => {
      if (captureCountdown > 1) setCaptureCountdown(captureCountdown - 1);
      else onCountdownEnd();
    }, 1000);
    return () => clearTimeout(timer);
  }, [captureCountdown]);

  const handleCancelCrafting = () => {
    setUserName(""); setCraftingTargetId(null);
    setTransitionDir("back");
    // 錬成完了の「もう1本錬成する」から来たときは、演出なしで錬成完了へ戻す
    if (newCraftFrom === "CRAFT_COMPLETE" && mySwordData) { setCompleteIntro(false); setStep("CRAFT_COMPLETE"); }
    else if (newCraftFrom === "TITLE") setStep("TITLE");
    else if (swordList.length > 0) setStep("SWORD_LIST");
    else setStep(craftReturnStep);
  };

  const handleBackFromPose = () => {
    setTransitionDir("back");
    if (craftingTargetId !== null) {
      setCraftingTargetId(null);
      // 錬成完了の画面から撮り直しに来たときは、演出なしで錬成完了の画面に戻す
      if (recaptureFrom === "CRAFT_COMPLETE") { setCompleteIntro(false); setStep("CRAFT_COMPLETE"); }
      else setStep("SWORD_LIST");
    } else {
      setStep("NAME_INPUT");
    }
  };

  const renderScreen = () => {
    switch (shownScreen) {
      // マッチング待ちはタイトル画面のメニューの場所に出す（ロゴなど他の要素はそのまま残る）
      case "TITLE":
      case "MATCHING":
        return <TitleScreen matchmaking={screen === "MATCHING" ? <MatchmakingScreen view={randomMatch.view} gameMode={matchMode} startedAt={matchStartedAt} onCancel={cancelRandomMatch} /> : null} mySwordData={mySwordData} backgroundSrc={publicImageOf(mySwordData)} titleMode={titleMode} targetId={targetId} setTargetId={setTargetId} systemMessage={roomNotice ? '' : (room.error || systemMessage)} goToCrafting={goToCrafting} startCrafting={startCraftingFromTitle} swordCount={swordList.length} handleCreateRoom={handleCreateRoom} handleJoinRoom={handleJoinRoom} handleCancelJoin={handleCancelJoin} connectToHost={connectToHost} connecting={room.connecting} openRandomMatch={openRandomMatch} startRandomMatch={startRandomMatch} matchMode={matchMode} setMatchMode={setMatchMode} direction={transitionDir} onOpenHowToPlay={() => setShowHowToPlay(true)}/>;
      case "NAME_INPUT":
        return <NameInputScreen direction={transitionDir} userName={userName} setUserName={setUserName} mySwordData={mySwordData} setMySwordData={setMySwordData} setStep={setStep} handleCancel={handleCancelCrafting} />;
      case "CRAFT_POSE":
        return <CraftPoseScreen direction={transitionDir} recapture={craftingTargetId !== null} camera={camera} videoRef={videoRef} canvasRef={canvasRef} captureCountdown={captureCountdown} startCaptureCountdown={startCaptureCountdown} forceCapture={captureNow} handleBack={handleBackFromPose} />;
      case "CRAFTING_API":
        return <CraftingApiScreen capturedImage={capturedImage} aspect={camera.aspect} leaving={craftLeaving} />;
      case "CRAFT_COMPLETE":
        return <CraftCompleteScreen sword={swordList.find(s => s.id === mySwordData?.id) ?? mySwordData} updateSword={updateSword} updatePrivacy={updatePrivacy} setStep={(s) => { setTransitionDir("forward"); setStep(s); }} startNewCrafting={() => startNewCrafting("CRAFT_COMPLETE")} startRecapture={id => startRecapture(id, "CRAFT_COMPLETE")} toggleSwordFlip={toggleSwordFlip} craftReturnStep={craftReturnStep} swordListLength={swordList.length} intro={completeIntro} />;
      case "SWORD_LIST":
        return <SwordListScreen direction={transitionDir} swordList={swordList} mySwordData={mySwordData} equipSword={equipSword} deleteSword={deleteSword} startNewCrafting={startNewCrafting} startRecapture={startRecapture} updateSword={updateSword} updatePrivacy={updatePrivacy} reorderSwords={reorderSwords} cancelList={cancelList} toggleSwordFlip={toggleSwordFlip} returnStep={craftReturnStep} />;
      // 対戦ロビーと対戦結果は、対戦画面と同じく16:9固定で見せる
      case "LOBBY":
        return <Stage16x9><LobbyScreen view={shownView} roomId={frozen?.roomId ?? room.roomId} isCopied={isCopied} handleCopyId={handleCopyId} swordList={swordList} mySwordData={mySwordData} imageOf={sword => publicImageOf(sword, isRandomMatchActive)} equipSword={equipSword} reorderSwords={reorderSwords} onReady={room.setReady} onGameMode={room.setGameMode} onLivesMode={room.setLivesMode} onSoloMode={room.setSoloMode} onBossPlayer={room.setBossPlayer} onSpin={() => room.spinSword()} onStart={room.start} onLeave={handleLeave} goToCrafting={goToCrafting} error={frozen ? '' : room.error} /></Stage16x9>;
      case "RESULT":
        return <Stage16x9><ResultScreen view={shownView} onReturnToLobby={room.returnToLobby} onLeave={handleLeave} onFindNewOpponents={view?.room?.autoStart ? findNewOpponents : null} /></Stage16x9>;
      default: return null;
    }
  };

  return (
    <div style={{ textAlign: 'center', minHeight: '100vh', position: 'relative', overflow: 'hidden' }}>
      
      <SumiFilters />

      {/* 撮影フラッシュ演出用のスタイル */}
      <style>{`
        @keyframes flashFade {
          0% { opacity: 1; }
          100% { opacity: 0; }
        }
      `}</style>
      {isFlash && <div style={{ position: 'fixed', inset: 0, backgroundColor: '#fff', zIndex: 9999, pointerEvents: 'none', animation: 'flashFade 0.5s ease-out forwards' }}></div>}

      {/* 遊び方パネル（画面遷移せず、常に最前面にオーバーレイ表示） */}
      <HowToPlayPanel open={showHowToPlay} onClose={() => setShowHowToPlay(false)} />

      {/* 部屋が終わったときのお知らせ（解散・通信の切断）。「タイトルに戻る」を押すまで、終わった時点の画面を残す */}
      {roomNotice && (
        <ConfirmSheet title={roomNotice.title} confirmLabel="タイトルに戻る" onConfirm={handleLeave}>
          {roomNotice.message}
        </ConfirmSheet>
      )}

      {abortNotice && (
        <ConfirmSheet title="対戦を開始できませんでした" confirmLabel="閉じる" onConfirm={() => setSeenAbortId(abortNotice.matchId)}>
          {abortNotice.reason}
        </ConfirmSheet>
      )}

      {/* 画面描画（遅延なしで瞬時に切り替わり、各画面のCSSアニメーションが発動します） */}
      {shownScreen !== "PLAYING" && (
        // ロビーと結果は16:9の舞台が自分で大きさを合わせる。それ以外は高さに収まるよう縮める。
        // 錬成完了と武器庫は、横長なら16:9の舞台、縦長なら画面いっぱいの縦の配置を自分で組む
        ROOM_STEPS.includes(shownScreen) || ["TITLE", "MATCHING", "SWORD_LIST", "CRAFT_COMPLETE", "NAME_INPUT", "CRAFT_POSE", "CRAFTING_API"].includes(shownScreen) ? renderScreen() : <FitToHeight>{renderScreen()}</FitToHeight>
      )}

      {/* 16:9固定の画面を、スマホを縦に持って見ているときの案内（かぶせるだけで、下の画面は動かし続ける） */}
      {ROOM_STEPS.includes(screen) && <RotateNotice />}

      {/* スマホで全画面にするボタン（Androidなど対応する端末だけ。対戦中も押せるよう常に出す） */}
      <FullscreenButton />

      {/* Unity側の描画 */}
      {room.hasArena && (
        <div className="arena-stage" style={{ display: shownScreen === "PLAYING" ? 'flex' : 'none' }}>
          <BattleArena bridge={room.bridge} view={view} onLoadFailed={room.reportLoadFailure} />
        </div>
      )}
      {shownScreen === "PLAYING" && !room.hasArena && (
        <div style={{ ...styles.container, justifyContent: 'center', minHeight: '100vh' }}>
          <p style={{ fontSize: '20px', fontWeight: 'bold' }}>対戦を準備しています…</p>
        </div>
      )}

      {/* 対戦開始の演出。ロビーを和紙で覆ってから出し、最後は覆いごと高速フェードして Unity のカウントダウンを見せる */}
      {introOn && (
        <div className={`match-cover ${lobbyLeaving ? 'is-lobby-out' : ''} ${intro.stage === 'leave' ? 'is-leaving' : ''}`}>
          {!lobbyLeaving && (
            <Stage16x9><MatchIntro room={view.room} localPlayerId={view.localPlayerId} waiting={intro.waiting} /></Stage16x9>
          )}
        </div>
      )}
    </div>
  );
}
