FROM python:3.11-slim

# 1. OpenCV等に必要なシステムライブラリをインストール
RUN apt-get update && apt-get install -y libgl1

# 2. 作業ディレクトリ作成
WORKDIR /code

# 3. 依存ライブラリのインストール
COPY ./requirements.txt /code/requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

# 4. 全ファイルをコピー
COPY . .

# 5. API起動（Spacesはポート 7860 を必須とする仕様です）
CMD ["uvicorn", "server:app", "--host", "0.0.0.0", "--port", "7860"]