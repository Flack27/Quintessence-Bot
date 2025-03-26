using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieBot.Bot.GoogleSheets
{
    public static class SheetUtils
    {
        public static string GetColumnLetter(int columnIndex)
        {
            string column = string.Empty;
            while (columnIndex >= 0)
            {
                column = (char)('A' + (columnIndex % 26)) + column;
                columnIndex = (columnIndex / 26) - 1;
            }
            return column;
        }

        public static int GetColumnIndex(string columnLetter)
        {
            int index = 0;
            for (int i = 0; i < columnLetter.Length; i++)
            {
                index = index * 26 + (columnLetter[i] - 'A' + 1);
            }
            return index - 1;
        }
    }
}
