# 2026-08-20 10:20:00 — BaseModel の意味

## 1. BaseModel とは

`BaseModel` は Pydantic のクラスで、JSON の入力形式を Python の構造として定義するためのものです。

[image-process/server.py](image-process/server.py#L39-L45) では、次のように定義されています。

```python
class CutoutRequest(BaseModel):
    imageData: str
    userName: str = ""
```

## 2. なぜ使うのか

- JSON のキー名と型を明示できる
- 不正なデータを自動的に弾ける
- `req.imageData` のように安全にアクセスできる
- FastAPI の `/docs` に反映される

## 3. 役割

BaseModel は「JSON の形」を定義するものです。

例えば、次の JSON を受け取る前提になっています。

```json
{
  "imageData": "base64文字列",
  "userName": "taro"
}
```

FastAPI はこの JSON を `CutoutRequest` に変換して、関数内では次のように使えます。

```python
req.imageData
req.userName
```

## 4. 文字列ではダメなのか

文字列そのものではなく、BaseModel を使うのは複数フィールドを一括で扱うためです。

`imageData` と `userName` のような複数要素をまとめて、型安全に扱いたいからです。

## 5. 具体例

```python
def cutout(req: CutoutRequest):
    print(req.imageData)
    print(req.userName)
```

これが、JSON を自動で Python オブジェクトに変換してくれるため、手書きの `json.loads()` と型確認が不要になります。

## 6. まとめ

BaseModel は「API を受け取るべき入力データの形」を定義するクラスです。

このプロジェクトでは、`CutoutRequest` が request body の定義になっており、FastAPI との連携を簡潔で安全にしています。
