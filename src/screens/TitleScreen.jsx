import React from 'react';
import { styles } from '../styles';

export default function TitleScreen({
    mySwordData,
    systemMessage,
    goToCrafting,
    handleCreateRoom,
    handleJoinRoom
}) {
    return (
        <div style={styles.container}>
            {mySwordData?.imageSrc && (
                <img src={mySwordData.imageSrc} alt="Background Sword" style={styles.bgImageCenter} />
            )}

            <div style={styles.contentWrapper}>
                <img src="/logo.png" alt="オレブレード" style={{ width: '90%', maxWidth: '800px', marginBottom: '40px', objectFit: 'contain' }} />

                <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '100%', maxWidth: '300px' }}>
                    {/* 1. 剣を錬成するボタン */}
                    <div className="ink-btn-container">
                        <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                        <button
                            className="sharp-button"
                            style={{ '--btn-color': '#4CAF50' }}
                            onClick={() => goToCrafting("TITLE")}
                        >
                            {mySwordData ? "⚔️ 剣を再錬成する" : "⚔️ 剣を錬成する"}
                        </button>
                    </div>

                    <div style={{ borderTop: '2px solid #ddd', margin: '10px 0' }}></div>

                    {!mySwordData && (
                        <p style={{ color: '#888', fontSize: '14px', margin: '0 0 -10px 0', fontWeight: 'bold' }}>
                            対戦するには、先に剣を錬成してください
                        </p>
                    )}

                    {/* 2. 部屋を作るボタン */}
                    <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
                        <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                        <button
                            className="sharp-button"
                            onClick={handleCreateRoom}
                            disabled={!mySwordData}
                        >
                            ロビーを作成
                        </button>
                    </div>

                    {/* 3. 部屋に入るボタン */}
                    <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
                        <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                        <button
                            className="sharp-button"
                            onClick={handleJoinRoom}
                            disabled={!mySwordData}
                        >
                            ロビーに入る
                        </button>
                    </div>
                </div>

                {/* ▼【変更】エラーメッセージ領域の高さを固定（レイアウトずれ防止） */}
                <div style={{ height: '50px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginTop: '30px', width: '90%' }}>
                    {systemMessage && (
                        <div style={{ ...styles.errorMessage, margin: '0' }}>
                            ⚠️ {systemMessage}
                        </div>
                    )}
                </div>

            </div>
        </div>
    );
}