# Folder Icon Changer

A small, native Windows 11 desktop app that changes a folder icon from a picture. The Explorer command opens a picture picker for the folder you right-clicked; choose a picture and the app converts and applies it. The same app also works on its own when launched from the Start menu.

## Architecture

The application uses C# and Windows Forms on .NET 10. This keeps the interface native to Windows and avoids a browser runtime. Magick.NET-Q8-x64 supplies common image decoders (including WebP, TIFF, and animated GIF); its Apache-2.0 license permits the project's MIT license. The app builds a seven-size ICO with PNG-compressed images, transparency, center cropping, and Lanczos resampling.

The supplied `media\yellow_folder_mountain_logo_cutout.ico` is embedded as the executable icon and used by the app window and MSI shortcut. The supplied transparent PNG is the source for the package's generated 44×44, 150×150, and 50×50 logos in `package\Assets\`; the original artwork is left unchanged.

The MSI retains a standard Windows shell verb for the classic menu and, for signed releases, includes a sparse MSIX package that registers a native `IExplorerCommand` using [Microsoft's packaged File Explorer integration model](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer). On the tested Windows 11 Pro build 26300, Explorer places this command in the modern context menu's **App extensions** flyout, rather than as an ungrouped top-level row. The MSI's custom actions register and remove that sparse package for the installing user; they do not install or trust a certificate. Windows requires the package signer to be trusted before package registration. The supported package integration does not provide a placement setting to force the command out of Explorer's grouping.

## Install

1. Use a release built with `.\build.ps1 -Task SignedRelease`. For this self-signed publisher, first install `dist\FolderIconChangerPublisher.cer` into **Local Machine → Trusted People**. Current User trust did not satisfy AppX deployment on the tested PC. This requires administrator approval; the MSI itself does not change certificate stores.
2. Double-click `dist\FolderIconChangerSetup.msi` and approve the Windows Installer permission prompt. Installation writes the app under **Program Files**, adds one branded Start Menu shortcut, and registers the classic verb and sparse package for the installing user.
3. In File Explorer, right-click a folder, open **App extensions**, and select **Change Folder Icon** (the tested Windows 11 build groups it there). The classic verb is also registered under **Show more options**.
4. Choose a picture. The app applies it automatically and shows **Folder icon changed successfully!**

The standalone `dist\FolderIconChanger.exe` can also be run without installing. It includes the .NET runtime; keep `THIRD-PARTY-NOTICES.md` and the accompanying Apache/ImageMagick license files beside it.

## Use the app by itself

Open **Folder Icon Changer** from the Start menu, or run the standalone executable. Browse to a folder, choose an image, check its preview, and select **Apply Folder Icon**. Use **Restore default icon** to remove the icon settings added by the app. If the folder had a custom icon before the change, that previous setting is restored.

The window uses per-monitor DPI scaling for high-resolution displays. Field rows and the status line grow to fit scaled text, action buttons wrap when needed, and the preview uses the remaining space.

Supported input includes PNG, JPEG, BMP, GIF (first frame), WebP, TIFF, ICO, and other raster formats supported by ImageMagick. The app uses the image contents rather than relying on the filename extension.

## Safety and file handling

- The app writes a uniquely named, hidden `.foldericon-<id>.ico` file in the selected folder; it never replaces an existing icon file.
- It edits only `IconResource`, `IconFile`, and `IconIndex` in `desktop.ini`, preserving other settings, text encoding, and line endings. On the first change it records just those original icon settings so they can be restored.
- The prior implementation could return success after attempted writes and notifications without verifying the result, and it did not set the folder's `ReadOnly` customization bit. The current workflow sets that bit and verifies the icon, `desktop.ini`, and attributes before success is shown.
- A new `desktop.ini` is removed on restore only if it still contains no other settings. If someone changes the app-managed icon settings while the app is installed, restore leaves those changed settings alone.
- For safety, generated `.ico` files are left in the folder after restore and uninstall. The utility does not delete user folder contents.
- The icon is written to a unique temporary file, parsed and verified, then moved into place before `desktop.ini` is updated. The app preserves unrelated settings, sets the folder's `System` and `ReadOnly` attributes and the INI's `Hidden` and `System` attributes, then reads the icon and INI back to verify their contents before reporting success.
- A per-folder Windows mutex serializes changes from multiple app instances. After verification, the app sends `SHChangeNotify` updates and refreshes any open Explorer view showing the folder or its parent. The **Rebuild** button deletes only the current user's top-level `thumbcache_*.db` files, then restarts the Windows shell so thumbnails are rebuilt. It does not delete icon caches; open Explorer windows may close.
- Restore verifies the restored INI and attributes before reporting success. It only restores the folder's original attributes when the app-managed icon settings are still unchanged.
- The installer adds only its own uniquely named Windows Installer/Explorer entries. Uninstall removes those entries, its Start Menu entry, and the installed application; it does not inspect or change customized folders.
- Applying an icon to a folder you cannot write to will fail with a plain-language message. The application does not elevate itself.

## Build from source

Building requires the **.NET 10 SDK** and an internet connection to restore the pinned NuGet packages. No machine-wide packages or Windows settings are changed by the build. Packages are restored to the repository's ignored `.packages` directory; WiX is a local .NET tool.

From PowerShell in the repository, the normal build and test workflow is:

```powershell
.\build.ps1
```

This builds the app, runs the focused tests, publishes a self-contained x64 executable, builds the native shell server, and creates an unsigned MSI without the sparse package. Use the signed release workflow to produce the distributable package:

```powershell
.\build.ps1 -Task SignedRelease
```

`SignedRelease` selects the configured certificate from `Cert:\CurrentUser\My`, checks its validity, accessible RSA private key, and Code Signing EKU, then builds and signs the EXE, native shell DLL, sparse MSIX, and MSI using SHA-256. The manifest Publisher is generated from and checked against the certificate Subject. The supplied password-protected PFX was inspected locally and matches this certificate; release signing uses the accessible certificate-store key and never writes the private key into the build output. The build exports the matching public-only `.cer` for explicit trust setup. It does not install certificates or alter trust settings. No timestamp is applied.

The outputs are:

- `dist\FolderIconChangerSetup.msi` — Windows Installer package
- `dist\FolderIconChanger.exe` — standalone application
- `dist\FolderIconChanger.Shell.dll` — native `IExplorerCommand` server
- `dist\FolderIconChanger.msix` — sparse package used for modern menu registration
- `dist\FolderIconChangerPublisher.cer` — public-only signer certificate; never contains the private key

To run a smaller step:

```powershell
.\build.ps1 -Task Test
.\build.ps1 -Task Publish
.\build.ps1 -Task Installer
```

Build outputs and locally restored packages are not source-controlled. The `.NET 10 SDK` is required only to build; users installing the MSI or running the standalone app do not need to install .NET separately.

## Uninstall

Uninstall **Folder Icon Changer** from **Settings → Apps → Installed apps** (or **Control Panel → Programs and Features**). Windows Installer removes the app, its Start Menu entry, and its Explorer registration. Folder icons already applied remain in place and continue to work; uninstall never changes a user's `desktop.ini` or deletes their `.ico` files.

## Windows and test limitations

The MSI is built for Windows x64, which covers standard Intel/AMD Windows 11 PCs. Its classic registry verb is per-machine, so installing or uninstalling prompts for administrator approval. Sparse-package registration is per-user. Changing a folder icon uses only the current user's permissions.

Automated tests cover ICO dimensions and transparency, malformed ICO rejection, common input formats, non-square images, an animated GIF's first frame, Arabic filenames/settings, paths on separate drives, existing custom icons, preserved INI attributes, repeated and concurrent changes, restoration, and workflow success/failure ordering. Shell notifications are simulated in workflow tests; they do not exercise Explorer. On Windows 11 Pro build 26300, the signed MSI installed and registered its sparse package; the app, Start Menu entry, package identity, and classic registry verb were verified. A live modern-menu test found **Change Folder Icon** inside the **App extensions** flyout, not as a direct top-level row. Invoking it on a disposable folder passed the selected folder to the app; choosing the supplied PNG automatically wrote the ICO and verified `desktop.ini` settings/attributes before the success message appeared. Live folder-icon application and refresh were also exercised against isolated temporary folders. MSI uninstall behavior remains unverified.

## License

The project's original source code is available under the [MIT License](LICENSE). Third-party notices and bundled image-codec licenses are documented in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
