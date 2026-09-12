# 2026-08-20 10:30:00 — Python の例外処理

## 1. ValueError

`ValueError` は Python の標準例外で、

> 「値が不正です」

という意味です。

このコードでは、base64 のデコードや画像の読み込みが失敗したときに使われます。

```python
try:
    original = decode_image(req.imageData)
except ValueError as e:
    raise HTTPException(status_code=400, detail=str(e))
```

## 2. HTTPException

`HTTPException` は FastAPI で使う例外で、

> 「HTTP レスポンスとして失敗を返す」

ためのものです。

```python
raise HTTPException(status_code=401, detail="Unauthorized")
```

これはクライアントに HTTP 401 を返します。

## 3. `as e` の意味

```python
except ValueError as e:
```

`e` は発生した例外そのものです。

```python
str(e)
```

でメッセージを文字列として取り出せます。

## 4. `raise` と `except`

- `raise` は例外を投げる
- `except` は例外を受け取って対処する

例:

```python
try:
    x = int("abc")
except ValueError:
    print("変換できません")
```

## 5. `raise` と `return` の違い

- `return` は正常終了して値を返す
- `raise` は異常終了して例外を投げる

```python
def foo():
    return 1

def bar():
    raise ValueError("失敗")
```

## 6. この API では何をしているか

- `ValueError` を捕まえて、`HTTPException(400)` に変換している
- 認証失敗は `HTTPException(401)` を投げている

こうすることで、Python の内部エラーがそのままではなく、HTTP レスポンスとして返っています。

## 7. まとめ

例外処理の基本は次の通りです。

- 観察したい処理を `try` に入れる
- 失敗したときの型を `except` で拾う
- 必要に応じて `raise` で API で返せるエラーに変換する
