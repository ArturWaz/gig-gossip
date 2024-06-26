﻿using System.Diagnostics;
using System.Reflection;
using Spectre.Console;

namespace SpectreCLITest;

class KeyPressedEventArgs : EventArgs
{
    public int Line { get; set; }
    public ConsoleKey Key { get; set; }
}

class DataTable
{
    Table table;
    List<string[]> data=new();
    int selectionIdx=0;
    bool endLoop = false;
    Thread thread = null;
    Stopwatch blinker = new Stopwatch();
    bool blink=true;

    public event EventHandler<KeyPressedEventArgs> OnKeyPressed;

    public DataTable(string[] cols)
    {
        table = new Table()
            .Border(TableBorder.Rounded);
        foreach (var c in cols)
            table = table.AddColumn(c);
    }

    public int NumCols { get => table.Columns.Count; }
    public int NumRows { get => data.Count; }

    public void UpdateCell(int ridx, int cidx, string val)
    {
        lock (table)
        {
            data[ridx][cidx] = val;
            table.UpdateCell(ridx, cidx, val);
            Monitor.PulseAll(table);
        }
    }

    public void AddRow(string[] row)
    {
        lock (table)
        {
            table.AddRow(row);
            data.Add(row);
            Monitor.PulseAll(table);
        }
    }

    public void RemoveRow(int idx)
    {
        lock (table)
        {
            table.RemoveRow(idx);
            data.RemoveAt(idx);
            Monitor.PulseAll(table);
        }
    }

    private void HighlightRow(int idx,bool high,bool reset)
    {
        lock (table)
        {
            var row = data[idx];
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if(high)
                    table.UpdateCell(idx, i, "[black on orange1]" + row[i]+"[/]");
                else if(reset)
                    table.UpdateCell(idx, i, "[white]" + row[i] + "[/]");
                else
                    table.UpdateCell(idx, i, "[orange1]" + row[i] + "[/]");
            }
            Monitor.PulseAll(table);
        }

    }

    public void Start()
    {
        thread = new Thread(() =>
        {
            AnsiConsole.Live(table)
            .Start(ctx =>
            {
                ctx.Refresh();
                while (!endLoop)
                {
                    lock (table)
                    {
                        Monitor.Wait(table);
                        ctx.Refresh();
                    }
                }
            });
        });
        thread.Start();

        HighlightRow(selectionIdx, true,false);

        blinker.Start();
        while (true)
        {
            if (blinker.Elapsed.Milliseconds > 500)
            {
                blink = !blink;
                HighlightRow(selectionIdx, blink, false);
                blinker.Restart();
            }
            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey().Key;
                var oldSelectionIdx = selectionIdx;
                if (k == ConsoleKey.DownArrow)
                {
                    selectionIdx += 1;
                    if (selectionIdx >= table.Rows.Count - 1)
                        selectionIdx = table.Rows.Count - 1;
                }
                if (k == ConsoleKey.UpArrow)
                {
                    selectionIdx -= 1;
                    if (selectionIdx < 0)
                        selectionIdx = 0;
                }
                if (k == ConsoleKey.Escape)
                {
                    lock (table)
                    {
                        endLoop = true;
                        Monitor.PulseAll(table);
                    }
                    thread.Join();
                    return;
                }
                if(k==ConsoleKey.Enter)
                {
                    if(OnKeyPressed!=null)
                        OnKeyPressed(this, new KeyPressedEventArgs() {Key=k, Line=selectionIdx});
                }
                if (oldSelectionIdx != selectionIdx)
                    lock (table)
                    {
                        blink = true;
                        HighlightRow(oldSelectionIdx, false,true);
                        HighlightRow(selectionIdx, blink, false);
                        Monitor.PulseAll(table);
                        blinker.Restart();
                    }
            }

        }
    }

}

class Program
{
    static void Main(string[] args)
    {
        DataTable dataTable = new DataTable(new string[] { "", "Name", "Age" });
        dataTable.AddRow(new string[] { "", "Pawel", "47" });
        Thread.Sleep(100);
        dataTable.AddRow(new string[] { "", "Kasia", "43" });
        Thread.Sleep(100);
        dataTable.AddRow(new string[] { "", "Tymon", "13" });
        Thread.Sleep(100);
        dataTable.AddRow(new string[] { "", "Ignas", "10" });

        dataTable.Start();
    }
}

