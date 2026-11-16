# Changelog

## Unreleased

- Add a **Rebuild** action that deletes only the current user's thumbnail cache database files and restarts the Windows shell.
- Use the supplied PNG and ICO artwork for the app, window, installer, and sparse-package logos; add a signed-release build for the EXE, native shell server, sparse MSIX, and MSI.
- Keep the branded MSI shortcut as the only Start Menu entry; hide the sparse package's helper app from the app list while retaining Explorer integration.
- Add the sparse-package `IExplorerCommand` integration for the Windows 11 modern menu; retain the classic registry verb as a fallback. Live placement on Windows 11 Pro build 26300 is in the validation note below.
- Verify signed-release signer identity and package Publisher; report the current self-signed certificate's untrusted chain without changing Windows trust settings.
- Keep native compiler byproducts in ignored build output and align the MSI and sparse-package versions at 1.1.5.
- Fix MSI sparse-package registration quoting: pass the install folder as a dot-qualified path so its trailing backslash cannot escape the closing command-line quote.
- Export the matching public-only signer certificate with signed release outputs for explicit package trust setup; never embed or copy the private key.
- Fix the missing shell-customization `ReadOnly` folder attribute and replace optimistic completion with verification of the completed ICO, `desktop.ini` contents, and required attributes before reporting success.
- Apply from the Explorer-launched image picker through the same shared workflow as the standalone UI; serialize changes across app instances, notify Explorer after disk verification, and refresh open views of the folder or its parent.
- Roll back incomplete apply and restore operations when verification or shell notification fails.
- Add regression tests for failure reporting, repeated and concurrent changes, attribute preservation, metadata migration, and workflow ordering.
- Keep the classic registry verb as a fallback; the supported modern-menu route uses an `IExplorerCommand` COM server and sparse-package identity, with exact grouping controlled by Explorer.
- Live validation on Windows 11 Pro build 26300: the packaged command appears in the modern menu's **App extensions** flyout, not as a direct top-level row. Invoking it there and choosing an image automatically applied the icon to a disposable folder and verified the customization before reporting success.

## 1.0.0

- Added the Windows Forms app, Explorer folder verb, image-to-ICO conversion, folder customization and restore flow.
- Added a self-contained x64 executable, WiX MSI source, focused tests, and setup/usage documentation.
