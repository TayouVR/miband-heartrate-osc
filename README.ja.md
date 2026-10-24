# Mi Band Heartrate

Windows・Linux・macOS で Mi Band デバイスの心拍数を取得します。

![miband heartrate](https://github.com/Eryux/miband-heartrate/raw/master/mibandheatrate-screen.png "Mi Band Heartrate screen")
![miband heartrate newer version](https://github.com/TayouVR/miband-heartrate-osc/assets/31988415/9f7684bb-03ce-4dff-960e-78cacb0a31b4)

### サポートしているデバイス

* Mi Band 2
* Mi Band 3
* Mi Band 4
* Mi Band 5


### 動作環境

* .NET 10 SDK/runtime ([download](https://dotnet.microsoft.com/download))
* Windows 10 1703+, Linux (BlueZ + デスクトップ環境) または macOS
* Bluetooth 4.0/4.2 BLE に対応した Bluetooth アダプタ

実行:

```bash
dotnet run --project MiBandHR.Avalonia/MiBandHR.Avalonia.csproj
```


### 使い方

* 最新のリリースからzipをダウンロード・解凍してください。

#### Mi Band 2 or Mi Band 3

* Mi Band 2/3を全てのデバイスからペアリング解除してください

* Mi Band 2/3をPCにペアリング・接続してください

* `MiBandHR.Avalonia`を起動してください

* `Manual Connect`ボタンを押してください

* `Select your device`から`Mi Band 2`や`Mi Band 3`を選択してください

* `Select the model`から`Mi Band 2/3`を選択してください

* `Connect`ボタンを押してください

* デバイスが正しく接続・認証されました、`Start`ボタンを押してください

#### Mi Band 4/5

* デバイス用の認証キーを取得してください、詳しくは ([freemyband.com](http://www.freemyband.com/))

* Mi Band 4/5をPCにペアリング・接続してください

* `MiBandHR.Avalonia`を起動してください

* `Manual Connect`ボタンを押してください

* `Select your device`から`Mi Band 4`や`Mi Band 5`を選択してください

* `Select the model`から`Mi Band 4/5`を選択してください

* `Connect`ボタンを押してください

* 新しいウィンドウが出るので、そこに認証キーを入力して`Ok`を押してください

* デバイスが正しく接続・認証されました、`Start`ボタンを押してください


### 自動接続

TOML設定ファイル（`config.toml`、初回実行時に作成されます）の`[Device]`セクションを設定することで自動接続をすることができるようになります。

自動接続を有効にした場合、`Manual Connect`ボタンが`Auto Connect`ボタンに変わります。

|キー|説明|備考|
|-|-|-|
|useAutoConnect|自動接続するかどうか|自動接続を有効にする場合は`true`|
|autoConnectDeviceVersion|デバイスのバージョン|MiBand 4の場合は`4`、MiBand 5の場合は`5`|
|autoConnectDeviceName|Bluetoothデバイスの名前|基本的にはMiBand 5の場合は`MiBand 5`|
|autoConnectDeviceAuthkey|32文字の認証キー|例:`0123456789abcdef0123456789abcdef`|

設定例
``` toml
[Device]
useAutoConnect = true
autoConnectDeviceVersion = "4"
autoConnectDeviceName = "Mi Band 4"
autoConnectDeviceAuthkey = "0123456789abcdef0123456789abcdef"
```


### オプション

* **Export data in CSV file :** 日付、時刻、心拍数を含むCSVファイルに記録します。

* **Write realtime date in text file :** テキストファイル内に心拍数の値を継続的に書き込みます。

* **Send OSC to VRChat :** OSCを使用して心拍や心拍数を送信します。詳細な仕様は次の章を参照してください。

* **Continuous mode :** Mi Bandの心拍センサーは「1回のみ取得」「連続取得」の2個のモードがあります。「1回のみ取得」のモードでは取得まで5\~10秒かかり、心拍数の値を取得してから停止します。「連続取得」のモードでは、2\~5秒ごとに心拍数の値が更新されます。

## VRChat
### Addresses

"Send OSC to VRChat"が有効になっている場合はOSCを送信し続けます。

|Addresss|Value Type|Description|
|-|-|-|
|/avatar/parameters/HR/Int|Int|心拍数（毎分） [0, 255]|
|/avatar/parameters/HR/Float|Float|正規化された心拍数（毎分） ([0, 255] -> [-1, 1])|
|/avatar/parameters/HR/HalfFloat|Float|正規化された心拍数（毎分） ([0, 255] -> [0, 1]) <br> これはラジアルを使用したシェイプキーの操作をするときに有効です。[参考リンク](https://note.com/citron_vr/n/n7d54ebaebd83)|
|/avatar/parameters/HR/Beat|Bool|True : QRS時間（ドックンの時間）(心拍の1/5の時間としています) <br> False : それ以外の時間|
|/avatar/parameters/HR/BeatToggle|Bool|心拍ごとに値が反転します|
|/avatar/parameters/HR/Connected|Bool|True if device is connected and sending data|
|/avatar/parameters/HR/Min|Int|Minimum heart rate per min in session [0, 255] (not implemented yet)|
|/avatar/parameters/HR/Max|Int|Maximum heart rate per min in session [0, 255] (not implemented yet)|

#### Old Addresses

|Addresss|Value Type|Description|
|-|-|-|
|/avatar/parameters/HeartRateInt|Int|心拍数（毎分） [0, 255]|
|/avatar/parameters/HeartRate3|Int|HeartRateIntと同じ|
|/avatar/parameters/HeartRateFloat|Float|正規化された心拍数（毎分） ([0, 255] -> [-1, 1])|
|/avatar/parameters/HeartRate|Float|HeartRateFloatと同じ|
|/avatar/parameters/floatHR|Float|HeartRateFloatと同じ - for compatibility with other apps|
|/avatar/parameters/HeartRateFloat01|Float|正規化された心拍数（毎分） ([0, 255] -> [0, 1]) <br> これはラジアルを使用したシェイプキーの操作をするときに有効です。[参考リンク](https://note.com/citron_vr/n/n7d54ebaebd83)|
|/avatar/parameters/HeartRate2|Float|HeartRateFloat01と同じ|
|/avatar/parameters/HeartBeatInt|Int|1 : QRS時間（ドックンの時間）(心拍の1/5の時間としています) <br> 0 : それ以外の時間|
|/avatar/parameters/HeartBeatPulse|Bool|True : QRS時間（ドックンの時間）(心拍の1/5の時間としています) <br> False : それ以外の時間|
|/avatar/parameters/HeartBeatToggle|Bool|心拍ごとに値が反転します|
|/avatar/parameters/isHRConnected|Bool|True if device is connected and sending data - for compatibility with other apps|

### ビルドに必要なもの

* A suitable IDE, like Visual Studio 2022 or Jetbrains Rider
* .Net 10.0 SDK


### Build

* このリポジトリをクローンしてください

* `MiBand-Heartrate.slnx`のソリューションを開いてください

* ソリューションを右クリックしてビルド


### 便利なリンク
* [huami-token](https://github.com/argrento/huami-token)
* [Microsoft GATT Documentation](https://docs.microsoft.com/fr-fr/windows/uwp/devices-sensors/bluetooth-low-energy-overview)
* [Mi Band 2 Authentication by leojrfs](https://leojrfs.github.io/writing/miband2-part1-auth/#reference), [python](https://github.com/leojrfs/miband2)
* https://github.com/creotiv/MiBand2
* [How I hacked my Xiaomi MiBand 2 fitness tracker — a step-by-step Linux guide by Andrey Nikishaev](https://medium.com/machine-learning-world/how-i-hacked-xiaomi-miband-2-to-control-it-from-linux-a5bd2f36d3ad)

VRChat
* [vard88508/vrc-osc-miband-hrm: Mi Band/Amazfit heart rate monitor with OSC integration for VRChat](https://github.com/vard88508/vrc-osc-miband-hrm)
* [ラジアルでのシェイプキー操作方法｜みかんねここ｜note](https://note.com/citron_vr/n/n7d54ebaebd83)
* [OSC HeartRateSendersドキュメントページ - おめが？日記_(2)](https://omega.hatenadiary.jp/entry/2022/02/27/035024)
* [【Mi スマートバンド(Mi Band) × VRChat OSC】自分の心拍数をアバターに表示する方法！ | Till0196のぼーびろく](https://till0196.com/post16907)


### Thirdparty licenses
OscCore | [MIT Licence](https://github.com/tilde-love/osc-core/blob/master/LICENSE)


### License

MIT License
