// Assets/Plugins/ReactCommunication.jslib
mergeInto(LibraryManager.library, {
  SendToReact: function (typePtr, jsonStringPtr) {
    var type = UTF8ToString(typePtr);
    var jsonString = UTF8ToString(jsonStringPtr);
    
    if (type.indexOf("MP_") === 0 && window.MultiplayerApp && window.MultiplayerApp.receiveFromUnity) {
      window.MultiplayerApp.receiveFromUnity(type, jsonString);
    } else if (window.ReactApp && window.ReactApp.receiveFromUnity) {
      window.ReactApp.receiveFromUnity(type, jsonString);
    }
  }
});
