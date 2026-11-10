using System;

// 확정된 글자와 한글 조합 중인 글자에서 이스터에그 문자열 확인
public sealed class TitleScrapCode
{
    private readonly string code;
    private string buffer = "";

    public TitleScrapCode(string code) => this.code = code;

    // 최근 입력만 보관하며 백스페이스도 반영
    public bool Feed(char value)
    {
        if (value == '\b')
        {
            if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1);
            return false;
        }
        if (char.IsControl(value)) { Clear(); return false; }
        buffer += value;
        if (buffer.Length > code.Length) buffer = buffer.Substring(buffer.Length - code.Length);
        return buffer.EndsWith(code, StringComparison.Ordinal);
    }

    // 예: 확정된 '바' + 아직 조합 중인 '보'
    public bool MatchesComposition(string composition) =>
        !string.IsNullOrEmpty(composition) && (buffer + composition).EndsWith(code, StringComparison.Ordinal);

    public void Clear() => buffer = "";
}
