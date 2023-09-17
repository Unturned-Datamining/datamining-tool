/* This code is based of this GitHub repo:
 * https://github.com/jpierson/to-markdown-table/blob/develop/src/ToMarkdownTable/LinqMarkdownTableExtensions.cs
 * 
 * MIT License
 * 
 * Copyright(c) 2017 Jeff
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

namespace UnturnedDatamining;
/// <summary>
/// Ex: removed sort by name
/// </summary>
internal static class MarkdownTableHelperEx
{
    public static string ToMarkdownTable<T>(this IEnumerable<T> source) where T : struct
    {
        var properties = typeof(T).GetProperties();

        var gettables
            = properties.Select(p => new { p.Name, GetValue = (Func<object, object?>)p.GetValue, Type = p.PropertyType });

        var maxColumnValues = source
            .Select(x => gettables.Select(p => p.GetValue(x)?.ToString()?.Length ?? 0))
            .Union(new[] { gettables.Select(p => p.Name.Length) }) // Include header in column sizes
            .Aggregate(
                new int[gettables.Count()].AsEnumerable(),
                (accumulate, x) => accumulate.Zip(x, Math.Max))
            .ToArray();

        var columnNames = gettables.Select(p => p.Name);

        var headerLine = "| " + string.Join(" | ", columnNames.Select((n, i) => n.PadRight(maxColumnValues[i]))) + " |";

        var isNumeric = new Func<Type, bool>(type =>
            type == typeof(Byte) ||
            type == typeof(SByte) ||
            type == typeof(UInt16) ||
            type == typeof(UInt32) ||
            type == typeof(UInt64) ||
            type == typeof(Int16) ||
            type == typeof(Int32) ||
            type == typeof(Int64) ||
            type == typeof(Decimal) ||
            type == typeof(Double) ||
            type == typeof(Single));

        var rightAlign = new Func<Type, char>(type => isNumeric(type) ? ':' : ' ');

        var headerDataDividerLine =
            "| " +
             string.Join(
                 "| ",
                 gettables.Select((g, i) => new string('-', maxColumnValues[i]) + rightAlign(g.Type))) +
            "|";

        var lines = new[]
            {
                    headerLine,
                    headerDataDividerLine,
                }.Union(
                source
                .Select(s =>
                    "| " + string.Join(" | ", gettables.Select((n, i) => (n.GetValue(s)?.ToString() ?? "").PadRight(maxColumnValues[i]))) + " |"));

        return lines
            .Aggregate((p, c) => p + Environment.NewLine + c);
    }
}
