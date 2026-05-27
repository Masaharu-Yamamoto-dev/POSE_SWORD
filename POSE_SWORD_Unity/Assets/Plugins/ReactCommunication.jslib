// Assets/Plugins/ReactCommunication.jslib
mergeInto(LibraryManager.library, {
  SendToReact: function (typePtr, jsonStringPtr) {
    var type = UTF8ToString(typePtr);
    var jsonString = UTF8ToString(jsonStringPtr);
    
    if (window.ReactApp && window.ReactApp.receiveFromUnity) {
      window.ReactApp.receiveFromUnity(type, jsonString);
    }
  }
});