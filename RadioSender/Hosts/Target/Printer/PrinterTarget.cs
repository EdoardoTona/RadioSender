using RadioSender.Helpers;
using RadioSender.Hosts.Common;
using System;
using System.Linq;

namespace RadioSender.Hosts.Target.PosPrinter;

public static class PrinterTarget
{
  public static string FormatPunch(Punch punch, string format, int[]? columnWidths)
  {
    var columns = format
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(columnFormat => new
        {
          Format = columnFormat,
          Value = FormatStringHelper.GetString(punch, columnFormat)
        })
        .ToArray();

    return string.Concat(columns.Select((column, index) =>
    {
      if (index == columns.Length - 1)
        return column.Value.TrimEnd();

      var width = GetColumnWidth(column.Format, index, columnWidths);
      var value = FitColumn(column.Value, width);
      var paddedValue = IsCompetitorIdColumn(column.Format)
          ? value.PadLeft(width)
          : value.PadRight(width);

      return $"{paddedValue} ";
    })).TrimEnd();
  }

  private static int GetColumnWidth(string columnFormat, int index, int[]? columnWidths)
  {
    if (columnWidths != null &&
        index < columnWidths.Length &&
        columnWidths[index] > 0)
      return columnWidths[index];

    if (ContainsAny(columnFormat, "CompetitorId", "Card", "Bib"))
      return 7;

    if (ContainsAny(columnFormat, "Type", "Control"))
      return 7;

    if (ContainsAny(columnFormat, "Time"))
      return 13;

    if (ContainsAny(columnFormat, "Source"))
      return 10;

    if (ContainsAny(columnFormat, "Status"))
      return 3;

    if (ContainsAny(columnFormat, "Cancellation"))
      return 3;

    return 8;
  }

  private static bool ContainsAny(string value, params string[] needles)
  {
    return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
  }

  private static bool IsCompetitorIdColumn(string columnFormat)
  {
    return ContainsAny(columnFormat, "CompetitorId", "Card", "Bib");
  }

  private static string FitColumn(string value, int width)
  {
    value = value.Trim();

    if (value.Length <= width)
      return value;

    return value[..width];
  }

}
