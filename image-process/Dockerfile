FROM python:3.11-slim

# 1. 必要なシステムライブラリをインストール
# libgl1       : OpenGL（rembg/onnxruntime が必要）
# libglib2.0-0 : GLib（mediapipe の cv2 が必要）
# libgles2     : OpenGL ES（mediapipe の C バインディングが必要）
# libegl1      : EGL（mediapipe の C バインディングが必要）
RUN apt-get update && apt-get install -y \
    libgl1 \
    libglib2.0-0 \
    libgles2 \
    libegl1 \
  && rm -rf /var/lib/apt/lists/*

# 2. 作業ディレクトリ作成
WORKDIR /code

# 3. 依存ライブラリのインストール
COPY ./requirements.txt /code/requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

# 4. rembg モデルをビルド時に事前ダウンロード（起動時のダウンロードを回避）
RUN python -c "from rembg import new_session; new_session('u2net')"

# 5. 全ファイルをコピー
COPY . .

# 6. API起動（Cloud Run は PORT 環境変数でポートを指定する）
#    --workers 2 : CPU バウンドな画像処理を並列化するため複数プロセスで起動
CMD uvicorn server:app --host 0.0.0.0 --port ${PORT:-8080} --workers 2