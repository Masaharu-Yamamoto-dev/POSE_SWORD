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

# 4. 全ファイルをコピー
COPY . .

# 5. API起動（Spacesはポート 7860 を必須とする仕様です）
CMD ["uvicorn", "server:app", "--host", "0.0.0.0", "--port", "7860"]