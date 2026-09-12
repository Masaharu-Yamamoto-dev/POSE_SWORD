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

# 4. モデルをビルド時に焼き込む
#    Cloud Run のファイルシステムは起動のたびに空になるため、実行時に取りに行くと
#    スケールゼロから起きるたびに再ダウンロードが発生する。切り抜き(u2net)と
#    姿勢推定(MediaPipe)の両方をここで用意し、起動後は外部通信なしで動くようにする。
ENV POSE_MODEL_PATH=/opt/models/pose_landmarker_lite.task
COPY ./stats.py ./person_cutout.py /code/
RUN python -c "from rembg import new_session; import person_cutout; new_session(person_cutout.DEFAULT_MODEL)" \
 && python -c "import stats; stats.warmup_pose()"

# 5. 全ファイルをコピー
COPY . .

# 6. API起動（Cloud Run は PORT 環境変数でポートを指定する）
#    --workers 1 : 並列化は Cloud Run のインスタンス数で行う。1コンテナに複数プロセスを
#    置くとモデルがプロセスごとに載ってメモリが倍増し、同時実行数との掛け算にもなる。
#    サービス側の「同時実行数」を 2〜4 程度に絞り、あとはインスタンスを増やして捌く。
CMD uvicorn server:app --host 0.0.0.0 --port ${PORT:-8080} --workers 1