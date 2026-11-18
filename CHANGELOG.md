# Changelog

## Unreleased

- Add a **Rebuild** action that deletes only the current user's thumbnail cache database files and restarts the Windows shell.
- Use the supplied PNG and ICO artwork for the app, window, installer, and sparse-package logos; add a signed-release build for the EXE, native shell server, sparse MSIX, and MSI.
- Keep the branded MSI shortcut as the only Start Menu entry; hide the sparse package's helper app from the app list while retaining Explorer integration.
- Keep the sparse-package `IExplorerCommand` as the sole context-menu command and remove the legacy registry verb from existing installations.
- Verify signed-release signer identity and package Publisher; report the current self-signed certificate's untrusted chain without changing Windows trust settings.
- Hide the packaged Explorer command from **Show more options** by distinguishing the classic-menu site from the modern menu; align the MSI and sparse-package versions at 1.1.7. Live validation confirms one first-level entry and none in the classic menu on Windows 11 Pro build 26300.
- Fix MSI sparse-package registration quoting: pass the install folder as a dot-qualified path so its trailing backslash cannot escape the closing command-line quote.
- Export the matching public-only signer certificate with signed release outputs for explicit package trust setup; never embed or copy the private key.
- Fix the missing shell-customization `ReadOnly` folder attribute and replace optimistic completion with verification of the completed ICO, `desktop.ini` contents, and required attributes before reporting success.
- Apply from the Explorer-launched image picker through the same shared workflow as the standalone UI; serialize changes across app instances, notify Explorer after disk verification, and refresh open views of the folder or its parent.
- Roll back incomplete apply and restore operations when verification or shell notification fails.
- Add regression tests for failure reporting, repeated and concurrent changes, attribute preservation, metadata migration, and workflow ordering.
- Live validation on Windows 11 Pro build 26300: after installation removed the legacy registry verb, Explorer showed one **Change Folder Icon** entry directly in the first-level menu.

## 1.0.0

- Added the Windows Forms app, Explorer folder verb, image-to-ICO conversion, folder customization and restore flow.
- Added a self-contained x64 executable, WiX MSI source, focused tests, and setup/usage documentation.
