using System.Globalization;
using System.Runtime.InteropServices;

namespace Pharmacy;

// Windows includes SQLite; no server or external NuGet package is required.
public sealed class Sqlite : IDisposable
{
    private IntPtr database;
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, IntPtr destructor);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    public Sqlite(string file)
    {
        if (sqlite3_open(file, out database) != 0)
            throw new InvalidOperationException("Не удалось открыть базу данных.");
        Run("PRAGMA foreign_keys=ON");
        Run("PRAGMA busy_timeout=5000");
    }

    public List<string[]> Run(string sql, params object?[] parameters)
    {
        if (sqlite3_prepare_v2(database, sql, -1, out var statement, IntPtr.Zero) != 0)
            throw new InvalidOperationException(Marshal.PtrToStringUTF8(sqlite3_errmsg(database)));
        try
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var value = Convert.ToString(parameters[i], CultureInfo.InvariantCulture) ?? "";
                if (sqlite3_bind_text(statement, i + 1, value, -1, new IntPtr(-1)) != 0)
                    throw new InvalidOperationException("Ошибка параметра запроса.");
            }
            var rows = new List<string[]>();
            int result;
            while ((result = sqlite3_step(statement)) == 100)
            {
                var row = new string[sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++)
                    row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)) ?? "";
                rows.Add(row);
            }
            if (result != 101)
                throw new InvalidOperationException(Marshal.PtrToStringUTF8(sqlite3_errmsg(database)));
            return rows;
        }
        finally
        {
            sqlite3_finalize(statement);
        }
    }

    public int Scalar(string sql, params object?[] parameters) => int.Parse(Run(sql, parameters)[0][0]);

    public void Transaction(Action action)
    {
        Run("BEGIN IMMEDIATE");
        try
        {
            action();
            Run("COMMIT");
        }
        catch
        {
            Run("ROLLBACK");
            throw;
        }
    }

    public void Dispose() => sqlite3_close(database);
}
