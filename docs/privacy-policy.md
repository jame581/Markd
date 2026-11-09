# Privacy policy

Last updated: 2026-11-09

This policy covers Markd (listed in the Microsoft Store as Markd Day Counter) on Windows and
Android. The part about cloud sync applies to Markd 1.1 and later.

Markd stores the occasions, milestones, categories, notes and settings you enter on your own
device. If you turn on sync, an encrypted copy also goes to your own Google Drive or OneDrive, and
nowhere else.

## What is collected

Nothing by the developer. Markd has no Markd account and no server of its own. It does not collect,
transmit or share personal information, usage analytics or crash reports with the developer, and it
contains no advertising and no third-party tracking.

## What leaves your device

Only what you choose: an export you make yourself, and the encrypted sync file if you turn sync on.

Export writes a backup file wherever you choose to save or share it, and you decide where that
goes. A backup contains your occasions, milestones, categories and settings, plus the name of the
device it was made on, so you can tell backups apart. A backup can be protected with a password, in
which case it is encrypted on your device before it is written.

On Android, if you have backup turned on in your device settings, Android can include Markd's
database in your device backup to your own Google account, and restore it when you set up a new
phone. Markd does not send it anywhere itself, and it has no access to that backup. Turn off
backup in your device settings to keep it out. The database also holds the email address and
account id described under Cloud sync, so they are covered by the same backup.

## Cloud sync (Markd 1.1 and later)

Sync is optional and off by default. Markd works fully without it.

**What it stores.** When you turn sync on and sign in with Google or Microsoft, Markd keeps one file
in a hidden folder that belongs only to Markd in your own Google Drive (the app data folder, scope
`drive.appdata`) or your own OneDrive (the `Apps/Markd` app folder, permission
`Files.ReadWrite.AppFolder`). Markd cannot see, list or change any of your other files in either
service.

**Encryption.** The file is encrypted on your device with your sync passphrase before it is
uploaded. Nobody, including the developer, can read it without that passphrase. If you lose the
passphrase, the cloud copy can no longer be read. Your data on your devices is never affected.

**Account details.** Markd reads the email address and account id of the account you sign in with
(for Google through the openid and email scopes, for Microsoft through the User.Read permission).
They are used only to show which account is connected and to recognise it again. They are kept in
Markd's database on your device and are not sent anywhere else.

**Sign-in data.** On Windows, Markd keeps the Google refresh token and the Microsoft sign-in cache
in files that are encrypted for your Windows user (DPAPI) in Markd's data folder. On Android, Google
sign-in is handled by Google Play services on your phone, and the Microsoft sign-in cache is kept
by the Microsoft sign-in library inside Markd's private app storage. None of this is sent to the
developer. There is no Markd server.

**Network.** Sync talks only to Google's or Microsoft's servers. On Android, Markd uses the
network state permission to pause sync while the device is offline.

**Disconnecting.** Disconnecting removes the sign-in from that device and offers to delete the cloud
file. To remove Markd's access for all your devices, use your account settings: in your Google
Account under Security and third-party access, or in your Microsoft account under Privacy and apps
and services.

**Google user data.** Markd's use and transfer of information received from Google APIs adheres to
the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including the Limited Use requirements.

- Accessed: the Markd file in your Drive app data folder, and your email address and account id.
- Used: only to sync your occasions between your own devices and to show which account is connected.
- Stored: the file stays in your Drive, encrypted with your passphrase. The email address and account
  id stay in Markd's database on your device.
- Shared: not shared with anyone and not sold. Not used for advertising, profiling or for training
  AI models. No human at the developer reads it.

## Notifications

Markd schedules milestone notifications locally on your device. No notification content is sent
to any server.

## Deleting your data

Erase all occasions in Settings removes every occasion and milestone from the device. Uninstalling
Markd from the Microsoft Store or from Android removes all of its data on that device, including
categories and settings. The portable Windows zip keeps its data in a `com.janmesarc.markd` folder
under `%LOCALAPPDATA%`; delete that folder to remove it. If you used sync, disconnect and choose to
delete the cloud file, or delete the Markd app folder data from Google Drive or OneDrive yourself.
Backup files you exported stay wherever you saved them until you delete them.

## Changes

If this policy changes, the new version is published at this address with a new date.

## Contact

Questions about this policy: https://github.com/jame581/Markd/issues
