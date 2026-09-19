# Mi Band Authentication Protocol

This document describes, in detail, how the authentication ("pairing") between a
Xiaomi/Huami Mi Band and this application works. It covers both supported
protocol generations:

* **Mi Band 2 / Mi Band 3** – anonymous, key-less pairing
* **Mi Band 4 / Mi Band 5** (and later) – pairing with a vendor bound key

The protocol implementation lives in
[`MiBandHR.Core/Devices/MiBandDeviceBase.cs`](../MiBandHR.Core/Devices/MiBandDeviceBase.cs),
[`MiBand2_3_Device.cs`](../MiBandHR.Core/Devices/MiBand2_3_Device.cs) and
[`MiBand4_Device.cs`](../MiBandHR.Core/Devices/MiBand4_Device.cs).

> References and prior art:
>
> * [Mi Band 2 Authentication by leojrfs](https://leojrfs.github.io/writing/miband2-part1-auth/) (protocol reverse engineering)
> * [creotiv/MiBand2](https://github.com/creotiv/MiBand2) and
>   [leojrfs/miband2](https://github.com/leojrfs/miband2) (Python implementations)
> * [How I hacked my Xiaomi MiBand 2 by Andrey Nikishaev](https://medium.com/machine-learning-world/how-i-hacked-xiaomi-miband-2-to-control-it-from-linux-a5bd2f36d3ad)
> * [argrento/huami-token](https://github.com/argrento/huami-token) (auth key extraction)

---

## 1. Terminology

| Term | Meaning |
|------|---------|
| **Host** | The computer running this application (the GATT client / central). |
| **Band** | The Mi Band (the GATT server / peripheral). |
| **Auth key** | A 16 byte (128 bit) secret shared between host and band. Used as the AES key during the handshake. |
| **Auth service** | GATT service `0000fee1-0000-1000-8000-00805f9b34fb` (Huami private service). |
| **Auth characteristic** | GATT characteristic `00000009-0000-3512-2118-0009af100700` inside the auth service. All authentication traffic goes through this single characteristic. |

All multi-byte values are sent in order as listed (the protocol is effectively
big-endian for the command layout, there are no multi-byte numeric fields).

---

## 2. The authentication characteristic

Authentication is a small request/response protocol multiplexed over a single
GATT characteristic. Two payload families are used:

* **Commands** sent from host to band start with the command id (`0x01`,
  `0x02`, `0x03`), followed by `0x08` and an optional payload.
  `0x08` looks like a flag meaning "payload present" (every command with a
  payload uses it; the empty `0x02` request uses just `[0x02, 0x08]` too, so
  it is best understood as a constant part of the command frame).
* **Responses** sent from band to host start with `0x10`, followed by the
  command id they answer to, followed by a status byte:

  | Status byte | Meaning |
  |-------------|---------|
  | `0x01` | Success |
  | `0x04` | Failure (e.g. wrong key, wrong encrypted number) |

So a response frame looks like `[0x10, <command>, <status>, <optional payload>]`.

---

## 3. The three-step handshake

Both device generations run the *same* three-step handshake. The only
difference is where the AES key comes from (see [section 4](#4-where-the-key-comes-from)).

```text
Host                                    Band
  |                                       |
  | -- 1. [01 08 <16 byte key>] --------> |
  | <-------- [10 01 01] ---------------  |  key accepted
  | -- 2. [02 08] ----------------------> |  request random number
  | <-------- [10 02 01 <16 byte RN>] --  |  random challenge
  | -- 3. [03 08 <AES(key, RN)>] -------> |  encrypted response
  | <-------- [10 03 01] ---------------  |  authenticated!
```

### Step 1 – Send the key

Host → Band:

```
[0x01, 0x08, K0, K1, ..., K15]
```

The 18 byte frame contains the 16 byte authentication key. The band answers
with `[0x10, 0x01, 0x01]` when it accepts the key (or `[0x10, 0x01, 0x04]`
when it rejects it, which can happen on Mi Band 4+ if the key does not match
the one stored in the band's flash).

In code this is
[`MiBandDeviceBase.Authenticate()`](../MiBandHR.Core/Devices/MiBandDeviceBase.cs):

```csharp
characteristic.CharacteristicValueChanged += OnAuthenticateNotify;
await characteristic.StartNotificationsAsync();
await Write(characteristic, Combine(new byte[] { 0x01, 0x08 }, GetAuthenticationKey()));
```

> **Important:** notifications on the auth characteristic **must** be enabled
> before sending the key, otherwise the band's responses are lost.

### Step 2 – Request the random challenge

On `[0x10, 0x01, 0x01]` the host asks the band for a random number:

Host → Band:

```
[0x02, 0x08]
```

The band replies:

```
[0x10, 0x02, 0x01, R0, R1, ..., R15]
```

where `R0..R15` is a 16 byte random challenge. It changes on every connection,
which makes the authentication resistant against replay attacks: recording an
old "encrypted number" frame does not help an attacker, because the challenge
will be different next time.

### Step 3 – Answer the challenge

The host encrypts the 16 byte challenge with **AES-128 in ECB mode, no
padding**, using the authentication key from step 1:

```
encrypted = AES128_ECB_encrypt(key, challenge)
```

and sends it back:

Host → Band:

```
[0x03, 0x08, E0, E1, ..., E15]
```

ECB with a single 16 byte block is deterministic by nature, which is exactly
what the protocol needs: both sides compute the same ciphertext for the same
`(key, challenge)` pair. Because the challenge is fresh random data on every
connection, the usual ECB weakness (equal plaintext blocks produce equal
ciphertext blocks) does not matter here.

The band performs the same computation and compares the result. If they match,
it answers:

```
[0x10, 0x03, 0x01]
```

and the device is authenticated. From this point on, all restricted
characteristics (heart rate control point, sensor data, steps, …) become
accessible until the connection drops. Authentication is **per connection**:
every reconnect requires a new handshake.

In code the response handling is
[`MiBandDeviceBase.OnAuthenticateNotify()`](../MiBandHR.Core/Devices/MiBandDeviceBase.cs):

```csharp
switch (value[1]) {
    case 0x01 when value[2] == 0x01:                       // key accepted
        _ = Write(characteristic, new byte[] { 0x02, 0x08 });
        break;
    case 0x02: {                                           // challenge received
        byte[] number = value.Skip(3).ToArray();
        byte[] encryptedNumber = EncryptAuthenticationNumber(number);
        _ = Write(characteristic, Combine(new byte[] { 0x03, 0x08 }, encryptedNumber));
        break;
    }
    case 0x03 when value[2] == 0x01:                       // success
        Status = DeviceStatus.ONLINE_AUTH;
        break;
}
```

and the AES helper:

```csharp
private byte[] EncryptAuthenticationNumber(byte[] number)
{
    using Aes aes = Aes.Create();
    aes.Key = GetAuthenticationKey();
    aes.Mode = CipherMode.ECB;
    aes.Padding = PaddingMode.None;
    using ICryptoTransform encryptor = aes.CreateEncryptor();
    return encryptor.TransformFinalBlock(number, 0, number.Length);
}
```

### Full example frame sequence (Mi Band 4)

Key: `30313233343536373839616263646566` (hex)

```text
> 01 08 30 31 32 33 34 35 36 37 38 39 61 62 63 64 65 66
< 10 01 01
> 02 08
< 10 02 01 7A 4C 1F 9E 02 B8 D3 66 91 00 A5 5E 28 C4 7B 0D
> 03 08 9F 12 88 40 6C BD 51 03 72 EA 98 0D 4E 3C 55 21
< 10 03 01
```

---

## 4. Where the key comes from

### 4.1 Mi Band 2 / 3 – random key pairing

Mi Band 2/3 do not have a vendor-account bound key. After a factory reset the
band accepts the **first key it sees** and stores it. This application simply
generates a fresh random 16 byte key on every connection
([`MiBand2_3_Device.GetAuthenticationKey()`](../MiBandHR.Core/Devices/MiBand2_3_Device.cs)):

```csharp
protected override byte[] GetAuthenticationKey()
{
    return SHA256.HashData(Guid.NewGuid().ToByteArray()).Take(16).ToArray();
}
```

Consequences:

* The band must be **unpaired from any other device/app** before connecting,
  otherwise the stored key will not match and step 1 fails.
* During the first handshake the band asks the user to **touch the physical
  button** to confirm pairing. If the button is not touched in time, the band
  answers with a failure status or simply disconnects.
* Because the key is random per session, losing it is not a problem – the band
  only needs to be reset/unpaired to accept a new one.

### 4.2 Mi Band 4 / 5 – vendor bound key

Starting with Mi Band 4, the band is bound to the Huami/Xiaomi (Zepp Life /
Mi Fit) account it was first paired with. The 16 byte key is created by the
vendor cloud during the official pairing flow and stored in both the band and
the vendor app. Third-party apps must obtain it out-of-band:

* [freemyband.com](http://www.freemyband.com/) – online extraction from a Zepp Life / Mi Fit account
* [argrento/huami-token](https://github.com/argrento/huami-token) – offline extraction from a rooted phone / app backup

The key is presented to the user as a **32 character hex string**
(e.g. `0123456789abcdef0123456789abcdef`), which is converted to the raw
16 byte key in
[`MiBand4_Device.EncodeKey()`](../MiBandHR.Core/Devices/MiBand4_Device.cs):

```csharp
private static byte[] EncodeKey(string key)
{
    if (key.Length == 32) {
        return Enumerable.Range(0, key.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(key.Substring(x, 2), 16))
            .ToArray();
    }
    // Fallback: derive a deterministic 16 byte key from arbitrary passphrases.
    return SHA256.HashData(Encoding.UTF8.GetBytes(key)).Take(16).ToArray();
}
```

Consequences:

* The band does **not** need to be factory reset; the key stays valid across
  reconnects and even firmware updates (usually).
* If the band is re-paired with the official app afterwards, the vendor app
  may rotate the key, invalidating the extracted one.
* If the key is wrong, the band answers `[0x10, 0x01, 0x04]` in step 1 (or
  `[0x10, 0x03, 0x04]` in step 3, depending on firmware) and disconnects.
* Button confirmation is **not** required on Mi Band 4/5 – possession of the
  key is considered proof of ownership.

---

## 5. Failure handling and common pitfalls

| Symptom | Likely cause |
|---------|--------------|
| No response after sending `[0x01, 0x08, key]` | Notifications were not enabled on the auth characteristic before writing. |
| `[0x10, 0x01, 0x04]` | Wrong key (Mi Band 4+) or band already paired to another device (Mi Band 2/3). |
| `[0x10, 0x03, 0x04]` | Challenge encrypted with the wrong key, or a race condition: another write to the auth characteristic happened between challenge and response. |
| Band disconnects right after step 1 | On Mi Band 2/3 the button confirmation timed out. Touch the button when the band vibrates/shows the pairing prompt. |
| Random disconnects a few seconds after auth | Some firmwares expect the client to start using the device quickly; enable heart rate notifications promptly after authentication. |
| Nothing works on a "new" Mi Band 2/3 | The band still stores the key of a previous pairing. Factory reset it (or unpair it from the previous phone) so it accepts a fresh key. |

General notes:

* The handshake is time sensitive. Sending the next step only after receiving
  the previous response (as done in `OnAuthenticateNotify`) is the most
  reliable approach.
* The auth characteristic is write-with-response. Using write-without-response
  can silently lose frames on some Bluetooth stacks.
* Everything described here is independent of the Bluetooth transport. The
  `MiBandHR.Core.Bluetooth` abstraction only needs to provide GATT reads,
  writes and notifications – any BLE stack that can do that (InTheHand,
  Plugin.BLE, native Windows APIs, …) can drive the protocol.

---

## 6. What authentication unlocks

Before a successful handshake, most Huami-specific characteristics reject
writes. After `ONLINE_AUTH` is reached, the application:

1. (Mi Band 2/3 only) enables the sensor via the sensor service
   `0000fee0-...` / characteristic `00000001-...` with `[0x01, 0x03, 0x19]`.
2. Subscribes to heart rate notifications on the standard Heart Rate
   Measurement characteristic `0x2A37`.
3. Starts a continuous measurement through the Heart Rate Control Point
   `0x2A39`:
   ```
   [0x15, 0x02, 0x00]   stop one-shot measurement
   [0x15, 0x01, 0x00]   stop continuous measurement
   [0x15, 0x01, 0x01]   start continuous measurement
   ```
4. Sends a keep-alive ping `[0x16]` every 12 seconds, because the band stops
   the continuous measurement after ~15 s of inactivity.

Heart rate values arrive as two byte notifications on `0x2A37`: the first byte
is a flags field (`0x00` = 8 bit heart rate value follows), the second byte is
the heart rate in BPM.
