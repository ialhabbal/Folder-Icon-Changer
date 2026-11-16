#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <atomic>
#include <new>
#include <string>

namespace
{
    constexpr CLSID ClassId =
    { 0x7d8968e1, 0x5467, 0x4d54, { 0xa2, 0x64, 0xc3, 0x88, 0xdc, 0x50, 0x72, 0x11 } };

    HMODULE ModuleHandle = nullptr;
    std::atomic_long ObjectCount = 0;
    std::atomic_long ServerLockCount = 0;

    std::wstring QuoteArgument(const std::wstring& value)
    {
        std::wstring result = L"\"";
        size_t backslashes = 0;
        for (const wchar_t character : value)
        {
            if (character == L'\\')
            {
                ++backslashes;
                continue;
            }

            if (character == L'"')
            {
                result.append(backslashes * 2 + 1, L'\\');
                result.push_back(L'"');
                backslashes = 0;
                continue;
            }

            result.append(backslashes, L'\\');
            result.push_back(character);
            backslashes = 0;
        }

        result.append(backslashes * 2, L'\\');
        result.push_back(L'"');
        return result;
    }

    HRESULT GetSelectedFolder(IShellItemArray* items, PWSTR* folderPath)
    {
        if (items == nullptr || folderPath == nullptr)
        {
            return E_INVALIDARG;
        }

        *folderPath = nullptr;
        DWORD count = 0;
        HRESULT result = items->GetCount(&count);
        if (FAILED(result) || count != 1)
        {
            return FAILED(result) ? result : E_INVALIDARG;
        }

        IShellItem* item = nullptr;
        result = items->GetItemAt(0, &item);
        if (FAILED(result))
        {
            return result;
        }

        SFGAOF attributes = 0;
        result = item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attributes);
        if (SUCCEEDED(result)
            && (attributes & (SFGAO_FILESYSTEM | SFGAO_FOLDER))
                == (SFGAO_FILESYSTEM | SFGAO_FOLDER))
        {
            result = item->GetDisplayName(SIGDN_FILESYSPATH, folderPath);
        }
        else if (SUCCEEDED(result))
        {
            result = E_INVALIDARG;
        }

        item->Release();
        return result;
    }

    class FolderIconCommand final : public IExplorerCommand
    {
    public:
        FolderIconCommand()
        {
            ++ObjectCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }

            *object = nullptr;
            if (IsEqualIID(interfaceId, IID_IUnknown)
                || IsEqualIID(interfaceId, IID_IExplorerCommand))
            {
                *object = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }

            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return static_cast<ULONG>(++referenceCount_);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG remaining = static_cast<ULONG>(--referenceCount_);
            if (remaining == 0)
            {
                delete this;
            }

            return remaining;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override
        {
            return SHStrDupW(L"Change Folder Icon", title);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon == nullptr)
            {
                return E_POINTER;
            }

            wchar_t modulePath[MAX_PATH] = {};
            const DWORD length = GetModuleFileNameW(ModuleHandle, modulePath, ARRAYSIZE(modulePath));
            if (length == 0 || length >= ARRAYSIZE(modulePath))
            {
                *icon = nullptr;
                return HRESULT_FROM_WIN32(GetLastError());
            }

            PathRemoveFileSpecW(modulePath);
            if (!PathAppendW(modulePath, L"FolderIconChanger.exe"))
            {
                *icon = nullptr;
                return E_FAIL;
            }

            return SHStrDupW(modulePath, icon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tooltip) override
        {
            if (tooltip != nullptr)
            {
                *tooltip = nullptr;
            }

            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* commandName) override
        {
            if (commandName == nullptr)
            {
                return E_POINTER;
            }

            *commandName = GUID_NULL;
            return S_OK;
        }

        IFACEMETHODIMP GetState(
            IShellItemArray* items,
            BOOL,
            EXPCMDSTATE* state) override
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }

            *state = ECS_HIDDEN;
            PWSTR folderPath = nullptr;
            const HRESULT result = GetSelectedFolder(items, &folderPath);
            CoTaskMemFree(folderPath);
            if (SUCCEEDED(result))
            {
                *state = ECS_ENABLED;
            }

            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            PWSTR folderPath = nullptr;
            HRESULT result = GetSelectedFolder(items, &folderPath);
            if (FAILED(result))
            {
                return result;
            }

            wchar_t modulePath[MAX_PATH] = {};
            const DWORD length = GetModuleFileNameW(ModuleHandle, modulePath, ARRAYSIZE(modulePath));
            if (length == 0 || length >= ARRAYSIZE(modulePath))
            {
                CoTaskMemFree(folderPath);
                return HRESULT_FROM_WIN32(GetLastError());
            }

            PathRemoveFileSpecW(modulePath);
            if (!PathAppendW(modulePath, L"FolderIconChanger.exe"))
            {
                CoTaskMemFree(folderPath);
                return E_FAIL;
            }

            const std::wstring parameters = QuoteArgument(folderPath);
            const HINSTANCE launched = ShellExecuteW(
                nullptr,
                L"open",
                modulePath,
                parameters.c_str(),
                nullptr,
                SW_SHOWNORMAL);
            CoTaskMemFree(folderPath);
            return reinterpret_cast<INT_PTR>(launched) > 32
                ? S_OK
                : HRESULT_FROM_WIN32(static_cast<DWORD>(reinterpret_cast<INT_PTR>(launched)));
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr)
            {
                return E_POINTER;
            }

            *flags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands != nullptr)
            {
                *commands = nullptr;
            }

            return E_NOTIMPL;
        }

    private:
        ~FolderIconCommand()
        {
            --ObjectCount;
        }

        std::atomic_ulong referenceCount_ = 1;
    };

    class FolderIconClassFactory final : public IClassFactory
    {
    public:
        FolderIconClassFactory()
        {
            ++ObjectCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }

            *object = nullptr;
            if (IsEqualIID(interfaceId, IID_IUnknown)
                || IsEqualIID(interfaceId, IID_IClassFactory))
            {
                *object = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }

            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return static_cast<ULONG>(++referenceCount_);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG remaining = static_cast<ULONG>(--referenceCount_);
            if (remaining == 0)
            {
                delete this;
            }

            return remaining;
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID interfaceId, void** object) override
        {
            if (outer != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }

            auto* command = new (std::nothrow) FolderIconCommand();
            if (command == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const HRESULT result = command->QueryInterface(interfaceId, object);
            command->Release();
            return result;
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock)
            {
                ++ServerLockCount;
            }
            else
            {
                long currentCount = ServerLockCount.load();
                while (currentCount > 0
                    && !ServerLockCount.compare_exchange_weak(currentCount, currentCount - 1))
                {
                }
            }

            return S_OK;
        }

    private:
        ~FolderIconClassFactory()
        {
            --ObjectCount;
        }

        std::atomic_ulong referenceCount_ = 1;
    };
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        ModuleHandle = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}

STDAPI DllGetClassObject(
    REFCLSID classId,
    REFIID interfaceId,
    void** object)
{
    if (!IsEqualCLSID(classId, ClassId))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) FolderIconClassFactory();
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    const HRESULT result = factory->QueryInterface(interfaceId, object);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow()
{
    return ObjectCount.load() == 0 && ServerLockCount.load() == 0 ? S_OK : S_FALSE;
}
