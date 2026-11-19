#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include "../Session_Manager/ABItems.h"
#include <filesystem>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <map>
#include <functional>
#include <chrono>
#include <stdexcept>
// ============================================================================
//  Economy - 상점·보관함 규칙 + 파일 저장소
//   - 규칙(Apply/Reserve/Settle/Recover)은 저장소를 모르는 순수 함수다.
//   - 저장은 로비의 EconomyService가 한다.
//       DB 계정 → DB(dbo.Stash, dbo.StashItem) / DB 없이 들어온 계정 → 아래 파일 저장소
//   - 세션 서버는 저장소를 만지지 않는다. (장비는 IPC로 받고, 정산은 IPC로 보고)
// ============================================================================
namespace Economy {
constexpr int CommandPacket=210, StatePacket=211, Capacity=96;
static_assert(sizeof(AB::Items)/sizeof(AB::Item)+4<=Capacity,"Every item and resource must fit the stash snapshot");
enum Result {Ok=0,Invalid=1,Funds=2,NotOwned=3,Busy=4,StorageError=5};
#pragma pack(push,1)
struct Command {int version=1,sequence=0,action=0,itemHash=0,count=0,slot=0;};
struct Entry {int hash=0,count=0,sell=0,buy=0;};
struct Snapshot {int version=2,sequence=0,result=0;std::int64_t credits=0;int weapon=0,item=0,inRun=0,count=0;Entry entries[Capacity]{};};
#pragma pack(pop)
static_assert(sizeof(Command)==24&&sizeof(Snapshot)==1572,"economy wire");
struct Account {
    std::int64_t credits=300;
    int weapon=0,item=0;
    std::map<int,int> stash;
    std::string run,lastRun;          // 진행 중인 판 / 마지막으로 정산한 판
    unsigned long ownerPid=0;         // 예전 파일 형식 호환용 (더는 쓰지 않음)
    int carriedWeapon=0,carriedItem=0,carriedCount=0;   // 출격 때 보관함에서 꺼내 간 장비
};
struct Grant {int weapon=0,item=0,count=0;bool freeWeapon=true,freeItem=true;};
// 기본 장비 (보관함에서 꺼내지 않음 → 정산 대상 아님)
inline Grant StarterGrant(){Grant g;g.weapon=ItemHash::Of("sword_001");g.item=ItemHash::Of("health_potion");g.count=3;return g;}
inline std::string Key(int dbId,const std::string& name){return dbId>0?"account:"+std::to_string(dbId):"development:"+name;}
// 파일 저장소 위치: RETRY_ECONOMY_DIR → 소스 폴더의 Data\Economy → 실행 파일 옆 Data\Economy
inline std::filesystem::path Root(){
    wchar_t overridePath[32768];DWORD n=GetEnvironmentVariableW(L"RETRY_ECONOMY_DIR",overridePath,32768);
    if(n>0&&n<32768)return std::filesystem::absolute(overridePath).lexically_normal();
    wchar_t exe[32768];GetModuleFileNameW(nullptr,exe,32768);auto exeDir=std::filesystem::path(exe).parent_path();auto path=exeDir;
    for(int i=0;i<7&&!path.empty();i++,path=path.parent_path())if(std::filesystem::is_directory(path/L"Retry_Server"))return path/L"Data"/L"Economy";
    return exeDir/L"Data"/L"Economy";   // 실행 파일만 따로 옮겨도 동작 (제출용)
}
inline std::string Hex(const std::string& key){const char* hex="0123456789abcdef";std::string result;for(unsigned char c:key){result+=hex[c>>4];result+=hex[c&15];}return result;}
inline unsigned long long Hash(const std::string& text){unsigned long long h=14695981039346656037ull;for(unsigned char c:text){h^=c;h*=1099511628211ull;}return h;}
struct Guard {
    HANDLE handle=nullptr;bool locked=false;
    explicit Guard(const std::filesystem::path& path){auto name=L"Local\\RetryEconomy_"+std::to_wstring(Hash(path.u8string()));handle=CreateMutexW(nullptr,FALSE,name.c_str());if(handle){DWORD status=WaitForSingleObject(handle,5000);locked=status==WAIT_OBJECT_0||status==WAIT_ABANDONED;}if(!locked){if(handle)CloseHandle(handle);handle=nullptr;throw std::runtime_error("Economy store busy");}}
    ~Guard(){if(handle){if(locked)ReleaseMutex(handle);CloseHandle(handle);}}
};
inline bool IsWeapon(int hash){auto item=AB::Find(hash);return (item&&item->kind==2)||hash==ItemHash::Of("gun_001");}
inline int BuyPrice(int hash){
    if(hash==ItemHash::Of("gun_001"))return 0;
    auto item=AB::Find(hash);if(!item)return 0;
    if(item->tier==1)return item->value*2;
    return 0;
}
inline std::string Encode(const Account& a){std::ostringstream s;s<<"RETRY_ECONOMY 1\n"<<a.credits<<' '<<a.weapon<<' '<<a.item<<' '<<a.ownerPid<<' '<<a.carriedWeapon<<' '<<a.carriedItem<<' '<<a.carriedCount<<'\n'<<std::quoted(a.run)<<' '<<std::quoted(a.lastRun)<<'\n'<<a.stash.size()<<'\n';for(auto [hash,count]:a.stash)s<<hash<<' '<<count<<'\n';return s.str();}
// 읽어 온 계정 검사 + 폐기된 장비 이전 (파일·DB 공용)
inline bool Normalize(Account& a){
    if(a.stash.size()>(size_t)Capacity||a.credits<0||a.credits>1000000000)return false;
    const int retired=ItemHash::Of("beam_greatsword"),replacement=ItemHash::Of("greatsword_001");
    for(auto [hash,n]:a.stash)if(n<=0||n>999999||(AB::Value(hash)<=0&&hash!=retired))return false;
    // Retired equipment remains recoverable in existing signed saves.
    auto old=a.stash.find(retired);
    if(old!=a.stash.end()){if(a.stash[replacement]>999999-old->second)return false;a.stash[replacement]+=old->second;a.stash.erase(old);}
    if(a.weapon==retired)a.weapon=replacement;
    if(a.carriedWeapon==retired)a.carriedWeapon=replacement;
    return true;
}
inline bool Load(const std::filesystem::path& path,Account& a){
    if(!std::filesystem::exists(path))return true;
    std::ifstream file(path,std::ios::binary);std::string checksum,body;if(!std::getline(file,checksum))return false;body.assign(std::istreambuf_iterator<char>(file),{});
    // A text-mode checkout may have converted the saved LF line endings to CRLF.
    if (!checksum.empty() && checksum.back() == '\r')
    {
        checksum.pop_back();
        for (std::size_t pos = body.find("\r\n"); pos != std::string::npos; pos = body.find("\r\n", pos + 1))
            body.erase(pos, 1);
    }
    if(checksum!=std::to_string(Hash(body)))return false;
    std::istringstream s(body);int version=0;size_t count=0;std::string magic;
    if(!(s>>magic>>version)||magic!="RETRY_ECONOMY"||version!=1||!(s>>a.credits>>a.weapon>>a.item>>a.ownerPid>>a.carriedWeapon>>a.carriedItem>>a.carriedCount>>std::quoted(a.run)>>std::quoted(a.lastRun)>>count)||count>Capacity)return false;
    for(size_t i=0;i<count;i++){int hash,n;if(!(s>>hash>>n)||a.stash.count(hash))return false;a.stash[hash]=n;}
    return Normalize(a);
}
inline bool Save(const std::filesystem::path& path,const Account& a){
    auto body=Encode(a);auto text=std::to_string(Hash(body))+"\n"+body;
    auto temp=path;temp+=L".pending";
    HANDLE file=CreateFileW(temp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;DWORD written=0;bool ok=WriteFile(file,text.data(),(DWORD)text.size(),&written,nullptr)&&written==text.size()&&FlushFileBuffers(file);CloseHandle(file);
    return ok&&MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
}
// 파일 저장소 1계정: 읽기 → operation → 저장
inline Result Transact(const std::string& key,const std::function<Result(Account&)>& operation,Account* result=nullptr,const std::filesystem::path& root={}){
    if(key.empty()||key.size()>96)return Invalid;
    try{
        auto folder=root.empty()?Root():root;std::filesystem::create_directories(folder);auto path=folder/(Hex(key)+".stash");Guard lock(path);Account account;
        if(!Load(path,account))return StorageError;
        auto status=operation(account);if(status==Ok&&!Save(path,account))return StorageError;
        if(result)*result=account;return status;
    }catch(...){return StorageError;}
}
inline void Add(Account& a,int hash,int count){if(count<=0)return;if(AB::Value(hash)<=0||a.stash[hash]>999999-count)throw std::runtime_error("stash capacity");a.stash[hash]+=count;}
inline void Remove(Account& a,int hash,int count){auto it=a.stash.find(hash);if(it==a.stash.end()||count<=0||it->second<count)throw std::runtime_error("stash missing");it->second-=count;if(it->second==0)a.stash.erase(it);}
// 끝난 판에 묶인 계정 풀기: 가져간 장비를 돌려준다 (세션 비정상 종료, 로비 재시작)
inline void Recover(Account& a){
    if(a.carriedWeapon)Add(a,a.carriedWeapon,1);if(a.carriedItem)Add(a,a.carriedItem,a.carriedCount);
    a.lastRun=a.run;a.run.clear();a.ownerPid=0;a.carriedWeapon=a.carriedItem=a.carriedCount=0;
}
// 로비 명령: 0 조회, 1 구매, 2 판매, 3 장착, 4 이전 출격 정리, 5 데모 지급
inline Result Apply(Account& a,const Command& command){
    if(command.version!=1||command.action<0||command.action>5)return Invalid;
    if(command.action==0)return Ok;
    if(command.action==4){if(a.run.empty())return Ok;a.lastRun=a.run;a.run.clear();a.ownerPid=0;a.carriedWeapon=a.carriedItem=a.carriedCount=0;if(!a.stash.count(a.weapon))a.weapon=0;if(!a.stash.count(a.item))a.item=0;return Ok;}
    if(!a.run.empty())return Busy;
    if(command.action==5){if(a.credits>1000000000-1000)return Invalid;a.credits+=1000;return Ok;} // Demo: fixed grant, never client supplied
    if(command.action==3){if(command.slot<0||command.slot>1)return Invalid;auto item=AB::Find(command.itemHash);
        if(command.itemHash!=0&&(command.slot==0?!IsWeapon(command.itemHash):(!item||item->kind==2)))return Invalid;
        if(command.itemHash!=0&&a.stash[command.itemHash]<1)return NotOwned;
        if(command.slot==0)a.weapon=command.itemHash;else a.item=command.itemHash;return Ok;}
    if(command.count<1||command.count>99||AB::Value(command.itemHash)<=0)return Invalid;
    if(command.action==1){int price=BuyPrice(command.itemHash);if(price<=0)return Invalid;auto cost=(std::int64_t)price*command.count;if(a.credits<cost)return Funds;Add(a,command.itemHash,command.count);a.credits-=cost;return Ok;}
    auto found=a.stash.find(command.itemHash);if(found==a.stash.end()||found->second<command.count)return NotOwned;
    auto gain=(std::int64_t)AB::Value(command.itemHash)*command.count;if(a.credits>1000000000-gain)return Invalid;
    Remove(a,command.itemHash,command.count);a.credits+=gain;if(!a.stash.count(command.itemHash)){if(a.weapon==command.itemHash)a.weapon=0;if(a.item==command.itemHash)a.item=0;}return Ok;
}
inline Snapshot View(const Account& a,int sequence,Result status){
    Snapshot s;s.sequence=sequence;s.result=status;s.credits=a.credits;s.weapon=a.weapon;s.item=a.item;s.inRun=!a.run.empty();
    std::map<int,int> entries=a.stash;for(auto& item:AB::Items){int hash=ItemHash::Of(item.id);if(BuyPrice(hash)>0)entries.emplace(hash,0);}
    for(auto [hash,count]:entries){if(s.count>=Capacity)break;s.entries[s.count++]={hash,count,AB::Value(hash),BuyPrice(hash)};}return s;
}
// 출격: 장착한 장비를 보관함에서 꺼내 예약한다 (장착 안 했으면 기본 장비)
inline Result Reserve(Account& a,const std::string& token,Grant& grant){
    if(!a.run.empty())return Busy;
    grant.weapon=a.weapon?a.weapon:ItemHash::Of("sword_001");grant.item=a.item?a.item:ItemHash::Of("health_potion");grant.freeWeapon=a.weapon==0;grant.freeItem=a.item==0;
    auto item=AB::Find(grant.item);if(!item||!IsWeapon(grant.weapon)||item->kind==2)return Invalid;
    if((!grant.freeWeapon&&a.stash[grant.weapon]<1)||(!grant.freeItem&&a.stash[grant.item]<1))return NotOwned;
    grant.count=grant.freeItem?3:item->kind==0?(std::min)(3,a.stash[grant.item]):1;
    if(!grant.freeWeapon)Remove(a,grant.weapon,1);if(!grant.freeItem)Remove(a,grant.item,grant.count);
    a.run=token;a.ownerPid=0;a.carriedWeapon=grant.freeWeapon?0:grant.weapon;a.carriedItem=grant.freeItem?0:grant.item;a.carriedCount=grant.freeItem?0:grant.count;
    return Ok;
}
// 정산: 탈출이면 가방 아이템 입고, refund면 가져간 장비 반환. 같은 판은 한 번만 처리한다
inline Result Settle(Account& a,const std::string& token,const std::map<int,int>& items,bool refund){
    if(a.lastRun==token)return Ok;if(a.run!=token)return Busy;
    if(refund){if(a.carriedWeapon)Add(a,a.carriedWeapon,1);if(a.carriedItem)Add(a,a.carriedItem,a.carriedCount);}
    else for(auto [hash,count]:items)Add(a,hash,count);
    a.run.clear();a.lastRun=token;a.ownerPid=0;a.carriedWeapon=a.carriedItem=a.carriedCount=0;
    if(!a.stash.count(a.weapon))a.weapon=0;if(!a.stash.count(a.item))a.item=0;return Ok;
}
inline std::string RunToken(int sessionId){return std::to_string(GetCurrentProcessId())+"-"+std::to_string(std::chrono::high_resolution_clock::now().time_since_epoch().count())+"-"+std::to_string(sessionId);}
}
