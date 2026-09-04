# Alpha Bleed Fixer for RimWorld

透明PNGのミップマップ縮小時に発生する黒縁・色縁を軽減するWindows用ツールです。

可視画素とアルファ値は変更せず、完全透明画素 (`alpha = 0`) のRGBだけを、可視領域から指定幅だけ外側へ拡張します。

## 使い方

1. `dist/AlphaBleedFixer.exe`を起動します。
2. 必要に応じて拡張幅、サブフォルダ、バックアップの設定を変更します。
3. PNGまたはフォルダをウィンドウへドラッグ＆ドロップすると、確認なしですぐに処理します。
4. フォルダ選択で処理する場合は「フォルダ内の画像を処理...」を押します。
5. RimWorldを完全に終了して再起動し、表示を確認します。

EXEへPNGやフォルダを直接ドラッグ＆ドロップして起動することもできます。

設定値はEXEと同じディレクトリの`AlphaBleedFixer.ini`へ保存され、次回起動時に復元されます。

## アップデート

起動時に[GitHub Releases](https://github.com/superhellme/AlphaBleedFixer/releases)の最新版を自動確認します。「アップデートを確認...」ボタンから手動確認することもできます。

新しいバージョンがある場合は確認後に`AlphaBleedFixer.zip`をダウンロードし、安全に展開してアプリを差し替え・再起動します。既存の`AlphaBleedFixer.ini`は維持されます。

## 安全対策

- 画像サイズを維持します。
- 全画素のアルファ値を維持します。
- `alpha > 0`の可視画素を維持します。
- 保存したPNGを再読込し、期待するRGBAと完全一致する場合だけ元画像を置換します。
- 既定では元画像を`*.alpha-bleed-backup.bak`として保存します。
- 全透明画像、完全不透明画像、32-bit RGBA以外の画像はスキップします。

`.bak`はPNG拡張子ではないため、RimWorldのテクスチャとして読み込まれません。

## コマンドライン

```powershell
AlphaBleedFixer.exe --batch --padding 32 --recursive "C:\path\to\Textures"
```

オプション:

- `--padding 1..128`: RGBの拡張幅
- `--recursive`: サブフォルダを処理（既定）
- `--no-recursive`: 直下のみ処理
- `--no-backup`: バックアップを作らない

## ビルド

.NET Framework 4.8の参照アセンブリと.NET SDKが必要です。

```powershell
.\build.ps1
```

または`build.cmd`をダブルクリックします。外部NuGetパッケージは使用していません。Releaseビルドと配布用`AlphaBleedFixer.zip`が生成されます。
