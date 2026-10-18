// DSH Batch Rename — Windows 11 现代右键菜单扩展（IExplorerCommand）
// 打包为 MSIX 稀疏包，命令通过 com:SurrogateServer 由 dllhost.exe 加载。
// 本 DLL 只负责“显示菜单项 + 收集选中路径 + 启动主程序”，重命名逻辑全在 BatchRename.exe。

#include <windows.h>
#include <shobjidl_core.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <string>
#include <vector>

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "uuid.lib")

// ---------------------------------------------------------------- CLSID 定义
// 6 个 verb 共用一份实现，靠 CLSID 分派行为。
// A1 文件→打开预览  A2 文件→快速改名  A3 文件夹→打开预览  A4 文件夹→快速改名
// A5 空白处→重命名本文件夹  A6 空白处→撤销
static const GUID CLSID_RN_FILES =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x01 } };
static const GUID CLSID_RN_QUICK =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x02 } };
static const GUID CLSID_RN_DIR =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x03 } };
static const GUID CLSID_RN_DIRQUICK =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x04 } };
static const GUID CLSID_RN_BG =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x05 } };
static const GUID CLSID_RN_UNDO =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x06 } };

enum Act { ACT_DIALOG_LIST, ACT_QUICK_LIST, ACT_DIALOG_FOLDER, ACT_UNDO };

struct VerbInfo
{
    const GUID *clsid;
    Act          action;
    const wchar_t *title;
    int          iconRes;   // 负数资源 ID，指向本 DLL 内的图标
};

static const VerbInfo kVerbs[] =
{
    { &CLSID_RN_FILES,    ACT_DIALOG_LIST,   L"批量重命名…",                 -101 },
    { &CLSID_RN_QUICK,    ACT_QUICK_LIST,    L"快速重命名（沿用上次规则）",   -102 },
    { &CLSID_RN_DIR,      ACT_DIALOG_LIST,   L"批量重命名…",                 -101 },
    { &CLSID_RN_DIRQUICK, ACT_QUICK_LIST,    L"快速重命名",                   -102 },
    { &CLSID_RN_BG,       ACT_DIALOG_FOLDER, L"批量重命名（本文件夹）…",       -101 },
    { &CLSID_RN_UNDO,     ACT_UNDO,          L"撤销上次批量重命名",           -103 },
};
static const int kVerbCount = (int)(sizeof(kVerbs) / sizeof(kVerbs[0]));

static const VerbInfo *FindVerb(REFCLSID clsid)
{
    for (int i = 0; i < kVerbCount; ++i)
        if (IsEqualCLSID(*(kVerbs[i].clsid), clsid)) return &kVerbs[i];
    return nullptr;
}

// ---------------------------------------------------------------- 全局
static HINSTANCE   g_hInst = nullptr;
static LONG        g_objCount = 0;
static const CLSID CLSID_ExplorerCommandIID =
{ 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x00 } };

// 本 DLL 所在目录的上一级 = 主程序安装目录
static std::wstring InstallDir()
{
    wchar_t buf[MAX_PATH * 2] = {};
    GetModuleFileNameW(g_hInst, buf, MAX_PATH * 2);
    PathRemoveFileSpecW(buf);            // ...\Shell
    PathRemoveFileSpecW(buf);            // 安装根目录
    return std::wstring(buf);
}

static std::wstring ExePath()
{
    std::wstring d = InstallDir();
    if (!d.empty() && d.back() != L'\\') d += L'\\';
    return d + L"BatchRename.exe";
}

static std::wstring Quote(const std::wstring &s)
{
    std::wstring r = L"\"";
    for (size_t i = 0; i < s.size(); ++i)
    {
        if (s[i] == L'"') r += L'\\';
        r += s[i];
    }
    r += L'\"';
    return r;
}

// 把选中项写成 UTF-8 清单文件，返回路径（失败返回空）
static std::wstring WriteListFile(const std::vector<std::wstring> &paths)
{
    PWSTR localAppData = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData))) return std::wstring();
    std::wstring dir = localAppData;
    CoTaskMemFree(localAppData);
    dir += L"\\DSHBatchRename\\pending";
    SHCreateDirectoryExW(nullptr, dir.c_str(), nullptr);

    SYSTEMTIME st; GetLocalTime(&st);
    wchar_t name[64];
    swprintf_s(name, 64, L"\\%04d%02d%02d-%02d%02d%02d-%03d.list",
               st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
    std::wstring file = dir + name;

    std::string utf8;
    for (size_t i = 0; i < paths.size(); ++i)
    {
        int need = WideCharToMultiByte(CP_UTF8, 0, paths[i].c_str(), (int)paths[i].size(), nullptr, 0, nullptr, nullptr);
        if (need > 0)
        {
            std::string tmp(need, '\0');
            WideCharToMultiByte(CP_UTF8, 0, paths[i].c_str(), (int)paths[i].size(), &tmp[0], need, nullptr, nullptr);
            utf8 += tmp;
        }
        utf8 += "\r\n";
    }

    HANDLE h = CreateFileW(file.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return std::wstring();
    DWORD written = 0;
    WriteFile(h, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
    CloseHandle(h);
    return file;
}

static bool SelectedPaths(IShellItemArray *items, std::vector<std::wstring> &out)
{
    if (!items) return false;
    DWORD count = 0;
    if (FAILED(items->GetCount(&count))) return false;
    for (DWORD i = 0; i < count; ++i)
    {
        IShellItem *item = nullptr;
        if (FAILED(items->GetItemAt(i, &item)) || !item) continue;
        PWSTR p = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &p)) && p)
        {
            out.push_back(p);
            CoTaskMemFree(p);
        }
        else
        {
            // 虚拟项（如"主文件夹"/This PC 下的用户目录）SIGDN_FILESYSPATH 取不到，
            // 先试 PIDL -> SHGetPathFromIDListW；不行再走 IKnownFolderManager 解析已知文件夹路径。
            PIDLIST_ABSOLUTE pidl = nullptr;
            if (SUCCEEDED(SHGetIDListFromObject(item, &pidl)) && pidl)
            {
                wchar_t path[MAX_PATH * 2] = {};
                if (SHGetPathFromIDListW(pidl, path) && path[0])
                {
                    out.push_back(path);
                }
                else
                {
                    IKnownFolderManager *kfm = nullptr;
                    if (SUCCEEDED(CoCreateInstance(CLSID_KnownFolderManager, nullptr,
                                                    CLSCTX_INPROC_SERVER,
                                                    IID_PPV_ARGS(&kfm))))
                    {
                        IKnownFolder *kf = nullptr;
                        if (SUCCEEDED(kfm->FindFolderFromIDList(pidl, &kf)) && kf)
                        {
                            PWSTR kfpath = nullptr;
                            if (SUCCEEDED(kf->GetPath(0, &kfpath)) && kfpath)
                            {
                                out.push_back(kfpath);
                                CoTaskMemFree(kfpath);
                            }
                            kf->Release();
                        }
                        kfm->Release();
                    }
                }
                CoTaskMemFree(pidl);
            }
        }
        item->Release();
    }
    return !out.empty();
}

static bool AnySelectedIsFolder(IShellItemArray *items)
{
    if (!items) return false;
    DWORD count = 0;
    if (FAILED(items->GetCount(&count)) || count == 0) return false;
    IShellItem *item = nullptr;
    if (FAILED(items->GetItemAt(0, &item)) || !item) return false;
    SFGAOF attr = 0;
    bool isDir = SUCCEEDED(item->GetAttributes(SFGAO_FOLDER, &attr)) && (attr & SFGAO_FOLDER) != 0;
    item->Release();
    return isDir;
}

// ---------------------------------------------------------------- IExplorerCommand
class ShellCommand final : public IExplorerCommand
{
public:
    explicit ShellCommand(const VerbInfo *v) : _ref(1), _verb(v) { InterlockedIncrement(&g_objCount); }
    ~ShellCommand() { InterlockedDecrement(&g_objCount); }

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
            *ppv = static_cast<IExplorerCommand *>(this);
        if (!*ppv) return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return (ULONG)InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        LONG r = InterlockedDecrement(&_ref);
        if (r == 0) delete this;
        return (ULONG)r;
    }

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray *, PWSTR *title) override
    {
        if (!title) return E_POINTER;
        return SHStrDupW(_verb->title, title);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray *, PWSTR *icon) override
    {
        if (!icon) return E_POINTER;
        wchar_t self[MAX_PATH * 2] = {};
        GetModuleFileNameW(g_hInst, self, MAX_PATH * 2);
        wchar_t spec[MAX_PATH * 2 + 16] = {};
        swprintf_s(spec, L"%s,%d", self, _verb->iconRes);
        return SHStrDupW(spec, icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray *, PWSTR *tip) override
    {
        if (tip) *tip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID *guid) override
    {
        if (!guid) return E_POINTER;
        *guid = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray *items, BOOL, EXPCMDSTATE *state) override
    {
        if (!state) return E_POINTER;
        // E5A05(ACT_DIALOG_FOLDER) 注册在 Directory\Background 下，空白处右键时
        // shell 调 GetState 传的 items 为 NULL（无选中项）。之前用 !AnySelectedIsFolder(items)
        // 判断会把它永久自藏（ECS_HIDDEN），导致新式菜单里只剩撤销。verb 是否出现完全由
        // manifest 的 ItemType 注册控制，这里一律返回 ENABLED。
        *state = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray *items, IBindCtx *) override
    {
        std::wstring exe = ExePath();
        std::wstring cmd;

        if (_verb->action == ACT_UNDO)
        {
            cmd = Quote(exe) + L" --undo";
        }
        else if (_verb->action == ACT_DIALOG_FOLDER)
        {
            std::vector<std::wstring> paths;
            if (!SelectedPaths(items, paths)) return E_FAIL;
            cmd = Quote(exe) + L" --folder " + Quote(paths[0]);
        }
        else
        {
            std::vector<std::wstring> paths;
            if (!SelectedPaths(items, paths)) return E_FAIL;
            std::wstring list = WriteListFile(paths);
            if (list.empty()) return E_FAIL;
            if (_verb->action == ACT_QUICK_LIST) cmd = Quote(exe) + L" --quick --list " + Quote(list);
            else                                  cmd = Quote(exe) + L" --list " + Quote(list);
        }

        STARTUPINFOW si = { sizeof(si) };
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_SHOWNORMAL;
        PROCESS_INFORMATION pi = {};
        std::vector<wchar_t> buf(cmd.begin(), cmd.end());
        buf.push_back(L'\0');
        if (!CreateProcessW(exe.c_str(), buf.data(), nullptr, nullptr, FALSE,
                            CREATE_UNICODE_ENVIRONMENT, nullptr, InstallDir().c_str(), &si, &pi))
            return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS *flags) override
    {
        if (!flags) return E_POINTER;
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand **cmds) override
    {
        if (cmds) *cmds = nullptr;
        return E_NOTIMPL;
    }

private:
    LONG _ref;
    const VerbInfo *_verb;
};

// ---------------------------------------------------------------- 类厂
class ClassFactory final : public IClassFactory
{
public:
    explicit ClassFactory(const VerbInfo *v) : _ref(1), _verb(v) {}
    IFACEMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
            *ppv = static_cast<IClassFactory *>(this);
        if (!*ppv) return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return (ULONG)InterlockedIncrement(&_ref); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        LONG r = InterlockedDecrement(&_ref);
        if (r == 0) delete this;
        return (ULONG)r;
    }
    IFACEMETHODIMP CreateInstance(IUnknown *outer, REFIID riid, void **ppv) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        ShellCommand *p = new (std::nothrow) ShellCommand(_verb);
        if (!p) return E_OUTOFMEMORY;
        HRESULT hr = p->QueryInterface(riid, ppv);
        p->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock) InterlockedIncrement(&g_objCount); else InterlockedDecrement(&g_objCount);
        return S_OK;
    }
private:
    LONG _ref;
    const VerbInfo *_verb;
};

// ---------------------------------------------------------------- 导出（符号由链接器 /EXPORT 导出，见 Build-Shell.ps1）
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID rclsid, REFIID riid, void **ppv)
{
    if (!ppv) return E_POINTER;
    const VerbInfo *v = FindVerb(rclsid);
    if (!v) return CLASS_E_CLASSNOTAVAILABLE;
    ClassFactory *f = new (std::nothrow) ClassFactory(v);
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return (g_objCount == 0) ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hInst = inst;
        DisableThreadLibraryCalls(inst);
    }
    return TRUE;
}
