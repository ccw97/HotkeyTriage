// HotkeyTriage.Hook - 全局 WH_GETMESSAGE 钩子 DLL
// 捕获 WM_HOTKEY，记录"哪个进程收到了哪个热键"到共享内存。
//
// 编译: g++ -shared -o hook.dll hook.cpp -static -O2 -s -lkernel32 -luser32
//
// 设计要点（v2 修正）：
// 1. count 单调递增、永不清零 —— 避免多进程 DllMain 竞态清零，也避免读写索引错位
// 2. 只有「首个创建共享段的进程」初始化内存，其余进程只映射
// 3. 环形索引 = (seq - 1) & (MAX-1)，读写用同一个公式，不可能错位
// 4. 暴露 hookHits / hotkeyHits 诊断计数，用于判断钩子是否真的被调用

#include <windows.h>

#define MAX_RECORDS 256
static const char* MAP_NAME = "HotkeyTriage_HookMap_v2";
static const LONG MAGIC = 0x524B5448; // 'HTKR'

#pragma pack(push, 1)
struct HotkeyRecord {
    DWORD pid;
    UINT  mods;
    UINT  vk;
};

struct SharedData {
    LONG magic;       // 0: 映射已初始化
    LONG count;       // 累计写入总数（单调递增，不清零）
    LONG hookHits;    // 钩子回调被调用次数（所有消息）
    LONG hotkeyHits;  // 其中 WM_HOTKEY 次数
    LONG pad;
    HotkeyRecord records[MAX_RECORDS];
};
#pragma pack(pop)

static HMODULE  g_hSelf = NULL;
static HANDLE   g_mapFile = NULL;
static SharedData* g_shared = NULL;
static HHOOK  g_hook = NULL;

static void InitShared() {
    if (g_shared) return;

    // 先新建，再打开：GetLastError 必须在 CreateFileMapping 之后立刻取，
    // 否则会被后续调用（MapViewOfFile）覆盖。
    g_mapFile = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
                                   0, (DWORD)sizeof(SharedData), MAP_NAME);
    BOOL created = (g_mapFile != NULL) && (GetLastError() != ERROR_ALREADY_EXISTS);
    if (!g_mapFile) return;

    g_shared = (SharedData*)MapViewOfFile(g_mapFile, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    if (!g_shared) return;

    if (created || g_shared->magic != MAGIC) {
        ZeroMemory(g_shared, sizeof(SharedData));
        g_shared->magic = MAGIC;
    }
}

// 钩子回调：在每个被挂钩进程的消息循环里运行
extern "C" __declspec(dllexport)
LRESULT CALLBACK GetMsgProc(int code, WPARAM wParam, LPARAM lParam) {
    if (code == HC_ACTION && g_shared) {
        InterlockedIncrement(&g_shared->hookHits);
        const MSG* msg = (const MSG*)lParam;
        if (msg && msg->message == WM_HOTKEY) {
            InterlockedIncrement(&g_shared->hotkeyHits);
            LONG seq = InterlockedIncrement(&g_shared->count);
            LONG idx = (seq - 1) & (MAX_RECORDS - 1);
            g_shared->records[idx].pid  = GetCurrentProcessId();
            g_shared->records[idx].mods = LOWORD((DWORD)msg->lParam);
            g_shared->records[idx].vk   = HIWORD((DWORD)msg->lParam);
        }
    }
    return CallNextHookEx(g_hook, code, wParam, lParam);
}

extern "C" __declspec(dllexport) BOOL WINAPI SetHook(void) {
    if (!g_shared) InitShared();
    if (!g_shared) return FALSE;
    if (g_hook) return TRUE;
    g_hook = SetWindowsHookExW(WH_GETMESSAGE, GetMsgProc, g_hSelf, 0);
    return g_hook != NULL;
}

extern "C" __declspec(dllexport) BOOL WINAPI RemoveHook(void) {
    BOOL ok = g_hook ? UnhookWindowsHookEx(g_hook) : TRUE;
    g_hook = NULL;
    return ok;
}

// 读取 [fromSeq, count) 区间的记录，返回实际条数（最多 maxCount）。
// fromSeq 由调用方维护，读完自行更新为 GetCount()。
extern "C" __declspec(dllexport) int WINAPI ReadRecords(LONG fromSeq, HotkeyRecord* out, int maxCount) {
    if (!g_shared) return 0;
    LONG total = g_shared->count;
    LONG avail = total - fromSeq;
    if (avail <= 0) return 0;
    if (avail > maxCount) avail = maxCount;
    for (LONG i = 0; i < avail; i++) {
        LONG idx = (fromSeq + i) & (MAX_RECORDS - 1);
        out[i] = g_shared->records[idx];
    }
    return (int)avail;
}

extern "C" __declspec(dllexport) LONG WINAPI GetCount(void) {
    return g_shared ? g_shared->count : 0;
}

// 诊断：钩子是否真的被调用 / 是否真的看到 WM_HOTKEY / 共享段是否映射成功
extern "C" __declspec(dllexport) LONG WINAPI GetHookHits(void) {
    return g_shared ? g_shared->hookHits : -1;
}
extern "C" __declspec(dllexport) LONG WINAPI GetHotkeyHits(void) {
    return g_shared ? g_shared->hotkeyHits : -1;
}
extern "C" __declspec(dllexport) LONG WINAPI GetSharedState(void) {
    return g_shared ? 1 : 0;
}

BOOL WINAPI DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            g_hSelf = hModule;
            InitShared();
            DisableThreadLibraryCalls(hModule);
            break;
        case DLL_PROCESS_DETACH:
            if (g_hook)    { UnhookWindowsHookEx(g_hook); g_hook = NULL; }
            if (g_shared)  { UnmapViewOfFile(g_shared);   g_shared = NULL; }
            if (g_mapFile) { CloseHandle(g_mapFile);      g_mapFile = NULL; }
            break;
    }
    return TRUE;
}
