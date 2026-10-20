// HotkeyTriage.Hook - 全局 WH_GETMESSAGE 钩子 DLL
// 捕获 WM_HOTKEY，记录"哪个进程收到了哪个热键"到共享内存。
// 主程序用 SendInput 模拟按键 → 系统投递 WM_HOTKEY 给注册者 → 此 DLL 捕获归属。
//
// 编译: g++ -shared -o hook.dll hook.cpp -static -O2 -s -lkernel32 -luser32
// MinGW g++ 16.1.0 验证。

#include <windows.h>

#define MAX_RECORDS 256
static const char* MAP_NAME = "HotkeyTriage_HookMap_v1";

#pragma pack(push, 1)
struct HotkeyRecord {
    DWORD pid;
    UINT  mods;
    UINT  vk;
};

struct SharedData {
    volatile LONG count;       // 已捕获总数（消费后清零）
    volatile LONG writeIdx;   // 环形写索引
    HotkeyRecord records[MAX_RECORDS];
};
#pragma pack(pop)

static HMODULE g_hSelf = NULL;
static HANDLE  g_mapFile = NULL;
static SharedData* g_shared = NULL;
static HHOOK  g_hook = NULL;

static void InitShared() {
    // 每个进程都尝试创建/打开同一映射；首个创建者清零
    g_mapFile = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
                                   0, (DWORD)sizeof(SharedData), MAP_NAME);
    if (!g_mapFile) return;
    g_shared = (SharedData*)MapViewOfFile(g_mapFile, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    if (g_shared && GetLastError() != ERROR_ALREADY_EXISTS) {
        ZeroMemory(g_shared, sizeof(SharedData));
    }
}

// 钩子回调：在每个目标进程的消息队列里运行
extern "C" __declspec(dllexport)
LRESULT CALLBACK GetMsgProc(int code, WPARAM wParam, LPARAM lParam) {
    if (code == HC_ACTION && g_shared) {
        const MSG* msg = (const MSG*)lParam;
        if (msg && msg->message == WM_HOTKEY) {
            LONG idx = InterlockedIncrement(&g_shared->writeIdx) & (MAX_RECORDS - 1);
            g_shared->records[idx].pid  = GetCurrentProcessId();
            g_shared->records[idx].mods = LOWORD(msg->lParam);
            g_shared->records[idx].vk   = HIWORD(msg->lParam);
            InterlockedIncrement(&g_shared->count);
        }
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

// 主程序调用：安装全局钩子。DLL 必须已 LoadLibrary 到主进程，g_hSelf 由 DllMain 填充。
extern "C" __declspec(dllexport) BOOL WINAPI SetHook(void) {
    if (!g_shared) InitShared();
    if (!g_shared) return FALSE;
    if (g_hook) return TRUE;  // 已安装
    g_hook = SetWindowsHookExW(WH_GETMESSAGE, GetMsgProc, g_hSelf, 0);
    return g_hook != NULL;
}

extern "C" __declspec(dllexport) BOOL WINAPI RemoveHook(void) {
    BOOL ok = g_hook ? UnhookWindowsHookEx(g_hook) : TRUE;
    g_hook = NULL;
    return ok;
}

// 主程序调用：取出并清空已捕获的记录。返回条数。
extern "C" __declspec(dllexport) int WINAPI GetRecords(HotkeyRecord* out, int maxCount) {
    if (!g_shared) return 0;
    LONG total = InterlockedExchange(&g_shared->count, 0);
    if (total <= 0) return 0;
    int n = (total < maxCount) ? total : maxCount;
    LONG w = InterlockedExchange(&g_shared->writeIdx, 0);
    LONG start = (w - n + MAX_RECORDS) % MAX_RECORDS;
    for (int i = 0; i < n; i++) {
        out[i] = g_shared->records[(start + i) % MAX_RECORDS];
    }
    return n;
}

BOOL WINAPI DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            g_hSelf = hModule;
            InitShared();
            DisableThreadLibraryCalls(hModule);
            break;
        case DLL_PROCESS_DETACH:
            if (g_shared)  { UnmapViewOfFile(g_shared);  g_shared = NULL; }
            if (g_mapFile) { CloseHandle(g_mapFile);     g_mapFile = NULL; }
            break;
    }
    return TRUE;
}
