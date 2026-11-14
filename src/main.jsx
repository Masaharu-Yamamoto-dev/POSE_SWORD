import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'
import { guardTextInputKeys } from './keyboardGuard.js'

// 入力欄のキー操作（文字を消す等）を、裏で動いている Unity に止めさせない（Unity より先に登録する）
guardTextInputKeys()

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
