// ComProbe — 验证 DSHBRShell.dll 的 IExplorerCommand 实现
// 用法: ComProbe.exe <1-6> [文件/文件夹...]
//   1..6 对应六个 verb 的 CLSID；给了路径就用这些路径组 IShellItemArray 调 Invoke。
#include <windows.h>
#include <shobjidl_core.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <cstdio>
#include <string>
#include <vector>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")

static const GUID kClsids[6] =
{
    { 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x01 } },
    { 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x02 } },
    { 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x03 } },
    { 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x04 } },
    { 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x05 } },
    { 0x6e2a4c10, 0x5b7d, 0x4c1e, { 0x9a, 0x3f, 0x2d, 0x8b, 0x1c, 0x7e, 0x5a, 0x06 } },
};

static IShellItemArray *MakeItems(const std::vector<std::wstring> &paths)
{
    std::vector<PIDLIST_ABSOLUTE> pidls;
    for (size_t i = 0; i < paths.size(); ++i)
    {
        PIDLIST_ABSOLUTE pidl = ILCreateFromPathW(paths[i].c_str());
        if (pidl) pidls.push_back(pidl);
    }
    if (pidls.empty()) return nullptr;
    IShellItemArray *arr = nullptr;
    HRESULT hr = SHCreateShellItemArrayFromIDLists((UINT)pidls.size(),
                   (PCIDLIST_ABSOLUTE_ARRAY)&pidls[0], &arr);
    for (size_t i = 0; i < pidls.size(); ++i) ILFree(pidls[i]);
    return SUCCEEDED(hr) ? arr : nullptr;
}

int wmain(int argc, wchar_t **argv)
{
    if (argc < 2) { wprintf(L"usage: ComProbe <1-6> [paths...]\n"); return 2; }
    int idx = _wtoi(argv[1]);
    if (idx < 1 || idx > 6) { wprintf(L"index must be 1..6\n"); return 2; }

    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    IExplorerCommand *cmd = nullptr;
    HRESULT hr = CoCreateInstance(kClsids[idx - 1], nullptr, CLSCTX_ALL, IID_IExplorerCommand, (void **)&cmd);
    wprintf(L"CoCreateInstance hr=0x%08X\n", (unsigned)hr);
    if (FAILED(hr) || !cmd) return 3;

    PWSTR title = nullptr;
    hr = cmd->GetTitle(nullptr, &title);
    wprintf(L"GetTitle  hr=0x%08X  [%s]\n", (unsigned)hr, title ? title : L"");
    if (title) CoTaskMemFree(title);

    PWSTR icon = nullptr;
    hr = cmd->GetIcon(nullptr, &icon);
    wprintf(L"GetIcon   hr=0x%08X  [%s]\n", (unsigned)hr, icon ? icon : L"");
    if (icon) CoTaskMemFree(icon);

    EXPCMDSTATE st = ECS_ENABLED;
    hr = cmd->GetState(nullptr, FALSE, &st);
    wprintf(L"GetState  hr=0x%08X  state=%d\n", (unsigned)hr, (int)st);

    EXPCMDFLAGS fl = ECF_DEFAULT;
    hr = cmd->GetFlags(&fl);
    wprintf(L"GetFlags  hr=0x%08X  flags=0x%X\n", (unsigned)hr, (unsigned)fl);

    if (argc > 2)
    {
        std::vector<std::wstring> paths;
        for (int i = 2; i < argc; ++i) paths.push_back(argv[i]);
        IShellItemArray *items = MakeItems(paths);
        wprintf(L"Items     %p (%d paths)\n", (void *)items, (int)paths.size());
        if (items)
        {
            hr = cmd->Invoke(items, nullptr);
            wprintf(L"Invoke    hr=0x%08X\n", (unsigned)hr);
            items->Release();
        }
    }

    cmd->Release();
    CoUninitialize();
    return 0;
}
