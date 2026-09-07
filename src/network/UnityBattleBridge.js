export class UnityBattleBridge {
  constructor() { this.ready = false; this.sender = null; this.pending = null; this.matchId = null; }
  setSender(sender) { this.sender = sender; this.flush(); }
  sceneReady() { this.ready = true; this.flush(); }
  flush() {
    if (this.ready && this.sender && this.pending) {
      const command = this.pending; this.pending = null; this.send(command);
    }
  }
  send({ method, data }) { this.sender('GameManager', method, JSON.stringify(data)); }
  dispatch(command) {
    if (command.method === 'InitializeMultiplayer') {
      this.matchId = command.data.matchId; this.pending = command; this.flush(); return;
    }
    if (command.data.matchId !== this.matchId) return;
    if (command.method === 'StopMultiplayer') {
      this.pending = null;
      if (this.ready && this.sender) this.send(command);
      this.matchId = null;
    } else if (this.ready && this.sender) this.send(command);
  }
}
