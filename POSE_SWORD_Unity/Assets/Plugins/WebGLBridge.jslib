mergeInto(LibraryManager.library, {
  // Unityから呼ばれる関数
  SendToWeb: function (type, jsonString) {
    // C#の文字列をJSの文字列に変換
    var typeStr = UTF8ToString(type);
    var dataStr = UTF8ToString(jsonString);
    
    // Web（React）側で用意してもらった関数を呼び出す
    if (window.ReactApp && window.ReactApp.receiveFromUnity) {
      window.ReactApp.receiveFromUnity(typeStr, dataStr);
    } else {
      console.warn("React側の関数が見つかりません: window.ReactApp.receiveFromUnity");
    }
  }
});