Mulch Patcherの初回リリースです。

- WinIPSのような日本語UIでIPS / BPS / UPSを適用・作成。
- 元ROMを保持し、新しいファイルへ出力。既存ファイルも上書きしません。
- BPS / UPSは16 MiB→32 MiBの拡張と縮小に対応。
- BPS / UPSは元ROM・パッチ・出力ROMのCRC32を検証。
- 作成したパッチを再適用し、変更後ROMとの完全一致を確認して保存。
- ドラッグ＆ドロップとコマンドラインに対応。
- Flips・WinIPS・NUPSとの相互互換テストを実施。

`MulchPatcher.exe` をダウンロードして実行してください。
Windows 10 / 11、.NET Framework 4.8。追加DLLは不要です。

作成形式の初期値はBPSです。IPSでは16 MiB→32 MiBを表現できません。BPS / UPSを使用してください。
