# Flips・WinIPS・NUPSとパッチ形式の調査

調査日: 2026-10-04。

## 調査した手元のツール

- Floating IPS v198: `flips.exe --help` でIPS / BPS / UPS適用、IPS / BPS作成を確認。
  FlipsはBPS専用ではない。作成の初期方式はBPS delta。BPS linearも選択できる。
  `--exact` はSMCヘッダーの自動除去を止め、ファイルそのものを比較する。
  FlipsはGPL v3.0以降。配布物にはFlipsを含めない。
- WinIPS 0.71: 添付の `winips.txt` を確認。
  「操作」を選び2ファイルを指定するUI。IPS適用時は元をコピーし、パッチと同じフォルダーに
  `パッチ名.元ファイルの拡張子` を作る。作成時は変更前・変更後からIPSを作る。
  ドラッグ＆ドロップ、CLIの `/a` と `/c`、RLE、EOF後のサイズ指定に対応。
  0.66からEOFと同値のオフセットを持つレコードにも対応。
  0.68の「変更後が16MBを超えてもIPS作成可能な場合」は、IPSのアドレス制限がなくなったという意味ではない。
- NUPS 1.4: UPSの適用・作成。公開ソースの既定の適用処理は元ファイルへ書き戻す。
  Mulch Patcherでは元ファイルを読み取るだけで、全形式を別ファイルに出力する。

## フォーマット

IPSは `PATCH` ヘッダー、位置・長さ・データのレコード、`EOF` の構成。
位置は3-byte big endian、長さは2-byte big endian。長さ0はRLE。
EOF後に3-byteの出力サイズを置く拡張がある。入力のサイズ・チェックサムを持たない。
16 MiB→32 MiBの拡張はこの形式では表現できない。

BPSは `BPS1`、可変長整数の入力サイズ・出力サイズ・メタデータ長、命令、3個のCRC32。
SourceRead、TargetRead、SourceCopy、TargetCopyの4命令で出力を順番に作る。
TargetCopyは既存の出力を参照し、重なるコピーで繰り返しを表現できる。
出力サイズを直接記録するので16 MiBの入力と32 MiBの出力を別々に指定できる。

UPSは `UPS1`、可変長整数の入力サイズ・出力サイズ、相対位置とXOR差分、3個のCRC32。
サイズが違う領域はゼロで補ってXORを定義する。同じパッチを逆方向にも適用できる。
終端の0-byteは論理的な位置を1進める。変更がファイル末尾に届く場合も終端が必要。

BPS / UPSのCRC32は入力、出力、パッチを検証する。パッチのCRC32対象は自身の最後の4-byteを除く全データ。
拡張後に未変更領域だけコピーすると16 MiBで切れてしまうので、Mulchでは記録された出力サイズを確保する。

## 参照先

- [Flips公開ソース](https://github.com/Alcaro/Flips)
- [byuuのBPS仕様（public domain、Flips内の写し）](https://github.com/Alcaro/Flips/blob/master/bps_spec.md)
- [FlipsのUPS処理](https://github.com/Alcaro/Flips/blob/master/libups.cpp)
- [FlipsのIPS処理](https://github.com/Alcaro/Flips/blob/master/libips.cpp)
- [NUPS公開ソース](https://github.com/TimoVesalainen/Nintenlord-UPS-patcher)
- [WinIPS作者のサイト](http://smblabo.web.fc2.com/)

実装はC#。検証時のみ外部ツールをCLI・公開APIで呼び出し、両方向の出力を比較した。
外部ツールのEXE・DLL、ユーザーのROM、実際のROMを含むパッチはリポジトリにもReleaseにも含めない。

## 検証記録

1. 250セットのランダムデータ×3形式の作成・再適用。UPSは250セットの逆適用も確認。
2. BPS全4命令、負の相対位置、重なるTargetCopy、メタデータ。
3. IPSの通常レコード、RLE、拡張・縮小、EOFと同値の位置、レコード分割。
4. 元ROM・パッチ・出力のCRC32不一致、途中で切れたデータ、参照範囲外、整数オーバーフロー。
5. 16 MiB→32 MiBをBPS・UPSで作成・適用し、全バイト一致を確認。UPS逆適用も確認。
6. 同じ32 MiB入力の低い位置だけをIPSで変更した場合に、末尾まで保持することを確認。
7. FlipsでMulch生成のBPS・UPSを適用し、16 MiB→32 MiBの一致を確認。
8. Flips生成のBPS deltaをMulchで適用。IPSもFlips・WinIPSと相互確認。
9. NUPSとUPS作成・適用を相互確認。
10. 読み取り専用元ROM、日本語・空白入りパス、既存ファイル拒否、失敗時に出力しないことを確認。
11. 実際のEXEのCLI作成・適用、GUIのモード切替・形式選択・作成・適用・エラー表示を確認。

テストデータは実行時にOSの一時フォルダーへ生成し、失敗調査用に残す。実際のROMは使わない。
