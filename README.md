# Mulch Patcher

WinIPSと同じ404×162ピクセルの小さな画面で、**IPS / BPS / UPS** の適用と作成ができるWindows用パッチャーです。
元ROMは保持し、処理結果を新しいファイルとして保存します。

![Mulch Patcher](docs/screenshot.png)

## ダウンロード

[Releases](https://github.com/pit-true/mulch-patcher/releases/latest) の `MulchPatcher.exe` をダウンロードして実行してください。
配布ファイルはEXEだけです。追加DLLやインストーラーは不要です。
Windows 10 / 11、.NET Framework 4.8で動作します。日本語UIです。

## パッチ適用

1. 「パッチ適用」を選択します。
2. パッチファイルと元ROMを指定します。参照ボタン、パス入力、ドラッグ＆ドロップが使えます。
3. 「適用」を押します。保存先は自動生成され、完了ダイアログに表示します。

形式は拡張子ではなくファイルの内容で自動判定します。
初期の出力先はWinIPSと同様、パッチのフォルダーに「パッチ名＋元ROMの拡張子」です。
同名のファイルがある場合は `_2` などを付けます。画面には出力先の欄やログ欄はありません。
元ROM・パッチ・既存ファイルは上書きしません。処理に失敗した場合は出力を公開しません。

BPS / UPSは元ROMのサイズ・CRC32、パッチ自体のCRC32、出力ROMのCRC32を検証します。
UPSは同じパッチで逆方向の適用もできます。
IPSには元ROMのサイズやCRC32情報がありません。正しいROMを自分で選択する必要があります。
ROMヘッダーの自動除去や付け足しは行いません。BPSの比較テストではFlipsの `--exact` を使用しています。

## パッチ作成

1. 「操作」から「BPSパッチ作成」「UPSパッチ作成」「IPSパッチ作成」を選択します。
2. 変更前のROMと変更後のROMを指定します。
3. 「作成」を押します。変更後ROMのフォルダーに「変更後ROM名＋パッチの拡張子」で保存します。

作成の選択肢はBPS、UPS、IPSの順です。形式を切り替えても選択済みのROMは保持します。
作成したパッチをメモリー上で再適用し、変更後ROMとバイト単位で一致することを検証してから保存します。

| 形式 | 元ROMの検証 | 16 MiB → 32 MiB | 用途 |
| --- | --- | --- | --- |
| BPS | サイズ・CRC32 | 対応 | 通常の配布用。初期選択 |
| UPS | サイズ・CRC32 | 対応 | NUPSとの互換、逆適用 |
| IPS | なし | 非対応 | 従来のIPSツールとの互換 |

16MBと32MBは、GBA ROMで一般的な16 MiB（16,777,216 bytes）と32 MiB（33,554,432 bytes）を指します。
BPS / UPSはパッチに記録された出力サイズに従って自動で拡張・縮小します。あらかじめ元ROMを拡張する必要はありません。

IPSのレコード開始位置は24ビットです。範囲外の変更や表現できない縮小はエラーにしてBPS / UPSを案内します。
32 MiBのROMでも、サイズを変えずIPSで表現できる低い位置だけを変更するパッチは作成・適用できます。
RLE、EOF後のサイズ指定、WinIPSが受け付ける `0x454F46` のレコードにも対応しています。

BPSの作成は同じ位置のデータ参照と繰り返し圧縮を使う高速な方式です。
移動したデータを探すFlipsのdelta方式と同じ圧縮率は保証しません。適用はBPSの全4命令に対応します。
ファイルはメモリー上で処理し、各入力・出力のサイズ上限は512 MiBです。

## コマンドライン

```powershell
# 既存ファイルは上書きしません。終了コードは成功0、失敗1。
MulchPatcher.exe --apply "hack.bps" "original.gba" "patched.gba"
MulchPatcher.exe --create bps "original.gba" "modified.gba" "hack.bps"
MulchPatcher.exe --create ups "original.gba" "modified.gba" "hack.ups"
MulchPatcher.exe --create ips "original.gba" "modified.gba" "hack.ips"
```

パッチをEXEへドラッグ＆ドロップしてGUIを開くこともできます。
PowerShellで終了まで待ちたい場合は `Start-Process -Wait -PassThru` を使ってください。

## ソースからビルド

Visual Studio 2022またはBuild Toolsの「.NETデスクトップ開発」と.NET Framework 4.8ターゲットパックが必要です。
NuGetパッケージや外部DLLへの依存はありません。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Release用EXEを `dist/` に生成し、テストを実行します。
GitHub ActionsでもWindows上でビルド・テストし、`v*` タグでReleaseに成果物を添付します。

テストは生成したダミーデータだけを使います。ランダムな差分、全BPS命令、CRC不一致、破損した入力、IPS境界、
UPS逆適用、16→32 MiB、読み取り専用ファイル、日本語パス、元ファイル保持を検証します。
Flips・winIPS・NUPSとの相互互換テストもローカルで実施しています。

## 調査・ライセンス

動作を調べた既存ツールとフォーマット仕様は [docs/research.md](docs/research.md) にまとめています。
アプリ本体はC#で実装し、Flips・WinIPS・NUPSのバイナリは配布物に含めません。

Copyright (c) 2026 pit-true. [GPL-3.0-or-later](LICENSE).
