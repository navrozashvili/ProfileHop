# HID++ protocol verification

Every function number and byte layout used by `src/ProfileHop.Core/Hidpp/` checked against open-source
implementations before coding. Device replies (milestones 3–4) still have the final word.

Sources (fetched 2026-11-08, pinned to these commits):

- libratbag `8235b5bb6032dea15901ab58a8218956f4494d08`
  - [`src/hidpp20.c`](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/hidpp20.c)
  - [`src/hidpp20.h`](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/hidpp20.h)
  - [`src/hidpp-generic.h`](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/hidpp-generic.h)
  - [`src/driver-hidpp20.c`](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/driver-hidpp20.c)
- Solaar `e7304c4c451cc9bb4f206a914844525e67856a28`, all under `lib/logitech_receiver/`
  - [`base.py`](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/base.py)
  - [`hidpp20.py`](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/hidpp20.py)
  - [`hidpp20_constants.py`](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/hidpp20_constants.py)
  - [`hidpp10_constants.py`](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/hidpp10_constants.py)
  - [`settings_templates.py`](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/settings_templates.py)
  - [`notifications.py`](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/notifications.py)

Line numbers below refer to those commits. In libratbag the function is written as the full byte 3 value
(`0x10` = function 1 with swId 0); in Solaar as the low byte of the 16-bit `request_id` (same encoding).

## Function numbers

| Function | Plan | libratbag | Solaar | Match |
| --- | --- | --- | --- | --- |
| Root (feature index 0) | index 0 | `HIDPP_PAGE_ROOT_IDX 0x00` hidpp20.c:229 | `request(0x0000, …)` hidpp20.py:161, 379 | yes |
| Root getFeature | 0 | `CMD_ROOT_GET_FEATURE 0x00` hidpp20.c:231 | `request(0x0000, pack("!H", feature))` hidpp20.py:379 | yes |
| Root getProtocolVersion | 1 | `CMD_ROOT_GET_PROTOCOL_VERSION 0x10` hidpp20.c:232 | ping `request_id = 0x0010 \| sw_id` base.py:820 | yes |
| FeatureSet id | 0x0001 | `HIDPP_PAGE_FEATURE_SET 0x0001` hidpp20.h:111 | `FEATURE_SET = 0x0001` hidpp20_constants.py:34 | yes |
| FeatureSet getCount | 0 | `CMD_FEATURE_SET_GET_COUNT 0x00` hidpp20.c:319 | `request(fs_index << 8)` hidpp20.py:165 | yes |
| FeatureSet getFeatureId | 1 | `CMD_FEATURE_SET_GET_FEATURE_ID 0x10` hidpp20.c:320 | `feature_request(FEATURE_SET, 0x10, index)` hidpp20.py:316 | yes |
| OnboardProfiles id | 0x8100 | `HIDPP_PAGE_ONBOARD_PROFILES 0x8100` hidpp20.h:581 | `ONBOARD_PROFILES = 0x8100` hidpp20_constants.py:159 | yes |
| getProfilesDescription | 0 | `CMD_ONBOARD_PROFILES_GET_PROFILES_DESCR 0x00` hidpp20.c:1783 | `feature_request(ONBOARD_PROFILES, 0x00)` hidpp20.py:1665 | yes |
| setOnboardMode | 1 | `…_SET_ONBOARD_MODE 0x10` hidpp20.c:1784 | `0x10, b"\x01"` / `b"\x02"` settings_templates.py:573-575 | yes |
| getOnboardMode | 2 | `…_GET_ONBOARD_MODE 0x20` hidpp20.c:1785 | `0x20` settings_templates.py:564 | yes |
| setCurrentProfile | 3 | `…_SET_CURRENT_PROFILE 0x30` hidpp20.c:1786 | `0x30, data_bytes` settings_templates.py:576 | yes |
| getCurrentProfile | 4 | `…_GET_CURRENT_PROFILE 0x40` hidpp20.c:1787 | `0x40` settings_templates.py:566 | yes |
| memoryRead | 5 | `…_MEMORY_READ 0x50` hidpp20.c:1788 | `0x50` hidpp20.py:1649, 1703 | yes |
| memory write (6, 7, 8) | never used | `0x60`, `0x70`, `0x80` hidpp20.c:1789-1791 | `0x60` hidpp20.py:1722 | yes — **not implemented** |
| getCurrentDpiIndex | 11 | `…_GET_CURRENT_DPI_INDEX 0xb0` hidpp20.c:1792 | — | yes (libratbag only) |
| setCurrentDpiIndex | 12 | `…_SET_CURRENT_DPI_INDEX 0xc0` hidpp20.c:1793 | — | yes (libratbag only) |

## Framing

| Item | Plan | Source | Match |
| --- | --- | --- | --- |
| Short report | ID 0x10, 7 bytes | `HIDPP_SHORT_MESSAGE_ID = 0x10`, `SHORT_MESSAGE_SIZE = 7` base.py:90, 95 | yes |
| Long report | ID 0x11, 20 bytes | `HIDPP_LONG_MESSAGE_ID = 0x11`, `_LONG_MESSAGE_SIZE = 20` base.py:91, 96; `pack("!BB18s", 0x11, devnumber, data)` base.py:454 | yes |
| Byte 3 | `(function << 4) \| swId` | libratbag: "4 MSB: subcommand, 4 LSB: SW identifier" hidpp20.c:129-137; Solaar `(request_id & 0xFFF0) \| sw_id` base.py:696 | yes |
| Software ID | 0x0B | Solaar `SOLAAR_SOFTWARE_ID = 0x0B` base.py:910 (libratbag uses 0x08, hidpp20.c:131). Any non-zero value works; ProfileHop rotates 0x1..0xF per send so stale replies are dropped. | yes |
| Notifications | swId 0 | `# HID++ 2.0 feature notifications have the SoftwareID 0` / `address & 0x0F == 0x00` base.py:629-630 | yes |
| Reply match | same dev index, feature index, byte 3 | libratbag `sub_id == … && address == …` hidpp20.c:174-176 (device index checked by the hidraw node); Solaar `reply_devnumber == devnumber` and `reply_data[:2] == request_data[:2]` base.py:729, 769 | yes |
| Device indexes | 0x01–0x06 paired, 0xFF wired/receiver | Solaar: receiver requests use devnumber 0xFF (base.py:698), libratbag `device->index` | 0xFF confirmed; the 0x01–0x06 range is from the plan (receiver.py, which enumerates paired slots, was not fetched) |

## Error replies

| Item | Plan | Source | Match |
| --- | --- | --- | --- |
| HID++ 2.0 error | byte 2 = 0xFF, byte 3 = orig feature index, byte 4 = orig byte 3, byte 5 = code | libratbag `sub_id == 0xff && address == msg->sub_id && parameters[0] == msg->address`, code `parameters[1]` hidpp20.c:179-183; Solaar `reply_data[:1] == b"\xff" and reply_data[1:3] == request_data[:2]`, code `reply_data[3]` base.py:747-749 | yes |
| HID++ 1.0 error | byte 2 = 0x8F, same layout | libratbag `__ERROR_MSG` (0x8F) on the same check hidpp20.c:179; Solaar `reply_data[:1] == b"\x8f"` (short report only) base.py:731-735 | yes |
| 2.0 codes | 1 Unknown, 2 InvalidArgument, 3 OutOfRange, 4 HwError, 6 InvalidFeatureIndex, 7 InvalidFunctionId, 8 Busy, 9 Unsupported | hidpp-generic.h:70-79; hidpp20_constants.py:281-290 | yes — **plus 5 = LogitechInternal** (missing from plan, added to the enum) |
| 1.0 codes relevant to probing | "mouse not reachable" | hidpp10_constants.py:129-141: 1 INVALID_SUB_ID (device is HID++ 1.0, e.g. the receiver itself), 4 CONNECTION_REQUEST_FAILED / 9 RESOURCE_ERROR (unreachable), 8 UNKNOWN_DEVICE (nothing paired at that index); Solaar ping base.py:847-854 | yes — every 0x8F becomes `DeviceUnreachableException` carrying the code; the probe moves to the next index for all of them |

## Parameter and reply layouts

| Call | Request params | Reply params | Source | Match |
| --- | --- | --- | --- | --- |
| getFeature | feature ID BE16 | [index, type, version], index 0 = not present | hidpp20.c:272-280; hidpp20.py:379-387 (`index`, `flags = [1]`, `version = [2]`) | yes |
| getProtocolVersion | 00 00 ping | [major, minor, ping echo] | Solaar `pack("!HBBB", request_id, 0, 0, ping)`, reply major `[2]`, minor `[3]`, echo `reply_data[4:5] == request_data[-1:]` base.py:821-836 (reply_data starts at frame byte 2); libratbag hidpp20.c:308-309 | yes |
| getCount | — | [count] **excluding Root**, so indexes 0..count are valid | hidpp20.c:392-395 ("count does not include the root feature"); hidpp20.py:175 `count[0] + 1` | yes (detail added) |
| getFeatureId | [index] | [id hi, id lo, type, version] | hidpp20.c:353-361; hidpp20.py:321-328 (`version = [3]`, `flags = [2]`) | yes |
| getProfilesDescription | — | [memory model, profile format, macro format, profile count, profile count OOB, button count, sector count, sector size BE16, mechanical layout, various info, …] | `struct hidpp20_onboard_profiles_info` hidpp20.h:894-906, BE conversion hidpp20.c:1869; Solaar `unpack("!BBB")` + `unpack("!BBBBHB")` hidpp20.py:1666-1669 | yes |
| getOnboardMode | — | [mode] 1 onboard, 2 host | hidpp20.c:1799-1800, 2068; settings_templates.py:564-565 | yes |
| setOnboardMode | [mode] | — | hidpp20.c:2081 | yes |
| setCurrentProfile | profile BE16 (00 nn) | — | libratbag `parameters[1] = index + 1` hidpp20.c:2196; Solaar writes the 2-byte profile sector settings_templates.py:576 | yes |
| getCurrentProfile | — | profile BE16, 1-based | Solaar `active[:2]` = profile sector settings_templates.py:566-567, notifications.py:408 | yes per Solaar — **see disagreement 1** |
| memoryRead | sector BE16, offset BE16 | 16 bytes | hidpp20.c:1898, 1907, 1914; hidpp20.py:1703 | yes |
| getCurrentDpiIndex | — | [index] | hidpp20.c:2183 | yes |
| setCurrentDpiIndex | [index] | — | hidpp20.c:2141; libratbag rejects index > 4 (hidpp20.c:2144) | yes |
| 0x8100 notification | — | function 0: profile BE16 in params[0..1]; function 1: DPI index in params[0] | notifications.py:397-408 | plan only says "notification → re-query"; we react to function 0 only |

## Profile directory (sector 0x0000)

| Item | Source | Decision |
| --- | --- | --- |
| Location | `HIDPP20_USER_PROFILES_G402 0x0000` hidpp20.c:1810; Solaar reads sector 0 hidpp20.py:1649 | sector 0x0000, matches plan |
| Entry | 4 bytes `[sector BE16, enabled, 0]` libratbag write_dict hidpp20.c:2523-2528; Solaar `unpack("!HB")` hidpp20.py:1655, `to_bytes` hidpp20.py:1689-1691 | matches plan |
| End marker | `HIDPP20_PROFILE_DIR_END 0xFFFF` hidpp20.c:1813, 2825; Solaar `while chunk[0:2] != b"\xff\xff"` hidpp20.py:1654 | matches plan |
| Enabled | libratbag `!!d[HIDPP20_PROFILE_DIR_ENABLED]` (byte 2, any non-zero) hidpp20.c:1814, 2838; Solaar uses the byte's truthiness settings_templates.py:587 | any non-zero byte 2 = enabled |
| Sector → profile number | libratbag: entry position i → profile i+1, and it expects `sector == 0x0000 \| (i+1)` (logs otherwise) hidpp20.c:2828-2836; Solaar: the profile number **is** the sector value — `choices_universe[sector]` = "Profile {sector}", and that value is what setCurrentProfile is sent settings_templates.py:553-555, 586-588 | **profile number = sector value** (Solaar). Both agree for well-formed directories. Entries with sector 0 or ≥ 0x0100 (ROM, "Read-Only Profile") are not reported as enabled user profiles. |
| Length bound | libratbag stops after `profile_count` entries (hidpp20.c:2820); reads past `sector_size - 16` fail with INVALID_ARGUMENT (hidpp20.c:1901-1906) | stop at FF FF, at `profile_count` entries, or after 16 chunks; last chunk offset clamped to `sector_size - 16` |
| CRC | libratbag reads the whole sector and ignores the directory if the CRC-CCITT is invalid, falling back to ROM profiles (hidpp20.c:2816-2844); Solaar falls back to ROM sector 0x0100 if the first entry is all 00 or all FF (hidpp20.py:1651-1653) | **not implemented** — we read only as many 16-byte chunks as the directory needs, no CRC, no ROM fallback. An empty/erased directory gives an empty enabled list (the tray can use its config override). |

## DPI slot count

Profile sector bytes 3..12 are five little-endian u16 DPI values (`uint16_t dpi[5]` hidpp20.c:1822, parsed
`get_unaligned_le_u16(&data[2 * i + 3])` hidpp20.c:2754; Solaar `unpack("<H", bytes[i*2+3 : i*2+5])` for i in 0..4
hidpp20.py:1556). `HIDPP20_DPI_COUNT 5` hidpp20.h:622. A slot whose DPI is 0 is disabled (driver-hidpp20.c:1221-1226).

`NextDpiSlot` picks the DPI layout by the profile format from getProfilesDescription (`ProfileSector.DpiSlots`):
the libratbag layout above for formats 1–5, the inferred layout below for 0x08. It reads only the 16-byte memoryRead
chunks covering the DPI list of the current profile's sector (one chunk for 1–5, two for 0x08; read-only) and moves
from getCurrentDpiIndex to the next slot in 0..4 whose DPI is non-zero, wrapping. If only the current slot is enabled
it returns it and sends **no** setCurrentDpiIndex. If no slot is non-zero, the sector is unreadable, or the format's
layout is unknown, it wraps over all 5 slots. The profile's sector is taken to equal its profile number (Solaar
convention, above).

## Profile format 0x08 (PRO X2 SUPERSTRIKE, inferred)

The PRO X2 SUPERSTRIKE reports memory model 0x01, **profile format 0x08**, sector size 255, 5 profiles + 1 ROM
profile. Neither libratbag (accepts 1–5) nor Solaar (rejects > 0x05) knows format 0x08, and reading it with the
libratbag layout gives wrong DPI slots (e.g. profile 4's disabled fifth entry looks enabled). The layout below is
**inferred from device dumps, not from libratbag/Solaar**: real sectors of profiles 1–5 and ROM 0x0101 captured
2026-11-08 with `profiles --raw` (first 0x30 bytes + names in `tests/fixtures/profiles-superstrike-format08-2026-11-08.txt`).
Every sector's stored CRC (CRC-CCITT seed 0xFFFF, last 2 bytes BE, as libratbag) was valid.

| Bytes | Meaning (inferred) | Evidence |
| --- | --- | --- |
| 0 | report-rate code, mapping unknown — shown raw, not interpreted | 05 (P1–2), 04 (P3–5), 03 (ROM) |
| 1 | unknown | 0x03 in every sector |
| 2 | default DPI index, 0-based | getCurrentDpiIndex byte 0 was 0 on P1 and 4 on P5 = byte 2 of the active profile |
| 3 | switched / shift DPI index, 0-based | equal to byte 2 in every user profile (ROM: 00 00) |
| 4..28 | five DPI entries of 5 bytes: `[X LE16, Y LE16, flag]`; X = 0 → entry disabled | P1 entry 0 = 400 (`90 01 90 01 02`), entries 1–4 all 00; P4 entries 0–3 = 1600, entry 4 = 0; ROM 800/1200/1600/2400/3200; names match entry 0's DPI |
| 0x26..0x2B | two 3-byte groups, probably per-button (left/right) HITS settings — shown raw, not interpreted | `08 09 0C 08 09 0C` on P1–4, `20 09 0C 20 09 0C` on P5, `14 08 0C …` on ROM; the user confirmed P1 and P5 differ in HITS click feel |
| 0xA0.. | profile name, UTF-16LE, NUL-terminated | "400", "600", "800", "1600", "3200" |
| last 2 | CRC-CCITT, BE | valid on every sector |

Used by `NextDpiSlot` (enabled = X ≠ 0) and by the `profiles` command, which labels the decode "INFERRED LAYOUT".
Nothing is ever written. Unverified: the flag byte, bytes 0–1, and whether byte 3 really is the DPI-shift slot.

## Profile sector header (`profiles` diagnostic)

| Item | Source | Decision |
| --- | --- | --- |
| Profile format ids | libratbag `HIDPP20_ONBOARD_PROFILES_PROFILE_TYPE_` G402 1, G303 2, G900 3, G915 4, G502X 5 hidpp20.c:1802-1806, accepted set hidpp20.c:2224-2228; Solaar returns nothing unless `memory == 0x01` and `profile <= 0x05` hidpp20.py:1666 | formats 1–5 decoded, anything else printed raw only |
| Layout | one `union hidpp20_internal_profile` for all five formats hidpp20.c:1816-1842: byte 0 report rate, 1 default DPI index, 2 switched DPI index, 3..12 `uint16_t dpi[5]`; parsed hidpp20.c:2746-2755. Solaar `OnboardProfile.from_bytes` same offsets hidpp20.py:1550-1556 | `ProfileSector.Decode` |
| Report rate | libratbag `1000 / max(1, report_rate)` hidpp20.c:2746 (byte is ms); Solaar prints "{report_rate} ms" hidpp20.py:1602 | Hz = 1000 / max(1, byte) |
| CRC | last 2 bytes, BE, CRC-CCITT seed 0xFFFF over the rest hidpp20.c:1921-1934, hidpp-generic.c:382-404; an invalid directory CRC makes libratbag use ROM profiles 0x0101.. instead hidpp20.c:2841-2868 | printed per sector, not enforced |
| ROM profiles | `HIDPP20_ROM_PROFILES_G402 0x0100`, profile i at `0x0100 + i` hidpp20.c:1811, 2866-2868 | `profiles` also reads sectors 0x0101..0x0100+ROM count (memoryRead only) |

Solaar's `OnboardProfilesVersion = 3` (hidpp20.py:1618) is the version of its YAML dump, not a device format id.

## Device name and type (0x0005) — added for choosing the mouse among several receivers

Not in the plan; added because the user's PC has two LIGHTSPEED receivers and keyboards can also have 0x8100.
Only Solaar implements the calls; libratbag just defines the page ID.

| Item | Source | Decision |
| --- | --- | --- |
| Feature ID | `HIDPP_PAGE_DEVICE_NAME 0x0005` hidpp20.h:123; `DEVICE_NAME = 0x0005` hidpp20_constants.py:39 | 0x0005 |
| getDeviceNameCount | function 0: `feature_request(DEVICE_NAME)` → `ord(reply[:1])` = name length, hidpp20.py:1857-1859 | reply [length] |
| getDeviceName | function 1: `feature_request(DEVICE_NAME, 0x10, len(name))`, param = char offset, reply = next chars, truncated to the remaining length, hidpp20.py:1862-1865; decoded as UTF-8 hidpp20.py:1870 | loop until `length` chars (bounded at 16 calls) |
| getDeviceType | function 2: `feature_request(DEVICE_NAME, 0x20)` → `ord(reply[:1])`, hidpp20.py:1843-1845 | reply [type] |
| Type codes | `DEVICE_KIND`: 0 keyboard, 1 remote control, 2 numpad, **3 mouse**, 4 touchpad, 5 trackball, 6 presenter, 7 receiver — hidpp20_constants.py:251-259 | "is a mouse" = type 3 |

Selection in `MouseController.Connect`: every enumerated device, indexes 0x01–0x06 then 0xFF; candidates are HID++ ≥ 2.0
with 0x8100. The first candidate whose type is 3 (mouse) wins; if none reports mouse (or 0x0005 is missing/fails), the
first candidate is used and `ConnectedMouse.TypeFallback` is set so the CLI can say so.

## Disagreements with the plan / between sources

1. **getCurrentProfile base.** Solaar treats the reply's first two bytes as the 1-based profile sector (same value it
   sends to setCurrentProfile), matching the plan. libratbag reads only `parameters[1]` (calls `parameters[0]`
   "unknown_0", hidpp20.c:2120-2121) and then treats the value as a **0-based** index unless the device has
   `HIDPP20_QUIRK_INDEX_OFFSET` ("Device returns 1-indexed profile", hidpp20.h:60, hidpp20.c:2281-2283), even though
   its own setCurrentProfile sends `index + 1`. We follow Solaar and the plan (BE16, 1-based). `SetProfile` confirms
   with getCurrentProfile, so a 0-based device would show up immediately as a confirmation mismatch. **Check at
   milestone 4**: `status` must print the profile the mouse is actually on.
2. **Error code 5** (LogitechInternal / LOGITECH_ERROR) exists in both sources but not in the plan; added.
3. **getCount excludes Root** — not stated in the plan; the feature table covers indexes 0..count inclusive.
4. Solaar also accepts replies whose device index is `devnumber ^ 0xFF` (Bluetooth devices answering 0x00 for 0xFF,
   base.py:729). Not implemented: the plan only covers the receiver and USB cable.

No function number in the plan disagrees with the sources.
