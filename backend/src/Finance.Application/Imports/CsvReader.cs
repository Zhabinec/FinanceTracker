using System.Text;
using Finance.Domain.Common;

namespace Finance.Application.Imports;

// Delimited text only: no formulas, macros or spreadsheet execution.
public static class CsvReader
{
    public const int MaxCharacters = 1_000_000;
    public const int MaxRows = 1000;
    public static IReadOnlyList<string[]> Read(string content, char delimiter)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxCharacters)
            throw new DomainException("CSV должен содержать данные и быть не больше 1 000 000 символов.");
        if (delimiter is not (',' or ';' or '\t')) throw new DomainException("Выберите запятую, точку с запятой или табуляцию.");
        content = content.TrimStart('\uFEFF');
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closed = false;
        void FinishField() { fields.Add(field.ToString()); field.Clear(); closed = false; if (fields.Count > 50) throw new DomainException("Не больше 50 колонок."); }
        void FinishRow()
        {
            FinishField();
            if (fields.Any(f => !string.IsNullOrWhiteSpace(f))) rows.Add(fields.ToArray());
            fields.Clear();
            if (rows.Count > MaxRows + 1) throw new DomainException("За один раз можно импортировать не больше 1000 операций.");
        }
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
            }
            else if (c == delimiter) FinishField();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++; FinishRow(); }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else
            {
                if (closed || c == '"') throw new DomainException("Некорректные кавычки в CSV.");
                field.Append(c);
            }
        }
        if (quoted) throw new DomainException("В CSV не закрыты кавычки.");
        if (field.Length > 0 || fields.Count > 0 || closed) FinishRow();
        if (rows.Count < 2) throw new DomainException("Нужна строка заголовков и хотя бы одна операция.");
        return rows;
    }
}
