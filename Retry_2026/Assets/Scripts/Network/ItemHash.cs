// ============================================================================
//  ItemHash
//
//  아이템 문자열 id를 32bit 정수로 변환. FNV-1a 32bit.
//  서버 Common/ItemHash.h의 ItemHash::Of와 반드시 동일한 결과여야 한다.
//
//  서버는 ItemData(ScriptableObject)를 읽을 수 없으므로 문자열 id만 공유하고
//  정수 id는 양쪽이 각자 계산한다. 수동 ID 테이블이 필요 없다.
// ============================================================================
public static class ItemHash
{
    public static int Of(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return 0;

        uint h = 2166136261u;              // FNV offset basis
        for (int i = 0; i < itemId.Length; i++)
        {
            h ^= (byte)itemId[i];         // 서버는 char 단위 → ASCII id만 사용할 것
            h *= 16777619u;               // FNV prime
        }
        return unchecked((int)h);
    }
}
