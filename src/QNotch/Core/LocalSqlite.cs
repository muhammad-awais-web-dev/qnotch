using System.Runtime.InteropServices;

namespace QNotch.Core;

internal static partial class LocalSqlite
{
    /// <summary>One short, read-only SQLite snapshot. Never return a partial result on a busy or changed database.</summary>
    internal static List<string[]> ReadRows(string path, string query)
    {
        var rows = new List<string[]>();
        if (!File.Exists(path)) return rows;
        nint db = 0, stmt = 0;
        try
        {
            if (sqlite3_open_v2(path, out db, OpenReadOnly, null) != 0) throw new IOException($"Opening {path} failed");
            sqlite3_busy_timeout(db, 1000);
            if (sqlite3_prepare_v2(db, query, -1, out stmt, 0) != 0) throw new IOException($"Database schema changed: {Marshal.PtrToStringUTF8(sqlite3_errmsg(db))}");
            int result;
            while ((result = sqlite3_step(stmt)) == Row)
            {
                var row = new string[sqlite3_column_count(stmt)];
                for (var i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i)) ?? "";
                rows.Add(row);
            }
            if (result != Done) throw new IOException($"Reading the database failed: {Marshal.PtrToStringUTF8(sqlite3_errmsg(db))}");
        }
        finally
        {
            if (stmt != 0) sqlite3_finalize(stmt);
            if (db != 0) sqlite3_close_v2(db);
        }
        return rows;
    }

    const int OpenReadOnly = 1, Row = 100, Done = 101;
    const string Lib = "winsqlite3.dll";
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_open_v2(string path, out nint db, int flags, string? vfs);
    [LibraryImport(Lib)] private static partial int sqlite3_busy_timeout(nint db, int ms);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_prepare_v2(nint db, string sql, int bytes, out nint stmt, nint tail);
    [LibraryImport(Lib)] private static partial int sqlite3_step(nint stmt);
    [LibraryImport(Lib)] private static partial nint sqlite3_column_text(nint stmt, int column);
    [LibraryImport(Lib)] private static partial int sqlite3_column_count(nint stmt);
    [LibraryImport(Lib)] private static partial nint sqlite3_errmsg(nint db);
    [LibraryImport(Lib)] private static partial int sqlite3_finalize(nint stmt);
    [LibraryImport(Lib)] private static partial int sqlite3_close_v2(nint db);
}
